using BenchmarkDotNet.Attributes;
using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Naive;
using SpatialEngine.Core.Oop;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Benchmarks;

/// <summary>
/// Radius queries at several selectivities. This is the other half of the latency budget: the simulator
/// runs one interactive query per frame on top of the tick.
/// </summary>
/// <remarks>
/// Query radius is expressed as a fraction of world <i>area</i> so the selectivity is comparable regardless
/// of world size. Both query shapes are measured: the <c>Span&lt;int&gt;</c> buffer overload the UI uses, and
/// the generic <c>ref struct</c> visitor overload that streams hits with no result buffer at all.
/// </remarks>
[MemoryDiagnoser]
public class QueryBenchmarks
{
    private float[] _xs = [];
    private float[] _ys = [];
    private int[] _results = [];
    private Circle[] _probes = [];
    private FlatQuadtree _flat = null!;
    private OopQuadtree _oop = null!;
    private BruteForceIndex _brute = null!;

    [Params(100_000)]
    public int AgentCount { get; set; }

    [Params(Distribution.Uniform, Distribution.Clustered)]
    public Distribution Layout { get; set; }

    /// <summary>Query circle area as a fraction of the world area.</summary>
    [Params(0.001f, 0.01f, 0.05f)]
    public float AreaFraction { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _xs = new float[AgentCount];
        _ys = new float[AgentCount];
        Scenario.Fill(_xs, _ys, AgentCount, Layout);
        _results = new int[AgentCount];
        _probes = Scenario.BuildProbes(64, AreaFraction);

        QuadtreeOptions options = QuadtreeOptions.Default(Scenario.World) with { InitialCapacity = AgentCount };
        _flat = new FlatQuadtree(options);
        _flat.Build(_xs, _ys);
        _oop = new OopQuadtree(options);
        _oop.Build(_xs, _ys);
        _brute = new BruteForceIndex(Scenario.World, AgentCount);
        _brute.Build(_xs, _ys);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _flat.Dispose();
        _oop.Dispose();
        _brute.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int Brute_Query() => RunAll(_brute);

    [Benchmark]
    public int Oop_Query() => RunAll(_oop);

    [Benchmark]
    public int Flat_Query_Buffer() => RunAll(_flat);

    [Benchmark]
    public int Flat_Query_Visitor()
    {
        int total = 0;
        for (int p = 0; p < _probes.Length; p++)
        {
            CountingVisitor visitor = default;
            _flat.QueryRange(_probes[p], ref visitor);
            total += visitor.Count;
        }

        return total;
    }

    private int RunAll(ISpatialIndex index)
    {
        int total = 0;
        Span<int> results = _results;
        for (int p = 0; p < _probes.Length; p++)
        {
            total += index.QueryRange(_probes[p], results);
        }

        return total;
    }

    /// <summary>Minimal visitor: measures the traversal without the cost of materialising a result list.</summary>
    private struct CountingVisitor : IQueryVisitor
    {
        public int Count;

        public bool Visit(int id, float x, float y)
        {
            Count++;
            return true;
        }
    }
}
