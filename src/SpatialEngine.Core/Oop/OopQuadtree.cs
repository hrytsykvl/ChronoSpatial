using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Core.Oop;

/// <summary>
/// Classic textbook quadtree: class-based nodes linked by references, <see cref="List{T}"/> buckets, eager
/// merging, and the conventional "remove then re-insert from the root" update.
/// </summary>
/// <remarks>
/// This is the control group for the study, not a straw man: the algorithm is the standard one and its queries
/// are written allocation-free (span recursion), so the measured difference isolates the effect of
/// <i>structure</i> — object graphs, reference chasing and node churn — rather than API shape.
/// <para>
/// The allocation it does exhibit per tick is intrinsic to the design: every split allocates four nodes and
/// four backing lists, every merge orphans them for the GC, and list buckets reallocate as populations shift.
/// Under continuous agent motion that churn never stops, which is exactly the Gen0/Gen1 pressure the flat
/// design eliminates.
/// </para>
/// </remarks>
public sealed class OopQuadtree : ISpatialIndex
{
    private readonly Aabb _world;
    private readonly int _maxDepth;
    private readonly int _nodeCapacity;
    private readonly Dictionary<int, OopEntry> _entries;
    private readonly Dictionary<int, OopQuadNode> _owners;

    private OopQuadNode _root;
    private QueryStats _lastStats;

    public OopQuadtree(QuadtreeOptions options)
    {
        options.Validate();
        _world = options.WorldBounds;
        _maxDepth = options.MaxDepth;
        _nodeCapacity = options.NodeCapacity;
        _entries = new Dictionary<int, OopEntry>(Math.Max(16, options.InitialCapacity));
        _owners = new Dictionary<int, OopQuadNode>(Math.Max(16, options.InitialCapacity));
        _root = new OopQuadNode(_world, 0);
    }

    public OopQuadtree(Aabb world, int maxDepth = 8, int nodeCapacity = 16, int initialCapacity = 1024)
        : this(QuadtreeOptions.Default(world) with
        {
            MaxDepth = maxDepth,
            NodeCapacity = nodeCapacity,
            InitialCapacity = initialCapacity,
        })
    {
    }

    public string Name => "OopQuadtree";

    public Aabb WorldBounds => _world;

    public int Count => _entries.Count;

    public QueryStats LastQueryStats => _lastStats;

    /// <summary>No-op: this implementation cannot pre-reserve its node graph, which is part of the point.</summary>
    public void EnsureCapacity(int agentCount)
    {
    }

    public void Clear()
    {
        _entries.Clear();
        _owners.Clear();
        _root = new OopQuadNode(_world, 0);
    }

    public void Build(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys)
    {
        Clear();
        int n = Math.Min(xs.Length, ys.Length);
        for (int i = 0; i < n; i++)
        {
            Insert(i, xs[i], ys[i]);
        }
    }

    public void Insert(int id, float x, float y)
    {
        _world.ClampPoint(ref x, ref y);
        if (_entries.TryGetValue(id, out OopEntry? existing))
        {
            existing.X = x;
            existing.Y = y;
            InsertInto(_root, existing);
            return;
        }

        OopEntry entry = new(id, x, y);
        _entries[id] = entry;
        InsertInto(_root, entry);
    }

    public void Remove(int id)
    {
        if (!_entries.TryGetValue(id, out OopEntry? entry))
        {
            return;
        }

        DetachFromOwner(id, entry);
        _entries.Remove(id);
    }

    /// <summary>Conventional update: detach from the owning leaf and re-insert from the root.</summary>
    public void Update(int id, float x, float y)
    {
        if (!_entries.TryGetValue(id, out OopEntry? entry))
        {
            Insert(id, x, y);
            return;
        }

        _world.ClampPoint(ref x, ref y);
        DetachFromOwner(id, entry);
        entry.X = x;
        entry.Y = y;
        InsertInto(_root, entry);
    }

    public void UpdateAll(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys)
    {
        int n = Math.Min(xs.Length, ys.Length);
        for (int i = 0; i < n; i++)
        {
            Update(i, xs[i], ys[i]);
        }
    }

    private void DetachFromOwner(int id, OopEntry entry)
    {
        if (!_owners.TryGetValue(id, out OopQuadNode? owner))
        {
            return;
        }

        owner.Entries.Remove(entry);
        _owners.Remove(id);
        TryMerge(owner.Parent);
    }

    private void InsertInto(OopQuadNode node, OopEntry entry)
    {
        while (!node.IsLeaf)
        {
            node = node.ChildFor(entry.X, entry.Y);
        }

        node.Entries.Add(entry);
        _owners[entry.Id] = node;

        if (node.Entries.Count > _nodeCapacity && node.Depth < _maxDepth)
        {
            Split(node);
        }
    }

    private void Split(OopQuadNode node)
    {
        int depth = node.Depth + 1;
        node.NW = new OopQuadNode(node.Bounds.Child(0), depth) { Parent = node };
        node.NE = new OopQuadNode(node.Bounds.Child(1), depth) { Parent = node };
        node.SW = new OopQuadNode(node.Bounds.Child(2), depth) { Parent = node };
        node.SE = new OopQuadNode(node.Bounds.Child(3), depth) { Parent = node };

        List<OopEntry> entries = node.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            OopEntry entry = entries[i];
            OopQuadNode child = node.ChildFor(entry.X, entry.Y);
            child.Entries.Add(entry);
            _owners[entry.Id] = child;
        }

        entries.Clear();

        if (depth < _maxDepth)
        {
            SplitIfNeeded(node.NW);
            SplitIfNeeded(node.NE);
            SplitIfNeeded(node.SW);
            SplitIfNeeded(node.SE);
        }
    }

    private void SplitIfNeeded(OopQuadNode node)
    {
        if (node.Entries.Count > _nodeCapacity && node.Depth < _maxDepth)
        {
            Split(node);
        }
    }

    /// <summary>Eager merge, the conventional strategy. Every collapse orphans four nodes and four lists.</summary>
    private void TryMerge(OopQuadNode? node)
    {
        while (node is not null && !node.IsLeaf)
        {
            OopQuadNode nw = node.NW!, ne = node.NE!, sw = node.SW!, se = node.SE!;
            if (!nw.IsLeaf || !ne.IsLeaf || !sw.IsLeaf || !se.IsLeaf)
            {
                return;
            }

            int total = nw.Entries.Count + ne.Entries.Count + sw.Entries.Count + se.Entries.Count;
            if (total > _nodeCapacity)
            {
                return;
            }

            Absorb(node, nw);
            Absorb(node, ne);
            Absorb(node, sw);
            Absorb(node, se);
            node.NW = node.NE = node.SW = node.SE = null;
            node = node.Parent;
        }
    }

    private void Absorb(OopQuadNode parent, OopQuadNode child)
    {
        List<OopEntry> entries = child.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            parent.Entries.Add(entries[i]);
            _owners[entries[i].Id] = parent;
        }
    }

    public int QueryRange(in Aabb box, Span<int> results)
    {
        _lastStats.Reset();
        int hits = 0;
        QueryBox(_root, box, results, ref hits);
        _lastStats.Hits = hits;
        return hits;
    }

    private void QueryBox(OopQuadNode node, in Aabb box, Span<int> results, ref int hits)
    {
        if (!node.Bounds.Intersects(box))
        {
            return;
        }

        _lastStats.NodesVisited++;
        if (!node.IsLeaf)
        {
            QueryBox(node.NW!, box, results, ref hits);
            QueryBox(node.NE!, box, results, ref hits);
            QueryBox(node.SW!, box, results, ref hits);
            QueryBox(node.SE!, box, results, ref hits);
            return;
        }

        List<OopEntry> entries = node.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            OopEntry entry = entries[i];
            _lastStats.CandidatesTested++;
            if (box.Contains(entry.X, entry.Y))
            {
                if (hits < results.Length)
                {
                    results[hits] = entry.Id;
                }
                else
                {
                    _lastStats.Truncated = true;
                }

                hits++;
            }
        }
    }

    public int QueryRange(in Circle circle, Span<int> results)
    {
        _lastStats.Reset();
        int hits = 0;
        QueryCircle(_root, circle, results, ref hits);
        _lastStats.Hits = hits;
        return hits;
    }

    private void QueryCircle(OopQuadNode node, in Circle circle, Span<int> results, ref int hits)
    {
        if (!circle.Intersects(node.Bounds))
        {
            return;
        }

        _lastStats.NodesVisited++;
        if (!node.IsLeaf)
        {
            QueryCircle(node.NW!, circle, results, ref hits);
            QueryCircle(node.NE!, circle, results, ref hits);
            QueryCircle(node.SW!, circle, results, ref hits);
            QueryCircle(node.SE!, circle, results, ref hits);
            return;
        }

        List<OopEntry> entries = node.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            OopEntry entry = entries[i];
            _lastStats.CandidatesTested++;
            if (circle.Contains(entry.X, entry.Y))
            {
                if (hits < results.Length)
                {
                    results[hits] = entry.Id;
                }
                else
                {
                    _lastStats.Truncated = true;
                }

                hits++;
            }
        }
    }

    public void QueryRange<TVisitor>(in Aabb box, ref TVisitor visitor)
        where TVisitor : struct, IQueryVisitor, allows ref struct
    {
        _lastStats.Reset();
        VisitBox(_root, box, ref visitor);
    }

    private bool VisitBox<TVisitor>(OopQuadNode node, in Aabb box, ref TVisitor visitor)
        where TVisitor : struct, IQueryVisitor, allows ref struct
    {
        if (!node.Bounds.Intersects(box))
        {
            return true;
        }

        _lastStats.NodesVisited++;
        if (!node.IsLeaf)
        {
            return VisitBox(node.NW!, box, ref visitor)
                && VisitBox(node.NE!, box, ref visitor)
                && VisitBox(node.SW!, box, ref visitor)
                && VisitBox(node.SE!, box, ref visitor);
        }

        List<OopEntry> entries = node.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            OopEntry entry = entries[i];
            _lastStats.CandidatesTested++;
            if (box.Contains(entry.X, entry.Y))
            {
                _lastStats.Hits++;
                if (!visitor.Visit(entry.Id, entry.X, entry.Y))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public void QueryRange<TVisitor>(in Circle circle, ref TVisitor visitor)
        where TVisitor : struct, IQueryVisitor, allows ref struct
    {
        _lastStats.Reset();
        VisitCircle(_root, circle, ref visitor);
    }

    private bool VisitCircle<TVisitor>(OopQuadNode node, in Circle circle, ref TVisitor visitor)
        where TVisitor : struct, IQueryVisitor, allows ref struct
    {
        if (!circle.Intersects(node.Bounds))
        {
            return true;
        }

        _lastStats.NodesVisited++;
        if (!node.IsLeaf)
        {
            return VisitCircle(node.NW!, circle, ref visitor)
                && VisitCircle(node.NE!, circle, ref visitor)
                && VisitCircle(node.SW!, circle, ref visitor)
                && VisitCircle(node.SE!, circle, ref visitor);
        }

        List<OopEntry> entries = node.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            OopEntry entry = entries[i];
            _lastStats.CandidatesTested++;
            if (circle.Contains(entry.X, entry.Y))
            {
                _lastStats.Hits++;
                if (!visitor.Visit(entry.Id, entry.X, entry.Y))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Live node count, walked on demand. Useful for comparing structural churn against the flat tree.</summary>
    public int NodeCount => CountNodes(_root);

    private static int CountNodes(OopQuadNode node)
        => node.IsLeaf ? 1 : 1 + CountNodes(node.NW!) + CountNodes(node.NE!) + CountNodes(node.SW!) + CountNodes(node.SE!);

    public void Dispose()
    {
        _entries.Clear();
        _owners.Clear();
        _root = new OopQuadNode(_world, 0);
    }
}
