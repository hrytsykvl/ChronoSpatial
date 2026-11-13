using BenchmarkDotNet.Attributes;
using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Naive;
using SpatialEngine.Core.Oop;
using SpatialEngine.Core.Primitives;
using SpatialEngine.Simulation;
using SpatialEngine.Simulation.Movement;

namespace SpatialEngine.Benchmarks;

/// <summary>
/// One complete simulation tick: integrate movement, then bring the index up to date.
/// </summary>
/// <remarks>
/// This is the benchmark the project exists to produce. Besides comparing the three indices it compares
/// three <i>update strategies</i> for the flat tree, so the choice of dirty-checking over the obvious
/// remove-and-reinsert is justified by measurement rather than assertion.
/// <para>
/// Every strategy gets its own agent world seeded identically, so each index always sees a plausible
/// one-tick displacement rather than the accumulated drift of whichever benchmark ran before it.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class UpdateTickBenchmarks
{
    private const float Dt = 1f / 60f;

    private TickScenario _moveOnly = null!;
    private TickScenario _dirty = null!;
    private TickScenario _reinsert = null!;
    private TickScenario _rebuild = null!;
    private TickScenario _oop = null!;
    private TickScenario _brute = null!;

    [Params(10_000, 100_000)]
    public int AgentCount { get; set; }

    [Params(Distribution.Uniform, Distribution.Clustered)]
    public Distribution Layout { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        QuadtreeOptions options = QuadtreeOptions.Default(Scenario.World) with { InitialCapacity = AgentCount };

        _moveOnly = Create(null);
        _dirty = Create(new FlatQuadtree(options));
        _reinsert = Create(new FlatQuadtree(options));
        _rebuild = Create(new FlatQuadtree(options));
        _oop = Create(new OopQuadtree(options));
        _brute = Create(new BruteForceIndex(Scenario.World, AgentCount));

        TickScenario Create(ISpatialIndex? index) => new(AgentCount, Layout, index);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _moveOnly.Dispose();
        _dirty.Dispose();
        _reinsert.Dispose();
        _rebuild.Dispose();
        _oop.Dispose();
        _brute.Dispose();
    }

    /// <summary>Movement integration with no index at all: the floor every other result sits on.</summary>
    [Benchmark(Baseline = true)]
    public void Move_Only() => _moveOnly.Move(Dt);

    [Benchmark]
    public void Flat_DirtyCheck()
    {
        _dirty.Move(Dt);
        _dirty.Index!.UpdateAll(_dirty.World.X, _dirty.World.Y);
    }

    [Benchmark]
    public void Flat_RemoveInsert()
    {
        _reinsert.Move(Dt);
        ISpatialIndex index = _reinsert.Index!;
        ReadOnlySpan<float> xs = _reinsert.World.X;
        ReadOnlySpan<float> ys = _reinsert.World.Y;
        for (int i = 0; i < xs.Length; i++)
        {
            index.Remove(i);
            index.Insert(i, xs[i], ys[i]);
        }
    }

    [Benchmark]
    public void Flat_FullRebuild()
    {
        _rebuild.Move(Dt);
        _rebuild.Index!.Build(_rebuild.World.X, _rebuild.World.Y);
    }

    [Benchmark]
    public void Oop_Update()
    {
        _oop.Move(Dt);
        _oop.Index!.UpdateAll(_oop.World.X, _oop.World.Y);
    }

    [Benchmark]
    public void Brute_Update()
    {
        _brute.Move(Dt);
        _brute.Index!.UpdateAll(_brute.World.X, _brute.World.Y);
    }

    private sealed class TickScenario : IDisposable
    {
        private readonly BrownianWalk _walk = new(seed: 0xBEEFu);

        public TickScenario(int agentCount, Distribution layout, ISpatialIndex? index)
        {
            World = new AgentWorld(Scenario.World, agentCount);

            // Overwrite the world's own spawn with the shared scenario so every suite indexes the same points.
            float[] xs = new float[agentCount];
            float[] ys = new float[agentCount];
            Scenario.Fill(xs, ys, agentCount, layout);
            xs.CopyTo(World.X);
            ys.CopyTo(World.Y);

            Index = index;
            if (index is not null)
            {
                index.EnsureCapacity(agentCount);
                index.Build(World.X, World.Y);
            }
        }

        public AgentWorld World { get; }

        public ISpatialIndex? Index { get; }

        public void Move(float dt) => _walk.Step(World, dt);

        public void Dispose()
        {
            Index?.Dispose();
            World.Dispose();
        }
    }
}
