using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Tests;

public class FlatQuadtreeTests
{
    private static readonly Aabb World = new(0f, 0f, 1000f, 1000f);

    private static QuadtreeOptions Options(int maxDepth = 6, int capacity = 4, int cleanupInterval = 1, float hysteresis = 0.5f)
        => QuadtreeOptions.Default(World) with
        {
            MaxDepth = maxDepth,
            NodeCapacity = capacity,
            CleanupInterval = cleanupInterval,
            MergeHysteresis = hysteresis,
            InitialCapacity = 256,
        };

    [Fact]
    public void Insert_BelowCapacity_KeepsRootAsSingleLeaf()
    {
        using FlatQuadtree tree = new(Options(capacity: 4));

        for (int i = 0; i < 4; i++)
        {
            tree.Insert(i, 10f + i, 10f + i);
        }

        Assert.Equal(4, tree.Count);
        Assert.Equal(1, tree.NodeCount);
        tree.ValidateInvariants();
    }

    [Fact]
    public void Insert_AboveCapacity_SplitsAndCascades()
    {
        using FlatQuadtree tree = new(Options(maxDepth: 4, capacity: 4));

        // All five points sit in the same deep corner, forcing a cascade of splits down to MaxDepth.
        for (int i = 0; i < 5; i++)
        {
            tree.Insert(i, 0.5f + (i * 0.001f), 0.5f + (i * 0.001f));
        }

        Assert.Equal(5, tree.Count);
        Assert.True(tree.NodeCount > 1, "tree should have split");
        Assert.Equal(4, tree.MeasureDepth());
        tree.ValidateInvariants();
    }

    [Fact]
    public void LeafAtMaxDepth_MayExceedCapacityWithoutSplitting()
    {
        using FlatQuadtree tree = new(Options(maxDepth: 2, capacity: 2));

        for (int i = 0; i < 50; i++)
        {
            tree.Insert(i, 1f, 1f);
        }

        Assert.Equal(50, tree.Count);
        Assert.Equal(2, tree.MeasureDepth());
        tree.ValidateInvariants();

        int[] hits = new int[64];
        Assert.Equal(50, tree.QueryRange(new Aabb(0f, 0f, 2f, 2f), hits));
    }

    [Fact]
    public void Remove_ThenCleanup_CollapsesBranchesBackToRoot()
    {
        using FlatQuadtree tree = new(Options(maxDepth: 5, capacity: 4));

        for (int i = 0; i < 200; i++)
        {
            tree.Insert(i, (i * 37) % 1000, (i * 71) % 1000);
        }

        int splitNodeCount = tree.NodeCount;
        Assert.True(splitNodeCount > 1);

        for (int i = 0; i < 200; i++)
        {
            tree.Remove(i);
        }

        Assert.Equal(0, tree.Count);
        tree.Cleanup();

        Assert.Equal(1, tree.NodeCount);
        tree.ValidateInvariants();
    }

    [Fact]
    public void FreeList_RecyclesNodesInsteadOfGrowingTheArray()
    {
        using FlatQuadtree tree = new(Options(maxDepth: 5, capacity: 4));
        tree.EnsureCapacity(512);

        for (int cycle = 0; cycle < 10; cycle++)
        {
            for (int i = 0; i < 200; i++)
            {
                tree.Insert(i, (i * 37) % 1000, (i * 71) % 1000);
            }

            for (int i = 0; i < 200; i++)
            {
                tree.Remove(i);
            }

            tree.Cleanup();
            Assert.Equal(1, tree.NodeCount);
        }

        int growthAfterWarmup = tree.GrowthCount;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            for (int i = 0; i < 200; i++)
            {
                tree.Insert(i, (i * 37) % 1000, (i * 71) % 1000);
            }

            for (int i = 0; i < 200; i++)
            {
                tree.Remove(i);
            }

            tree.Cleanup();
        }

        Assert.Equal(growthAfterWarmup, tree.GrowthCount);
        tree.ValidateInvariants();
    }

    [Fact]
    public void Update_MovingAcrossQuadrantBoundary_RelocatesAgent()
    {
        using FlatQuadtree tree = new(Options(maxDepth: 4, capacity: 1));

        tree.Insert(0, 100f, 100f);
        tree.Insert(1, 900f, 900f);
        tree.ValidateInvariants();

        int[] hits = new int[8];
        Assert.Equal(1, tree.QueryRange(new Aabb(0f, 0f, 500f, 500f), hits));
        Assert.Equal(0, hits[0]);

        tree.Update(0, 900f, 100f);
        tree.ValidateInvariants();

        Assert.Equal(0, tree.QueryRange(new Aabb(0f, 0f, 499f, 499f), hits));
        Assert.Equal(1, tree.QueryRange(new Aabb(501f, 0f, 1000f, 499f), hits));
        Assert.Equal(0, hits[0]);
    }

    [Fact]
    public void Update_OscillatingOnABorder_StaysConsistentAndDoesNotThrash()
    {
        using FlatQuadtree tree = new(Options(maxDepth: 6, capacity: 4, cleanupInterval: 4));
        tree.EnsureCapacity(64);

        for (int i = 0; i < 64; i++)
        {
            tree.Insert(i, 500f - 0.5f + (i * 0.001f), 250f + i);
        }

        int growthAfterWarmup = tree.GrowthCount;

        // Jitter every agent back and forth across the x = 500 split line for many ticks.
        float[] xs = new float[64];
        float[] ys = new float[64];
        for (int tick = 0; tick < 200; tick++)
        {
            float offset = (tick % 2 == 0) ? 0.25f : -0.25f;
            for (int i = 0; i < 64; i++)
            {
                xs[i] = 500f + offset;
                ys[i] = 250f + i;
            }

            tree.UpdateAll(xs, ys);
        }

        tree.ValidateInvariants();
        Assert.Equal(64, tree.Count);
        Assert.Equal(growthAfterWarmup, tree.GrowthCount);

        int[] hits = new int[128];
        Assert.Equal(64, tree.QueryRange(new Aabb(499f, 0f, 501f, 1000f), hits));
    }

    [Fact]
    public void Update_AgentDrivenOutsideTheWorld_IsClampedNotLost()
    {
        using FlatQuadtree tree = new(Options());

        tree.Insert(0, 500f, 500f);
        tree.Update(0, -9999f, 9999f);
        tree.ValidateInvariants();

        Assert.Equal(1, tree.Count);
        int[] hits = new int[4];
        Assert.Equal(1, tree.QueryRange(World, hits));
        Assert.Equal(0, hits[0]);
    }

    [Fact]
    public void QueryRange_WithUndersizedBuffer_ReportsTotalAndFlagsTruncation()
    {
        using FlatQuadtree tree = new(Options(capacity: 4));

        for (int i = 0; i < 100; i++)
        {
            tree.Insert(i, 10f + (i * 0.5f), 10f);
        }

        int[] small = new int[10];
        int total = tree.QueryRange(World, small);

        Assert.Equal(100, total);
        Assert.True(tree.LastQueryStats.Truncated);
        Assert.Equal(100, tree.LastQueryStats.Hits);

        int[] big = new int[128];
        Assert.Equal(100, tree.QueryRange(World, big));
        Assert.False(tree.LastQueryStats.Truncated);
    }

    [Fact]
    public void CircleQuery_PrunesCornersThatTheBoundingBoxWouldAccept()
    {
        using FlatQuadtree tree = new(Options(capacity: 2));

        tree.Insert(0, 100f, 100f);  // at the circle centre
        tree.Insert(1, 140f, 140f);  // inside the bounding box, outside the radius

        int[] hits = new int[8];
        int count = tree.QueryRange(new Circle(100f, 100f, 50f), hits);

        Assert.Equal(1, count);
        Assert.Equal(0, hits[0]);
    }

    private ref struct CollectingVisitor : IQueryVisitor
    {
        private readonly Span<int> _sink;
        private int _count;

        public CollectingVisitor(Span<int> sink)
        {
            _sink = sink;
            _count = 0;
        }

        public readonly int Count => _count;

        public bool Visit(int id, float x, float y)
        {
            if (_count < _sink.Length)
            {
                _sink[_count++] = id;
                return true;
            }

            return false;
        }
    }

    [Fact]
    public void VisitorQuery_StreamsIntoCallerMemoryAndCanStopEarly()
    {
        using FlatQuadtree tree = new(Options(capacity: 4));
        for (int i = 0; i < 50; i++)
        {
            tree.Insert(i, 10f + i, 10f);
        }

        Span<int> sink = stackalloc int[50];
        CollectingVisitor all = new(sink);
        tree.QueryRange(World, ref all);
        Assert.Equal(50, all.Count);

        Span<int> limited = stackalloc int[5];
        CollectingVisitor early = new(limited);
        tree.QueryRange(World, ref early);
        Assert.Equal(5, early.Count);
    }

    [Fact]
    public void TopologyVersion_ChangesOnlyWhenTheTreeStructureChanges()
    {
        using FlatQuadtree tree = new(Options(maxDepth: 5, capacity: 4));

        for (int i = 0; i < 40; i++)
        {
            tree.Insert(i, (i * 23) % 1000, (i * 59) % 1000);
        }

        // Settle any pending merges first, so the version only reflects movement from here on.
        tree.Cleanup();
        int version = tree.TopologyVersion;

        // Pure in-leaf movement must not bump the version, so the renderer can skip the wireframe repaint.
        float[] xs = new float[40];
        float[] ys = new float[40];
        for (int i = 0; i < 40; i++)
        {
            xs[i] = (i * 23) % 1000;
            ys[i] = (i * 59) % 1000;
        }

        tree.UpdateAll(xs, ys);
        Assert.Equal(version, tree.TopologyVersion);
    }

    [Fact]
    public void RandomisedOperations_PreserveAllInvariants()
    {
        using FlatQuadtree tree = new(Options(maxDepth: 7, capacity: 8, cleanupInterval: 3));
        tree.EnsureCapacity(500);
        Random rng = new(98765);
        HashSet<int> live = [];

        for (int step = 0; step < 20000; step++)
        {
            int id = rng.Next(500);
            int action = rng.Next(100);

            if (action < 45)
            {
                tree.Insert(id, (float)(rng.NextDouble() * 1000.0), (float)(rng.NextDouble() * 1000.0));
                live.Add(id);
            }
            else if (action < 60)
            {
                tree.Remove(id);
                live.Remove(id);
            }
            else
            {
                tree.Update(id, (float)(rng.NextDouble() * 1000.0), (float)(rng.NextDouble() * 1000.0));
                live.Add(id);
            }

            if (step % 500 == 0)
            {
                tree.Cleanup();
                tree.ValidateInvariants();
                Assert.Equal(live.Count, tree.Count);
            }
        }

        tree.Cleanup();
        tree.ValidateInvariants();
        Assert.Equal(live.Count, tree.Count);
    }
}
