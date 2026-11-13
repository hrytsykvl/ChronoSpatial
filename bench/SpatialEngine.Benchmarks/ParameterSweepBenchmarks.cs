using BenchmarkDotNet.Attributes;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Primitives;
using SpatialEngine.Simulation;
using SpatialEngine.Simulation.Movement;

namespace SpatialEngine.Benchmarks;

/// <summary>
/// Sweeps the two structural parameters of the tree. This produces the latency heat map that the
/// optimisation chapter of the report is built on.
/// </summary>
/// <remarks>
/// The two parameters pull against each other: a deep tree with small leaves prunes aggressively but pays
/// more pointer-free hops and more split/merge churn, while a shallow tree with fat leaves is cheap to
/// maintain but degenerates toward the brute-force scan inside each leaf. The optimum also depends on the
/// distribution, which is why <see cref="Layout"/> is swept alongside.
/// </remarks>
[MemoryDiagnoser]
public class ParameterSweepBenchmarks
{
    private const float Dt = 1f / 60f;

    private AgentWorld _world = null!;
    private BrownianWalk _walk = null!;
    private FlatQuadtree _tree = null!;
    private Circle[] _probes = [];
    private int[] _results = [];

    [Params(100_000)]
    public int AgentCount { get; set; }

    [Params(4, 6, 8, 10)]
    public int MaxDepth { get; set; }

    [Params(4, 8, 16, 32, 64)]
    public int NodeCapacity { get; set; }

    [Params(Distribution.Uniform, Distribution.Clustered)]
    public Distribution Layout { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new AgentWorld(Scenario.World, AgentCount);
        float[] xs = new float[AgentCount];
        float[] ys = new float[AgentCount];
        Scenario.Fill(xs, ys, AgentCount, Layout);
        xs.CopyTo(_world.X);
        ys.CopyTo(_world.Y);

        _walk = new BrownianWalk(seed: 0x5EEDu);
        _tree = new FlatQuadtree(QuadtreeOptions.Default(Scenario.World) with
        {
            MaxDepth = MaxDepth,
            NodeCapacity = NodeCapacity,
            InitialCapacity = AgentCount,
        });
        _tree.Build(_world.X, _world.Y);

        _probes = Scenario.BuildProbes(32, 0.01f);
        _results = new int[AgentCount];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _tree.Dispose();
        _world.Dispose();
    }

    [Benchmark]
    public void Tick()
    {
        _walk.Step(_world, Dt);
        _tree.UpdateAll(_world.X, _world.Y);
    }

    [Benchmark]
    public int Query()
    {
        int total = 0;
        Span<int> results = _results;
        for (int p = 0; p < _probes.Length; p++)
        {
            total += _tree.QueryRange(_probes[p], results);
        }

        return total;
    }
}
