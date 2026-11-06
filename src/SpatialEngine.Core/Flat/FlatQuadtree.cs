using System.Runtime.CompilerServices;
using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Internal;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Core.Flat;

/// <summary>
/// Zero-allocation, array-backed quadtree for dense integer-identified point agents.
/// </summary>
/// <remarks>
/// <para><b>Memory layout.</b> There are no node objects and no references. Three parallel arrays describe the tree:</para>
/// <list type="bullet">
///   <item><description><c>_nodes</c> — 8-byte <see cref="QuadNode"/> topology records; children are allocated as groups of four, so one integer locates all siblings.</description></item>
///   <item><description><c>_nodeBounds</c> — 16-byte <see cref="Aabb"/> geometry, written only on split and merge.</description></item>
///   <item><description><c>_nodeMeta</c> — 8-byte parent/depth records, read only on the escape and split paths.</description></item>
/// </list>
/// <para>
/// Agent membership lives in a fourth array of 20-byte <see cref="Cell"/> records forming a doubly-linked chain
/// per leaf. Both node groups and cells are recycled through <see cref="IndexFreeList{T}"/>, so steady-state
/// operation never touches the allocator.
/// </para>
/// <para><b>Hot path.</b> <see cref="UpdateAll"/> is dirty-checked: for the overwhelming majority of agents the
/// only work is one SIMD box-contains test against the cached bounds of the leaf they already belong to, plus a
/// 20-byte write of the new position. Only agents that actually leave their leaf are unlinked, and those walk
/// <i>up</i> the parent chain to the nearest containing ancestor rather than re-descending from the root.</para>
/// <para><b>Anti-thrash.</b> Merging never happens inside <see cref="UpdateAll"/>. It is deferred to
/// <see cref="Cleanup"/>, run every <see cref="QuadtreeOptions.CleanupInterval"/> ticks, and a branch only
/// collapses once its children fall to <see cref="QuadtreeOptions.MergeHysteresis"/> of capacity. That dead band
/// between the split and merge thresholds is what stops a cluster hovering at capacity from splitting and
/// merging on alternate passes.</para>
/// <para><b>Threading.</b> Mutation is single-threaded. Concurrent reads are supported only through the
/// overloads that take a caller-owned traversal stack and <see cref="QueryStats"/>.</para>
/// </remarks>
public sealed class FlatQuadtree : ISpatialIndex
{
    private readonly QuadtreeOptions _options;
    private readonly Aabb _world;
    private readonly int _maxDepth;
    private readonly int _nodeCapacity;
    private readonly int _mergeThreshold;

    private IndexFreeList<QuadNode> _nodes;
    private IndexFreeList<Cell> _cells;
    private Aabb[] _nodeBounds;
    private NodeMeta[] _nodeMeta;

    private int[] _agentLeaf;
    private int[] _agentCell;
    private int _agentCapacity;

    private int[] _stack;
    private QueryStats _lastStats;

    private int _count;
    private int _tickCounter;
    private int _escapeCount;
    private int _topologyVersion;
    private int _arrayGrowthCount;
    private bool _disposed;

    public FlatQuadtree(QuadtreeOptions options)
    {
        options.Validate();
        _options = options;
        _world = options.WorldBounds;
        _maxDepth = options.MaxDepth;
        _nodeCapacity = options.NodeCapacity;
        _mergeThreshold = Math.Max(0, (int)(options.NodeCapacity * options.MergeHysteresis));

        int initialAgents = Math.Max(16, options.InitialCapacity);
        _nodes = new IndexFreeList<QuadNode>(4, EstimateNodeSlots(initialAgents));
        _cells = new IndexFreeList<Cell>(1, initialAgents);
        _nodeBounds = PooledArray.Rent<Aabb>(_nodes.Capacity);
        _nodeMeta = PooledArray.Rent<NodeMeta>(_nodes.Capacity);
        _agentLeaf = PooledArray.Rent<int>(initialAgents);
        _agentCell = PooledArray.Rent<int>(initialAgents);
        _agentLeaf.AsSpan().Fill(-1);
        _agentCell.AsSpan().Fill(-1);
        _agentCapacity = Math.Min(_agentLeaf.Length, _agentCell.Length);
        _stack = PooledArray.Rent<int>(RequiredStackSize);

        InitialiseRoot();
    }

    public FlatQuadtree(Aabb world, int maxDepth = 8, int nodeCapacity = 16, int initialCapacity = 1024)
        : this(QuadtreeOptions.Default(world) with
        {
            MaxDepth = maxDepth,
            NodeCapacity = nodeCapacity,
            InitialCapacity = initialCapacity,
        })
    {
    }

    public string Name => "FlatQuadtree";

    public Aabb WorldBounds => _world;

    public int Count => _count;

    public QuadtreeOptions Options => _options;

    public QueryStats LastQueryStats => _lastStats;

    /// <summary>Live node count (allocated node slots minus recycled ones).</summary>
    public int NodeCount => (_nodes.LiveBlocks * 4) + 1;

    /// <summary>Incremented on every split and merge; the renderer repaints the wireframe only when it changes.</summary>
    public int TopologyVersion => _topologyVersion;

    /// <summary>Agents that left their leaf during the most recent <see cref="UpdateAll"/>.</summary>
    public int LastEscapeCount => _escapeCount;

    /// <summary>
    /// Total backing-array reallocations since construction. The zero-allocation contract is that this value
    /// is constant across simulation ticks once <see cref="EnsureCapacity"/> has been called.
    /// </summary>
    public int GrowthCount => _arrayGrowthCount + _nodes.GrowthCount + _cells.GrowthCount;

    /// <summary>Minimum traversal-stack length for the thread-safe query overloads.</summary>
    public int RequiredStackSize => (4 * (_maxDepth + 2)) + 8;

    // ---------------------------------------------------------------- capacity

    private static int EstimateNodeSlots(int agentCount) => Math.Max(64, agentCount + 64);

    public void EnsureCapacity(int agentCount)
    {
        if (agentCount <= 0)
        {
            return;
        }

        if (agentCount > _agentCapacity)
        {
            int before = _agentCapacity;
            if (PooledArray.Grow(ref _agentLeaf, agentCount))
            {
                _arrayGrowthCount++;
            }

            if (PooledArray.Grow(ref _agentCell, agentCount))
            {
                _arrayGrowthCount++;
            }

            _agentLeaf.AsSpan(before).Fill(-1);
            _agentCell.AsSpan(before).Fill(-1);
            _agentCapacity = Math.Min(_agentLeaf.Length, _agentCell.Length);
        }

        _cells.EnsureCapacity(agentCount);

        // A quadtree over N points with leaf capacity C settles at roughly 4N/C nodes for a uniform
        // distribution; clustered loads need more, so reserve generously. Reserving here (outside the tick)
        // is what keeps Insert/Update from ever growing an array.
        int nodeSlots = (((agentCount / Math.Max(1, _nodeCapacity)) + 1) * 8) + 64;
        _nodes.EnsureCapacity(nodeSlots);
        SyncNodeArrays();
    }

    private void SyncNodeArrays()
    {
        int capacity = _nodes.Capacity;
        if (PooledArray.Grow(ref _nodeBounds, capacity))
        {
            _arrayGrowthCount++;
        }

        if (PooledArray.Grow(ref _nodeMeta, capacity))
        {
            _arrayGrowthCount++;
        }
    }

    private void EnsureAgentSlot(int id)
    {
        if ((uint)id < (uint)_agentCapacity)
        {
            return;
        }

        EnsureCapacity(id + 1);
    }

    // ---------------------------------------------------------------- structure

    private void InitialiseRoot()
    {
        int root = _nodes.Reserve(1);
        SyncNodeArrays();
        _nodes[root] = new QuadNode { FirstChild = -1, Count = 0 };
        _nodeBounds[root] = _world;
        _nodeMeta[root] = new NodeMeta { Parent = -1, Depth = 0 };
    }

    public void Clear()
    {
        for (int i = 0; i < _agentCapacity; i++)
        {
            _agentLeaf[i] = -1;
            _agentCell[i] = -1;
        }

        _nodes.Clear();
        _cells.Clear();
        _count = 0;
        _escapeCount = 0;
        _tickCounter = 0;
        _topologyVersion++;
        InitialiseRoot();
    }

    public void Build(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys)
    {
        int n = Math.Min(xs.Length, ys.Length);
        EnsureCapacity(n);
        Clear();
        for (int i = 0; i < n; i++)
        {
            Insert(i, xs[i], ys[i]);
        }
    }

    // ---------------------------------------------------------------- mutation

    public void Insert(int id, float x, float y)
    {
        EnsureAgentSlot(id);
        if (_agentLeaf[id] >= 0)
        {
            Update(id, x, y);
            return;
        }

        _world.ClampPoint(ref x, ref y);
        int leaf = DescendToLeaf(0, x, y);
        int cell = _cells.Allocate();

        ref Cell c = ref _cells[cell];
        c.Id = id;
        c.X = x;
        c.Y = y;

        LinkCell(leaf, cell);
        _agentLeaf[id] = leaf;
        _agentCell[id] = cell;
        _count++;
        MaybeSplit(leaf);
    }

    public void Remove(int id)
    {
        if ((uint)id >= (uint)_agentCapacity)
        {
            return;
        }

        int leaf = _agentLeaf[id];
        if (leaf < 0)
        {
            return;
        }

        int cell = _agentCell[id];
        UnlinkCell(leaf, cell);
        _cells.Free(cell);
        _agentLeaf[id] = -1;
        _agentCell[id] = -1;
        _count--;
    }

    public void Update(int id, float x, float y)
    {
        EnsureAgentSlot(id);
        int leaf = _agentLeaf[id];
        if (leaf < 0)
        {
            Insert(id, x, y);
            return;
        }

        _world.ClampPoint(ref x, ref y);
        int cell = _agentCell[id];

        // Fast path: still inside the leaf we already belong to. One 128-bit compare, one 8-byte write.
        if (_nodeBounds[leaf].Contains(x, y))
        {
            ref Cell stay = ref _cells[cell];
            stay.X = x;
            stay.Y = y;
            return;
        }

        _escapeCount++;
        UnlinkCell(leaf, cell);

        ref Cell moved = ref _cells[cell];
        moved.X = x;
        moved.Y = y;

        // Walk up to the nearest ancestor that still contains the point, then descend from there.
        // The root always contains the (clamped) point, so this terminates at worst at index 0.
        int ancestor = _nodeMeta[leaf].Parent;
        while (ancestor > 0 && !_nodeBounds[ancestor].Contains(x, y))
        {
            ancestor = _nodeMeta[ancestor].Parent;
        }

        if (ancestor < 0)
        {
            ancestor = 0;
        }

        int target = DescendToLeaf(ancestor, x, y);
        LinkCell(target, cell);
        _agentLeaf[id] = target;
        MaybeSplit(target);
    }

    public void UpdateAll(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys)
    {
        int n = Math.Min(xs.Length, ys.Length);
        EnsureCapacity(n);
        _escapeCount = 0;

        for (int i = 0; i < n; i++)
        {
            Update(i, xs[i], ys[i]);
        }

        _tickCounter++;
        if (_options.CleanupInterval > 0 && _tickCounter % _options.CleanupInterval == 0)
        {
            Cleanup();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int DescendToLeaf(int start, float x, float y)
    {
        int node = start;
        while (_nodes[node].Count == QuadNode.BranchMarker)
        {
            node = _nodes[node].FirstChild + _nodeBounds[node].QuadrantIndex(x, y);
        }

        return node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LinkCell(int leaf, int cell)
    {
        int head = _nodes[leaf].FirstChild;
        ref Cell c = ref _cells[cell];
        c.Prev = -1;
        c.Next = head;
        if (head >= 0)
        {
            _cells[head].Prev = cell;
        }

        ref QuadNode node = ref _nodes[leaf];
        node.FirstChild = cell;
        node.Count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UnlinkCell(int leaf, int cell)
    {
        ref Cell c = ref _cells[cell];
        int prev = c.Prev;
        int next = c.Next;

        if (prev >= 0)
        {
            _cells[prev].Next = next;
        }
        else
        {
            _nodes[leaf].FirstChild = next;
        }

        if (next >= 0)
        {
            _cells[next].Prev = prev;
        }

        _nodes[leaf].Count--;
        c.Prev = -1;
        c.Next = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MaybeSplit(int leaf)
    {
        if (_nodes[leaf].Count > _nodeCapacity && _nodeMeta[leaf].Depth < _maxDepth)
        {
            Split(leaf);
        }
    }

    private void Split(int node)
    {
        Aabb bounds = _nodeBounds[node];
        int depth = _nodeMeta[node].Depth + 1;

        int group = _nodes.Allocate();
        SyncNodeArrays();

        for (int i = 0; i < 4; i++)
        {
            int child = group + i;
            _nodes[child] = new QuadNode { FirstChild = -1, Count = 0 };
            _nodeBounds[child] = bounds.Child(i);
            _nodeMeta[child] = new NodeMeta { Parent = node, Depth = depth };
        }

        int cursor = _nodes[node].FirstChild;
        ref QuadNode parent = ref _nodes[node];
        parent.FirstChild = group;
        parent.Count = QuadNode.BranchMarker;

        while (cursor >= 0)
        {
            int next = _cells[cursor].Next;
            int quadrant = bounds.QuadrantIndex(_cells[cursor].X, _cells[cursor].Y);
            int child = group + quadrant;
            LinkCell(child, cursor);
            _agentLeaf[_cells[cursor].Id] = child;
            cursor = next;
        }

        _topologyVersion++;

        // Cascade: an entire cluster may land in one quadrant. Bounded by MaxDepth, so no allocation and no
        // unbounded recursion.
        if (depth < _maxDepth)
        {
            for (int i = 0; i < 4; i++)
            {
                MaybeSplit(group + i);
            }
        }
    }

    /// <summary>
    /// Deferred merge pass. Collapses every branch whose four children are all leaves holding no more than
    /// <c>NodeCapacity * MergeHysteresis</c> agents in total, bottom-up. Allocation free; recursion is bounded
    /// by <see cref="QuadtreeOptions.MaxDepth"/>.
    /// </summary>
    public void Cleanup()
    {
        if (_nodes[0].IsBranch)
        {
            CleanupNode(0);
        }
    }

    /// <summary>Returns the subtree population when the subtree is (now) a leaf, otherwise -1.</summary>
    private int CleanupNode(int node)
    {
        ref QuadNode n = ref _nodes[node];
        if (n.IsLeaf)
        {
            return n.Count;
        }

        int group = n.FirstChild;
        int total = 0;
        bool allLeaves = true;

        for (int i = 0; i < 4; i++)
        {
            int childCount = CleanupNode(group + i);
            if (childCount < 0)
            {
                allLeaves = false;
            }
            else
            {
                total += childCount;
            }
        }

        if (!allLeaves || total > _mergeThreshold)
        {
            return -1;
        }

        MergeChildren(node, group, total);
        return total;
    }

    private void MergeChildren(int node, int group, int total)
    {
        int head = -1;
        int tail = -1;

        for (int i = 0; i < 4; i++)
        {
            int cursor = _nodes[group + i].FirstChild;
            while (cursor >= 0)
            {
                int next = _cells[cursor].Next;
                ref Cell c = ref _cells[cursor];
                c.Prev = tail;
                c.Next = -1;
                if (tail >= 0)
                {
                    _cells[tail].Next = cursor;
                }
                else
                {
                    head = cursor;
                }

                tail = cursor;
                _agentLeaf[c.Id] = node;
                cursor = next;
            }

            _nodes[group + i] = new QuadNode { FirstChild = -1, Count = QuadNode.FreeMarker };
        }

        ref QuadNode parent = ref _nodes[node];
        parent.FirstChild = head;
        parent.Count = total;

        _nodes.Free(group);
        _topologyVersion++;
    }

    // ---------------------------------------------------------------- queries

    public int QueryRange(in Aabb box, Span<int> results)
    {
        _lastStats.Reset();
        return QueryBox(box, results, _stack, ref _lastStats);
    }

    public int QueryRange(in Circle circle, Span<int> results)
    {
        _lastStats.Reset();
        return QueryCircle(circle, results, _stack, ref _lastStats);
    }

    /// <summary>
    /// Thread-safe query overload: the caller supplies the traversal stack (at least
    /// <see cref="RequiredStackSize"/> entries) and the stats sink, so concurrent readers share no state.
    /// </summary>
    public int QueryRange(in Aabb box, Span<int> results, Span<int> stack, ref QueryStats stats)
    {
        stats.Reset();
        return QueryBox(box, results, stack, ref stats);
    }

    /// <inheritdoc cref="QueryRange(in Aabb, Span{int}, Span{int}, ref QueryStats)"/>
    public int QueryRange(in Circle circle, Span<int> results, Span<int> stack, ref QueryStats stats)
    {
        stats.Reset();
        return QueryCircle(circle, results, stack, ref stats);
    }

    private int QueryBox(in Aabb box, Span<int> results, Span<int> stack, ref QueryStats stats)
    {
        int capacity = results.Length;
        int hits = 0;
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int node = stack[--sp];
            if (!_nodeBounds[node].Intersects(box))
            {
                continue;
            }

            stats.NodesVisited++;
            ref QuadNode n = ref _nodes[node];
            if (n.IsBranch)
            {
                int group = n.FirstChild;
                stack[sp++] = group;
                stack[sp++] = group + 1;
                stack[sp++] = group + 2;
                stack[sp++] = group + 3;
                continue;
            }

            for (int cursor = n.FirstChild; cursor >= 0;)
            {
                ref Cell c = ref _cells[cursor];
                stats.CandidatesTested++;
                if (box.Contains(c.X, c.Y))
                {
                    if (hits < capacity)
                    {
                        results[hits] = c.Id;
                    }
                    else
                    {
                        stats.Truncated = true;
                    }

                    hits++;
                }

                cursor = c.Next;
            }
        }

        stats.Hits = hits;
        return hits;
    }

    private int QueryCircle(in Circle circle, Span<int> results, Span<int> stack, ref QueryStats stats)
    {
        int capacity = results.Length;
        int hits = 0;
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int node = stack[--sp];
            if (!circle.Intersects(_nodeBounds[node]))
            {
                continue;
            }

            stats.NodesVisited++;
            ref QuadNode n = ref _nodes[node];
            if (n.IsBranch)
            {
                int group = n.FirstChild;
                stack[sp++] = group;
                stack[sp++] = group + 1;
                stack[sp++] = group + 2;
                stack[sp++] = group + 3;
                continue;
            }

            for (int cursor = n.FirstChild; cursor >= 0;)
            {
                ref Cell c = ref _cells[cursor];
                stats.CandidatesTested++;
                if (circle.Contains(c.X, c.Y))
                {
                    if (hits < capacity)
                    {
                        results[hits] = c.Id;
                    }
                    else
                    {
                        stats.Truncated = true;
                    }

                    hits++;
                }

                cursor = c.Next;
            }
        }

        stats.Hits = hits;
        return hits;
    }

    public void QueryRange<TVisitor>(in Aabb box, ref TVisitor visitor)
        where TVisitor : struct, IQueryVisitor, allows ref struct
    {
        _lastStats.Reset();
        Span<int> stack = _stack;
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int node = stack[--sp];
            if (!_nodeBounds[node].Intersects(box))
            {
                continue;
            }

            _lastStats.NodesVisited++;
            ref QuadNode n = ref _nodes[node];
            if (n.IsBranch)
            {
                int group = n.FirstChild;
                stack[sp++] = group;
                stack[sp++] = group + 1;
                stack[sp++] = group + 2;
                stack[sp++] = group + 3;
                continue;
            }

            for (int cursor = n.FirstChild; cursor >= 0;)
            {
                ref Cell c = ref _cells[cursor];
                _lastStats.CandidatesTested++;
                int next = c.Next;
                if (box.Contains(c.X, c.Y))
                {
                    _lastStats.Hits++;
                    if (!visitor.Visit(c.Id, c.X, c.Y))
                    {
                        return;
                    }
                }

                cursor = next;
            }
        }
    }

    public void QueryRange<TVisitor>(in Circle circle, ref TVisitor visitor)
        where TVisitor : struct, IQueryVisitor, allows ref struct
    {
        _lastStats.Reset();
        Span<int> stack = _stack;
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int node = stack[--sp];
            if (!circle.Intersects(_nodeBounds[node]))
            {
                continue;
            }

            _lastStats.NodesVisited++;
            ref QuadNode n = ref _nodes[node];
            if (n.IsBranch)
            {
                int group = n.FirstChild;
                stack[sp++] = group;
                stack[sp++] = group + 1;
                stack[sp++] = group + 2;
                stack[sp++] = group + 3;
                continue;
            }

            for (int cursor = n.FirstChild; cursor >= 0;)
            {
                ref Cell c = ref _cells[cursor];
                _lastStats.CandidatesTested++;
                int next = c.Next;
                if (circle.Contains(c.X, c.Y))
                {
                    _lastStats.Hits++;
                    if (!visitor.Visit(c.Id, c.X, c.Y))
                    {
                        return;
                    }
                }

                cursor = next;
            }
        }
    }

    // ---------------------------------------------------------------- introspection

    /// <summary>
    /// Streams node rectangles as (minX, minY, width, height) quads for the wireframe overlay.
    /// Returns the number of rectangles found, which may exceed what fits in <paramref name="destination"/>.
    /// </summary>
    public int CollectNodeBounds(Span<float> destination, bool leavesOnly = false)
    {
        int capacity = destination.Length / 4;
        int written = 0;
        Span<int> stack = _stack;
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int node = stack[--sp];
            ref QuadNode n = ref _nodes[node];
            bool isLeaf = n.IsLeaf;

            if (!leavesOnly || isLeaf)
            {
                if (written < capacity)
                {
                    Aabb b = _nodeBounds[node];
                    int o = written * 4;
                    destination[o] = b.MinX;
                    destination[o + 1] = b.MinY;
                    destination[o + 2] = b.Width;
                    destination[o + 3] = b.Height;
                }

                written++;
            }

            if (!isLeaf)
            {
                int group = n.FirstChild;
                stack[sp++] = group;
                stack[sp++] = group + 1;
                stack[sp++] = group + 2;
                stack[sp++] = group + 3;
            }
        }

        return written;
    }

    /// <summary>Deepest level currently in use. O(nodes); intended for telemetry, not the hot path.</summary>
    public int MeasureDepth()
    {
        int maxDepth = 0;
        Span<int> stack = _stack;
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int node = stack[--sp];
            int depth = _nodeMeta[node].Depth;
            if (depth > maxDepth)
            {
                maxDepth = depth;
            }

            ref QuadNode n = ref _nodes[node];
            if (n.IsBranch)
            {
                int group = n.FirstChild;
                stack[sp++] = group;
                stack[sp++] = group + 1;
                stack[sp++] = group + 2;
                stack[sp++] = group + 3;
            }
        }

        return maxDepth;
    }

    /// <summary>
    /// Full structural audit used by the test suite: chain integrity, leaf/agent agreement, geometric
    /// containment, population accounting and free-list sanity. Throws on the first violation.
    /// </summary>
    public void ValidateInvariants()
    {
        int seenAgents = 0;
        Span<int> stack = _stack;
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int node = stack[--sp];
            ref QuadNode n = ref _nodes[node];

            if (n.Count == QuadNode.FreeMarker)
            {
                throw new InvalidOperationException($"Node {node} is reachable but marked free.");
            }

            if (n.IsBranch)
            {
                int group = n.FirstChild;
                for (int i = 0; i < 4; i++)
                {
                    int child = group + i;
                    if (_nodeMeta[child].Parent != node)
                    {
                        throw new InvalidOperationException($"Node {child} has parent {_nodeMeta[child].Parent}, expected {node}.");
                    }

                    if (_nodeMeta[child].Depth != _nodeMeta[node].Depth + 1)
                    {
                        throw new InvalidOperationException($"Node {child} has inconsistent depth.");
                    }

                    if (!_nodeBounds[node].Contains(_nodeBounds[child]))
                    {
                        throw new InvalidOperationException($"Node {child} bounds escape parent {node}.");
                    }

                    stack[sp++] = child;
                }

                continue;
            }

            int counted = 0;
            int prev = -1;
            for (int cursor = n.FirstChild; cursor >= 0;)
            {
                ref Cell c = ref _cells[cursor];
                if (c.Prev != prev)
                {
                    throw new InvalidOperationException($"Cell {cursor} has broken back-link in node {node}.");
                }

                if (_agentLeaf[c.Id] != node)
                {
                    throw new InvalidOperationException($"Agent {c.Id} maps to leaf {_agentLeaf[c.Id]} but sits in {node}.");
                }

                if (_agentCell[c.Id] != cursor)
                {
                    throw new InvalidOperationException($"Agent {c.Id} maps to cell {_agentCell[c.Id]} but sits in {cursor}.");
                }

                if (!_nodeBounds[node].Contains(c.X, c.Y))
                {
                    throw new InvalidOperationException($"Agent {c.Id} at ({c.X},{c.Y}) is outside node {node} bounds {_nodeBounds[node]}.");
                }

                counted++;
                if (counted > _count + 1)
                {
                    throw new InvalidOperationException($"Cycle detected in the cell chain of node {node}.");
                }

                prev = cursor;
                cursor = c.Next;
            }

            if (counted != n.Count)
            {
                throw new InvalidOperationException($"Node {node} reports {n.Count} agents but the chain holds {counted}.");
            }

            seenAgents += counted;
        }

        if (seenAgents != _count)
        {
            throw new InvalidOperationException($"Tree holds {seenAgents} agents but Count reports {_count}.");
        }

        if (_nodes.CountFreeBlocks() < 0)
        {
            throw new InvalidOperationException("Node free list is corrupt (cycle or out-of-range link).");
        }

        if (_cells.CountFreeBlocks() < 0)
        {
            throw new InvalidOperationException("Cell free list is corrupt (cycle or out-of-range link).");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _nodes.Dispose();
        _cells.Dispose();

        Aabb[]? bounds = _nodeBounds;
        NodeMeta[]? meta = _nodeMeta;
        int[]? leaf = _agentLeaf;
        int[]? cell = _agentCell;
        int[]? stack = _stack;
        PooledArray.Return(ref bounds);
        PooledArray.Return(ref meta);
        PooledArray.Return(ref leaf);
        PooledArray.Return(ref cell);
        PooledArray.Return(ref stack);

        _nodeBounds = [];
        _nodeMeta = [];
        _agentLeaf = [];
        _agentCell = [];
        _stack = [];
        _agentCapacity = 0;
    }
}
