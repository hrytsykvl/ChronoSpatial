using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Naive;
using SpatialEngine.Core.Oop;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Tests;

/// <summary>
/// Differential tests. <see cref="BruteForceIndex"/> is the oracle: it cannot be wrong about which points are
/// inside a region, so any disagreement is a bug in one of the trees.
/// </summary>
public class DifferentialTests
{
    private static readonly Aabb World = new(0f, 0f, 1024f, 1024f);

    private static (float[] Xs, float[] Ys) MakeUniform(int n, int seed)
    {
        Random rng = new(seed);
        float[] xs = new float[n];
        float[] ys = new float[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = (float)(rng.NextDouble() * World.Width);
            ys[i] = (float)(rng.NextDouble() * World.Height);
        }

        return (xs, ys);
    }

    private static (float[] Xs, float[] Ys) MakeClustered(int n, int seed)
    {
        Random rng = new(seed);
        float[] xs = new float[n];
        float[] ys = new float[n];
        int clusters = 6;
        float[] cx = new float[clusters];
        float[] cy = new float[clusters];
        for (int c = 0; c < clusters; c++)
        {
            cx[c] = (float)(rng.NextDouble() * World.Width);
            cy[c] = (float)(rng.NextDouble() * World.Height);
        }

        for (int i = 0; i < n; i++)
        {
            int c = rng.Next(clusters);
            xs[i] = Math.Clamp(cx[c] + (float)((rng.NextDouble() - 0.5) * 12.0), 0f, World.MaxX);
            ys[i] = Math.Clamp(cy[c] + (float)((rng.NextDouble() - 0.5) * 12.0), 0f, World.MaxY);
        }

        return (xs, ys);
    }

    private static List<int> Sorted(int[] buffer, int count)
    {
        List<int> list = [.. buffer.Take(count)];
        list.Sort();
        return list;
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(6, 8)]
    [InlineData(8, 16)]
    [InlineData(10, 64)]
    public void BoxAndCircleQueries_AgreeWithTheBruteForceOracle(int maxDepth, int capacity)
    {
        const int N = 4000;
        (float[] xs, float[] ys) = MakeUniform(N, 4242 + maxDepth);

        QuadtreeOptions options = QuadtreeOptions.Default(World) with
        {
            MaxDepth = maxDepth,
            NodeCapacity = capacity,
            InitialCapacity = N,
        };

        using BruteForceIndex oracle = new(World, N);
        using FlatQuadtree flat = new(options);
        using OopQuadtree oop = new(options);

        oracle.Build(xs, ys);
        flat.Build(xs, ys);
        oop.Build(xs, ys);

        flat.ValidateInvariants();
        Assert.Equal(N, flat.Count);
        Assert.Equal(N, oop.Count);

        Random rng = new(777);
        int[] a = new int[N];
        int[] b = new int[N];
        int[] c = new int[N];

        for (int q = 0; q < 200; q++)
        {
            float x = (float)(rng.NextDouble() * World.Width);
            float y = (float)(rng.NextDouble() * World.Height);
            float w = (float)(rng.NextDouble() * 200.0) + 1f;
            float h = (float)(rng.NextDouble() * 200.0) + 1f;
            Aabb box = new(x, y, x + w, y + h);

            List<int> expected = Sorted(a, oracle.QueryRange(box, a));
            Assert.Equal(expected, Sorted(b, flat.QueryRange(box, b)));
            Assert.Equal(expected, Sorted(c, oop.QueryRange(box, c)));

            Circle circle = new(x, y, (float)(rng.NextDouble() * 120.0) + 1f);
            expected = Sorted(a, oracle.QueryRange(circle, a));
            Assert.Equal(expected, Sorted(b, flat.QueryRange(circle, b)));
            Assert.Equal(expected, Sorted(c, oop.QueryRange(circle, c)));
        }
    }

    [Fact]
    public void ClusteredLoad_StillAgreesWithTheOracle()
    {
        const int N = 5000;
        (float[] xs, float[] ys) = MakeClustered(N, 31337);

        QuadtreeOptions options = QuadtreeOptions.Default(World) with
        {
            MaxDepth = 8,
            NodeCapacity = 8,
            InitialCapacity = N,
        };

        using BruteForceIndex oracle = new(World, N);
        using FlatQuadtree flat = new(options);

        oracle.Build(xs, ys);
        flat.Build(xs, ys);
        flat.ValidateInvariants();

        Random rng = new(2024);
        int[] a = new int[N];
        int[] b = new int[N];

        for (int q = 0; q < 150; q++)
        {
            float x = (float)(rng.NextDouble() * World.Width);
            float y = (float)(rng.NextDouble() * World.Height);
            Circle circle = new(x, y, (float)(rng.NextDouble() * 300.0) + 1f);

            Assert.Equal(Sorted(a, oracle.QueryRange(circle, a)), Sorted(b, flat.QueryRange(circle, b)));
        }
    }

    [Fact]
    public void ContinuousMovement_KeepsAllThreeIndicesInAgreement()
    {
        const int N = 3000;
        (float[] xs, float[] ys) = MakeUniform(N, 1111);
        float[] vx = new float[N];
        float[] vy = new float[N];
        Random rng = new(2222);
        for (int i = 0; i < N; i++)
        {
            vx[i] = (float)((rng.NextDouble() - 0.5) * 24.0);
            vy[i] = (float)((rng.NextDouble() - 0.5) * 24.0);
        }

        QuadtreeOptions options = QuadtreeOptions.Default(World) with
        {
            MaxDepth = 7,
            NodeCapacity = 8,
            CleanupInterval = 4,
            InitialCapacity = N,
        };

        using BruteForceIndex oracle = new(World, N);
        using FlatQuadtree flat = new(options);
        using OopQuadtree oop = new(options);

        oracle.Build(xs, ys);
        flat.Build(xs, ys);
        oop.Build(xs, ys);

        int[] a = new int[N];
        int[] b = new int[N];
        int[] c = new int[N];

        for (int tick = 0; tick < 60; tick++)
        {
            for (int i = 0; i < N; i++)
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

            oracle.UpdateAll(xs, ys);
            flat.UpdateAll(xs, ys);
            oop.UpdateAll(xs, ys);

            if (tick % 10 == 0)
            {
                flat.ValidateInvariants();
                Circle circle = new(World.CenterX, World.CenterY, 180f);
                List<int> expected = Sorted(a, oracle.QueryRange(circle, a));
                Assert.Equal(expected, Sorted(b, flat.QueryRange(circle, b)));
                Assert.Equal(expected, Sorted(c, oop.QueryRange(circle, c)));
            }
        }

        Assert.Equal(N, flat.Count);
        Assert.Equal(N, oop.Count);
    }

    [Fact]
    public void VisitorAndBufferOverloads_ProduceIdenticalResults()
    {
        const int N = 2000;
        (float[] xs, float[] ys) = MakeUniform(N, 909);

        using FlatQuadtree flat = new(QuadtreeOptions.Default(World) with { InitialCapacity = N });
        flat.Build(xs, ys);

        Circle circle = new(400f, 400f, 150f);
        int[] buffer = new int[N];
        int bufferCount = flat.QueryRange(circle, buffer);

        int[] visited = new int[N];
        ListVisitor visitor = new(visited);
        flat.QueryRange(circle, ref visitor);

        Assert.Equal(bufferCount, visitor.Count);
        Assert.Equal(Sorted(buffer, bufferCount), Sorted(visited, visitor.Count));
    }

    private struct ListVisitor(int[] sink) : IQueryVisitor
    {
        private readonly int[] _sink = sink;

        public int Count { get; private set; }

        public bool Visit(int id, float x, float y)
        {
            _sink[Count++] = id;
            return true;
        }
    }
}
