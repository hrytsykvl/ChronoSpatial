using System.Diagnostics;
using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Naive;
using SpatialEngine.Core.Oop;
using SpatialEngine.Core.Primitives;
using SpatialEngine.Simulation.Movement;

namespace SpatialEngine.Simulation;

/// <summary>
/// Owns the agent swarm, the movement pipeline and the spatial index, and advances them on a fixed timestep
/// that is decoupled from the render frame rate.
/// </summary>
/// <remarks>
/// A tick is strictly: <c>force models -&gt; integration -&gt; index update</c>. Nothing in that sequence
/// allocates once <see cref="SetAgentCount"/> has pre-sized the buffers, which is what lets
/// <see cref="Core.Flat.FlatQuadtree"/> keep its zero-allocation guarantee under a live simulation rather
/// than only in a micro-benchmark.
/// <para>
/// Configuration changes (agent count, tree parameters, index kind) are deliberately expensive and
/// allocating; they happen on user input, never inside a tick.
/// </para>
/// </remarks>
public sealed class SimulationHost : IDisposable
{
    private static readonly double TicksToMicroseconds = 1_000_000.0 / Stopwatch.Frequency;

    private readonly IMovementModel[] _models;
    private readonly int[] _partitionStarts;
    private AgentWorld _agents;
    private ISpatialIndex _index;
    private QuadtreeOptions _options;
    private SpatialIndexKind _indexKind;
    private double _accumulator;
    private long _tickIndex;
    private bool _disposed;

    public SimulationHost(Aabb world, int agentCount = 25_000, QuadtreeOptions? options = null, SpatialIndexKind indexKind = SpatialIndexKind.Flat)
    {
        WorldBounds = world;
        _options = (options ?? QuadtreeOptions.Default(world)) with { WorldBounds = world };
        _indexKind = indexKind;

        _agents = new AgentWorld(world, agentCount);
        Gravity = new GravityWellField();
        Brownian = new BrownianWalk();

        // Force models run first so integration sees the accumulated velocity in the same tick.
        _models = [Gravity, Brownian];
        _partitionStarts = new int[Environment.ProcessorCount + 1];

        _index = CreateIndex(_indexKind, _options, agentCount);
        _index.Build(_agents.X, _agents.Y);
    }

    public Aabb WorldBounds { get; }

    public GravityWellField Gravity { get; }

    public BrownianWalk Brownian { get; }

    public ISpatialIndex Index => _index;

    public AgentWorld Agents => _agents;

    public SpatialIndexKind IndexKind => _indexKind;

    public QuadtreeOptions Options => _options;

    public int AgentCount => _agents.Count;

    public bool Paused { get; set; }

    /// <summary>Fixed simulation rate in ticks per second. The render loop may run faster or slower.</summary>
    public double TickRate { get; set; } = 60.0;

    /// <summary>
    /// Maximum ticks executed in one <see cref="Advance"/> call. Without this cap, a host that falls behind
    /// would try to simulate more and more time per frame and fall further behind every frame.
    /// </summary>
    public int MaxCatchUpTicks { get; set; } = 5;

    /// <summary>
    /// Partitions movement across <see cref="Environment.ProcessorCount"/> threads. Off by default: the
    /// <see cref="System.Threading.Tasks.Parallel"/> machinery allocates, so the zero-allocation claim is only
    /// measured on the serial path, and WebAssembly is single-threaded regardless.
    /// </summary>
    public bool UseParallelMovement { get; set; }

    /// <summary>Ticks between tree-depth measurements, which are O(nodes) and not needed every frame.</summary>
    public int DepthSampleInterval { get; set; } = 30;

    public FrameTelemetry Telemetry;

    /// <summary>
    /// Feeds real elapsed time into the fixed-timestep accumulator and runs as many whole ticks as it covers.
    /// Returns the number of ticks executed.
    /// </summary>
    public int Advance(double elapsedSeconds)
    {
        if (elapsedSeconds <= 0.0 || double.IsNaN(elapsedSeconds))
        {
            return 0;
        }

        double step = 1.0 / Math.Clamp(TickRate, 1.0, 1000.0);

        // Clamp the input itself as well: a backgrounded browser tab can hand us multi-second deltas.
        _accumulator += Math.Min(elapsedSeconds, step * MaxCatchUpTicks);

        int ticks = 0;
        while (_accumulator >= step && ticks < MaxCatchUpTicks)
        {
            Tick((float)step);
            _accumulator -= step;
            ticks++;
        }

        if (_accumulator > step)
        {
            _accumulator = 0.0;
        }

        return ticks;
    }

    /// <summary>Runs exactly one simulation step. Allocation free on the serial path.</summary>
    public void Tick(float dt)
    {
        long tickStart = Stopwatch.GetTimestamp();

        if (!Paused)
        {
            RunMovement(dt);
            long afterMove = Stopwatch.GetTimestamp();

            _index.UpdateAll(_agents.X, _agents.Y);
            long afterUpdate = Stopwatch.GetTimestamp();

            Telemetry.MoveMicroseconds = Microseconds(tickStart, afterMove);
            Telemetry.UpdateMicroseconds = Microseconds(afterMove, afterUpdate);
            _tickIndex++;
        }
        else
        {
            Telemetry.MoveMicroseconds = 0.0;
            Telemetry.UpdateMicroseconds = 0.0;
        }

        Telemetry.TickMicroseconds = Microseconds(tickStart, Stopwatch.GetTimestamp());
        Telemetry.TickIndex = _tickIndex;
        Telemetry.AgentCount = _agents.Count;

        if (_index is FlatQuadtree flat)
        {
            Telemetry.NodeCount = flat.NodeCount;
            Telemetry.Escapes = flat.LastEscapeCount;
            if (DepthSampleInterval > 0 && _tickIndex % DepthSampleInterval == 0)
            {
                Telemetry.TreeDepth = flat.MeasureDepth();
            }
        }
    }

    private void RunMovement(float dt)
    {
        int count = _agents.Count;
        if (count == 0)
        {
            return;
        }

        for (int m = 0; m < _models.Length; m++)
        {
            _models[m].AdvanceState(dt);
        }

        if (!UseParallelMovement || count < 8192 || Environment.ProcessorCount < 2)
        {
            for (int m = 0; m < _models.Length; m++)
            {
                _models[m].StepRange(_agents, dt, 0, count);
            }

            return;
        }

        RunMovementParallel(dt, count);
    }

    private void RunMovementParallel(float dt, int count)
    {
        int workers = Math.Min(Environment.ProcessorCount, _partitionStarts.Length - 1);

        // Partition on vector-width boundaries so no SIMD block straddles two workers.
        int width = System.Numerics.Vector<float>.Count;
        int blocks = (count + width - 1) / width;
        for (int w = 0; w <= workers; w++)
        {
            long block = (long)blocks * w / workers;
            _partitionStarts[w] = (int)Math.Min(count, block * width);
        }

        AgentWorld agents = _agents;
        IMovementModel[] models = _models;
        int[] starts = _partitionStarts;

        // Each model must finish across the whole swarm before the next begins: gravity writes the velocity
        // that integration reads.
        for (int m = 0; m < models.Length; m++)
        {
            IMovementModel model = models[m];
            System.Threading.Tasks.Parallel.For(0, workers, w => model.StepRange(agents, dt, starts[w], starts[w + 1]));
        }
    }

    /// <summary>Timed radius query. Records the elapsed time in <see cref="Telemetry"/> in microseconds.</summary>
    public int QueryCircle(in Circle circle, Span<int> results)
    {
        long start = Stopwatch.GetTimestamp();
        int hits = _index.QueryRange(circle, results);
        Telemetry.QueryMicroseconds = Microseconds(start, Stopwatch.GetTimestamp());
        Telemetry.QueryHits = hits;
        return hits;
    }

    /// <inheritdoc cref="QueryCircle(in Circle, Span{int})"/>
    public int QueryBox(in Aabb box, Span<int> results)
    {
        long start = Stopwatch.GetTimestamp();
        int hits = _index.QueryRange(box, results);
        Telemetry.QueryMicroseconds = Microseconds(start, Stopwatch.GetTimestamp());
        Telemetry.QueryHits = hits;
        return hits;
    }

    /// <summary>
    /// Changes the population and rebuilds the index. Allocating and O(n): driven by the UI slider, never by
    /// a tick.
    /// </summary>
    public void SetAgentCount(int count)
    {
        count = Math.Max(0, count);
        if (count == _agents.Count)
        {
            return;
        }

        _agents.Resize(count);
        _options = _options with { InitialCapacity = Math.Max(_options.InitialCapacity, count) };
        _index.EnsureCapacity(count);
        _index.Build(_agents.X, _agents.Y);
    }

    /// <summary>Rebuilds the index with new tuning parameters. Used by the UI toggles and the sweep harness.</summary>
    public void SetTreeParams(int maxDepth, int nodeCapacity, int? cleanupInterval = null, float? mergeHysteresis = null)
    {
        _options = _options with
        {
            MaxDepth = maxDepth,
            NodeCapacity = nodeCapacity,
            CleanupInterval = cleanupInterval ?? _options.CleanupInterval,
            MergeHysteresis = mergeHysteresis ?? _options.MergeHysteresis,
            InitialCapacity = Math.Max(_options.InitialCapacity, _agents.Count),
        };
        ReplaceIndex(_indexKind);
    }

    /// <summary>Swaps the index implementation while keeping the swarm state intact.</summary>
    public void SetIndexKind(SpatialIndexKind kind)
    {
        if (kind == _indexKind)
        {
            return;
        }

        _indexKind = kind;
        ReplaceIndex(kind);
    }

    /// <summary>Re-seeds every agent from a fresh deterministic stream and rebuilds the index.</summary>
    public void Reseed(uint seed)
    {
        _agents.Reseed(seed);
        _index.Build(_agents.X, _agents.Y);
    }

    /// <summary>
    /// Drops a transient attractor into the world, simulating a traffic hotspot or a swarm converging on a
    /// target. Allocation free, so it is safe to call from a tick if desired.
    /// </summary>
    public int TriggerHotspot(float x, float y, float strength = 1800f, float radius = 0f, float timeToLive = 4f)
    {
        if (radius <= 0f)
        {
            radius = MathF.Max(WorldBounds.Width, WorldBounds.Height) * 0.5f;
        }

        return Gravity.Add(x, y, strength, radius, timeToLive);
    }

    private void ReplaceIndex(SpatialIndexKind kind)
    {
        ISpatialIndex replacement = CreateIndex(kind, _options, Math.Max(_agents.Count, _options.InitialCapacity));
        replacement.Build(_agents.X, _agents.Y);

        ISpatialIndex previous = _index;
        _index = replacement;
        previous.Dispose();
    }

    private static ISpatialIndex CreateIndex(SpatialIndexKind kind, QuadtreeOptions options, int agentCount)
    {
        QuadtreeOptions sized = options with { InitialCapacity = Math.Max(options.InitialCapacity, agentCount) };
        ISpatialIndex index = kind switch
        {
            SpatialIndexKind.Flat => new FlatQuadtree(sized),
            SpatialIndexKind.Oop => new OopQuadtree(sized),
            SpatialIndexKind.BruteForce => new BruteForceIndex(sized.WorldBounds, sized.InitialCapacity),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown index kind."),
        };

        index.EnsureCapacity(agentCount);
        return index;
    }

    private static double Microseconds(long start, long end) => (end - start) * TicksToMicroseconds;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _index.Dispose();
        _agents.Dispose();
    }
}
