using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Naive;
using SpatialEngine.Core.Oop;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Tests;

/// <summary>
/// The headline contract of the engine: once capacity is reserved, a simulation tick and a range query must
/// allocate exactly zero bytes on the managed heap, so the GC has nothing to collect and cannot introduce a pause.
/// </summary>
public class ZeroAllocationTests
{
    private static readonly Aabb World = new(0f, 0f, 2048f, 2048f);
    private const int AgentCount = 20_000;

    private static (float[] Xs, float[] Ys, float[] Vx, float[] Vy) MakeAgents(int n, int seed)
    {
        Random rng = new(seed);
        float[] xs = new float[n];
        float[] ys = new float[n];
        float[] vx = new float[n];
        float[] vy = new float[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = (float)(rng.NextDouble() * World.Width);
            ys[i] = (float)(rng.NextDouble() * World.Height);
            vx[i] = (float)((rng.NextDouble() - 0.5) * 8.0);
            vy[i] = (float)((rng.NextDouble() - 0.5) * 8.0);
        }

        return (xs, ys, vx, vy);
    }

    private static void Integrate(float[] xs, float[] ys, float[] vx, float[] vy)
    {
        for (int i = 0; i < xs.Length; i++)
        {
            xs[i] += vx[i];
            ys[i] += vy[i];
            if (xs[i] < World.MinX || xs[i] > World.MaxX)
            {
                vx[i] = -vx[i];
                xs[i] = Math.Clamp(xs[i], World.MinX, World.MaxX);
            }

            if (ys[i] < World.MinY || ys[i] > World.MaxY)
            {
                vy[i] = -vy[i];
                ys[i] = Math.Clamp(ys[i], World.MinY, World.MaxY);
            }
        }
    }

    [Fact]
    public void FlatQuadtree_UpdateTick_AllocatesNothing()
    {
        (float[] xs, float[] ys, float[] vx, float[] vy) = MakeAgents(AgentCount, 5150);

        using FlatQuadtree tree = new(QuadtreeOptions.Default(World) with
        {
            MaxDepth = 8,
            NodeCapacity = 16,
            CleanupInterval = 4,
            InitialCapacity = AgentCount,
        });

        tree.EnsureCapacity(AgentCount);
        tree.Build(xs, ys);

        // Warm up: let the tree reach its steady-state shape and let tiered JIT settle.
        for (int tick = 0; tick < 120; tick++)
        {
            Integrate(xs, ys, vx, vy);
            tree.UpdateAll(xs, ys);
        }

        int growthBefore = tree.GrowthCount;
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int tick = 0; tick < 120; tick++)
        {
            Integrate(xs, ys, vx, vy);
            tree.UpdateAll(xs, ys);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(growthBefore, tree.GrowthCount);
        tree.ValidateInvariants();
    }

    [Fact]
    public void FlatQuadtree_RangeQueries_AllocateNothing()
    {
        (float[] xs, float[] ys, _, _) = MakeAgents(AgentCount, 616);

        using FlatQuadtree tree = new(QuadtreeOptions.Default(World) with
        {
            MaxDepth = 8,
            NodeCapacity = 16,
            InitialCapacity = AgentCount,
        });

        tree.EnsureCapacity(AgentCount);
        tree.Build(xs, ys);

        int[] results = new int[AgentCount];
        int sink = 0;

        for (int i = 0; i < 200; i++)
        {
            sink += tree.QueryRange(new Circle(500f + i, 500f, 120f), results);
            sink += tree.QueryRange(new Aabb(100f, 100f, 400f + i, 400f), results);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 200; i++)
        {
            sink += tree.QueryRange(new Circle(500f + i, 500f, 120f), results);
            sink += tree.QueryRange(new Aabb(100f, 100f, 400f + i, 400f), results);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(sink > 0);
    }

    [Fact]
    public void FlatQuadtree_VisitorQuery_AllocatesNothingEvenWithARefStructVisitor()
    {
        (float[] xs, float[] ys, _, _) = MakeAgents(AgentCount, 4242);

        using FlatQuadtree tree = new(QuadtreeOptions.Default(World) with
        {
            MaxDepth = 8,
            NodeCapacity = 16,
            InitialCapacity = AgentCount,
        });

        tree.EnsureCapacity(AgentCount);
        tree.Build(xs, ys);

        float[] sinkBuffer = new float[AgentCount * 2];
        long total = 0;

        for (int i = 0; i < 100; i++)
        {
            total += RunVisitor(tree, sinkBuffer, i);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100; i++)
        {
            total += RunVisitor(tree, sinkBuffer, i);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(total > 0);
    }

    private static int RunVisitor(FlatQuadtree tree, float[] sinkBuffer, int i)
    {
        SpanWriterVisitor visitor = new(sinkBuffer);
        tree.QueryRange(new Circle(600f + i, 600f, 150f), ref visitor);
        return visitor.Written;
    }

    /// <summary>Writes hit coordinates straight into caller memory, exactly as the renderer does.</summary>
    private ref struct SpanWriterVisitor : IQueryVisitor
    {
        private readonly Span<float> _sink;
        private int _written;

        public SpanWriterVisitor(Span<float> sink)
        {
            _sink = sink;
            _written = 0;
        }

        public readonly int Written => _written;

        public bool Visit(int id, float x, float y)
        {
            int offset = _written * 2;
            if (offset + 1 >= _sink.Length)
            {
                return false;
            }

            _sink[offset] = x;
            _sink[offset + 1] = y;
            _written++;
            return true;
        }
    }

    [Fact]
    public void BruteForceIndex_UpdateTick_AlsoAllocatesNothing()
    {
        (float[] xs, float[] ys, float[] vx, float[] vy) = MakeAgents(AgentCount, 99);

        using BruteForceIndex index = new(World, AgentCount);
        index.Build(xs, ys);

        for (int tick = 0; tick < 50; tick++)
        {
            Integrate(xs, ys, vx, vy);
            index.UpdateAll(xs, ys);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int tick = 0; tick < 50; tick++)
        {
            Integrate(xs, ys, vx, vy);
            index.UpdateAll(xs, ys);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>
    /// The control case. The reference-based quadtree cannot avoid allocating while agents move, because node
    /// churn is intrinsic to its design. This test asserts the contrast the study is built around.
    /// </summary>
    [Fact]
    public void OopQuadtree_UpdateTick_AllocatesOnTheManagedHeap()
    {
        const int N = 8_000;
        (float[] xs, float[] ys, float[] vx, float[] vy) = MakeAgents(N, 31415);
        float[] tx = xs[..N];
        float[] ty = ys[..N];
        float[] tvx = vx[..N];
        float[] tvy = vy[..N];

        using OopQuadtree tree = new(QuadtreeOptions.Default(World) with
        {
            MaxDepth = 8,
            NodeCapacity = 16,
            InitialCapacity = N,
        });

        tree.Build(tx, ty);

        for (int tick = 0; tick < 20; tick++)
        {
            Integrate(tx, ty, tvx, tvy);
            tree.UpdateAll(tx, ty);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int tick = 0; tick < 20; tick++)
        {
            Integrate(tx, ty, tvx, tvy);
            tree.UpdateAll(tx, ty);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated > 0, "the OOP quadtree is expected to allocate during continuous motion");
    }
}
