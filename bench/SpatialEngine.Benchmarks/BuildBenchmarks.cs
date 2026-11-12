using BenchmarkDotNet.Attributes;
using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Naive;
using SpatialEngine.Core.Oop;

namespace SpatialEngine.Benchmarks;

/// <summary>
/// Cost of building an index from scratch over a dense agent set.
/// </summary>
/// <remarks>
/// This is not the hot path, but it bounds the cost of every configuration change in the simulator (agent
/// count slider, MaxDepth toggle) and shows how much allocation the OOP tree does just to come into
/// existence.
/// </remarks>
[MemoryDiagnoser]
public class BuildBenchmarks
{
    private float[] _xs = [];
    private float[] _ys = [];
    private FlatQuadtree _flat = null!;
    private OopQuadtree _oop = null!;
    private BruteForceIndex _brute = null!;

    [Params(10_000, 100_000)]
    public int AgentCount { get; set; }

    [Params(Distribution.Uniform, Distribution.Clustered)]
    public Distribution Layout { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _xs = new float[AgentCount];
        _ys = new float[AgentCount];
        Scenario.Fill(_xs, _ys, AgentCount, Layout);

        QuadtreeOptions options = QuadtreeOptions.Default(Scenario.World) with { InitialCapacity = AgentCount };
        _flat = new FlatQuadtree(options);
        _oop = new OopQuadtree(options);
        _brute = new BruteForceIndex(Scenario.World, AgentCount);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _flat.Dispose();
        _oop.Dispose();
        _brute.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int Brute_Build()
    {
        _brute.Build(_xs, _ys);
        return _brute.Count;
    }

    [Benchmark]
    public int Oop_Build()
    {
        _oop.Build(_xs, _ys);
        return _oop.Count;
    }

    [Benchmark]
    public int Flat_Build()
    {
        _flat.Build(_xs, _ys);
        return _flat.Count;
    }
}
