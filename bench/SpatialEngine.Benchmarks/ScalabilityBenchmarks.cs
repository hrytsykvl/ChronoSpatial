using BenchmarkDotNet.Attributes;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Naive;
using SpatialEngine.Core.Primitives;
using SpatialEngine.Simulation;

namespace SpatialEngine.Benchmarks;

/// <summary>
/// Agent-count sweep: the empirical O(n) versus O(n log n) curve, plus the payoff from partitioning
/// movement across cores.
/// </summary>
/// <remarks>
/// The parallel variant is reported separately because it is explicitly <i>not</i> allocation free -- the
/// <see cref="System.Threading.Tasks.Parallel"/> machinery allocates per call -- and because WebAssembly is
/// single threaded, so only the serial numbers describe the browser simulator.
/// </remarks>
[MemoryDiagnoser]
public class ScalabilityBenchmarks
{
    private const float Dt = 1f / 60f;

    private SimulationHost _serial = null!;
    private SimulationHost _parallel = null!;
    private FlatQuadtree _flat = null!;
    private BruteForceIndex _brute = null!;
    private Circle[] _probes = [];
    private int[] _results = [];

    [Params(1_000, 10_000, 50_000, 100_000, 200_000)]
    public int AgentCount { get; set; }

    [Params(Distribution.Uniform, Distribution.Clustered)]
    public Distribution Layout { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        QuadtreeOptions options = QuadtreeOptions.Default(Scenario.World) with { InitialCapacity = AgentCount };

        _serial = new SimulationHost(Scenario.World, AgentCount, options);
        _parallel = new SimulationHost(Scenario.World, AgentCount, options) { UseParallelMovement = true };

        float[] xs = new float[AgentCount];
        float[] ys = new float[AgentCount];
        Scenario.Fill(xs, ys, AgentCount, Layout);
        xs.CopyTo(_serial.Agents.X);
        ys.CopyTo(_serial.Agents.Y);
        xs.CopyTo(_parallel.Agents.X);
        ys.CopyTo(_parallel.Agents.Y);
        _serial.Index.Build(_serial.Agents.X, _serial.Agents.Y);
        _parallel.Index.Build(_parallel.Agents.X, _parallel.Agents.Y);

        _flat = new FlatQuadtree(options);
        _flat.Build(xs, ys);
        _brute = new BruteForceIndex(Scenario.World, AgentCount);
        _brute.Build(xs, ys);

        _probes = Scenario.BuildProbes(32, 0.01f);
        _results = new int[AgentCount];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serial.Dispose();
        _parallel.Dispose();
        _flat.Dispose();
        _brute.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void Tick_Serial() => _serial.Tick(Dt);

    [Benchmark]
    public void Tick_Parallel() => _parallel.Tick(Dt);

    [Benchmark]
    public int Query_Flat()
    {
        int total = 0;
        Span<int> results = _results;
        for (int p = 0; p < _probes.Length; p++)
        {
            total += _flat.QueryRange(_probes[p], results);
        }

        return total;
    }

    [Benchmark]
    public int Query_Brute()
    {
        int total = 0;
        Span<int> results = _results;
        for (int p = 0; p < _probes.Length; p++)
        {
            total += _brute.QueryRange(_probes[p], results);
        }

        return total;
    }
}
