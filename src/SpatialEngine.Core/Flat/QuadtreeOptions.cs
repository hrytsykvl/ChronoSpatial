using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Core.Flat;

/// <summary>
/// Tuning knobs for the quadtree family. These are exactly the parameters swept by
/// <c>ParameterSweepBenchmarks</c> and exposed as UI controls in the web simulator.
/// </summary>
public readonly record struct QuadtreeOptions
{
    /// <summary>Fixed world extent. The root node never grows.</summary>
    public Aabb WorldBounds { get; init; }

    /// <summary>Maximum subdivision depth. Root is depth 0. Leaves at this depth never split.</summary>
    public int MaxDepth { get; init; }

    /// <summary>A leaf splits once it holds more than this many agents (and depth allows).</summary>
    public int NodeCapacity { get; init; }

    /// <summary>
    /// Number of ticks between deferred <c>Cleanup</c> (merge) passes. Merging is never performed inside
    /// <c>Update</c>; deferring it is the primary defence against split/merge thrash from agents
    /// oscillating on a quadrant border. Set to 0 to disable merging entirely.
    /// </summary>
    public int CleanupInterval { get; init; }

    /// <summary>
    /// A branch whose four children are all leaves collapses only when their combined population drops to
    /// <c>NodeCapacity * MergeHysteresis</c> or below. Values below 1 create a dead band between the split
    /// and merge thresholds, so a cluster hovering around capacity does not oscillate every pass.
    /// </summary>
    public float MergeHysteresis { get; init; }

    /// <summary>Agent count to pre-size storage for, so the hot path never grows an array.</summary>
    public int InitialCapacity { get; init; }

    public static QuadtreeOptions Default(Aabb world) => new()
    {
        WorldBounds = world,
        MaxDepth = 8,
        NodeCapacity = 16,
        CleanupInterval = 8,
        MergeHysteresis = 0.5f,
        InitialCapacity = 1024,
    };

    internal void Validate()
    {
        if (MaxDepth is < 0 or > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDepth), MaxDepth, "MaxDepth must be in [0, 24].");
        }

        if (NodeCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(NodeCapacity), NodeCapacity, "NodeCapacity must be >= 1.");
        }

        if (MergeHysteresis is <= 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(MergeHysteresis), MergeHysteresis, "MergeHysteresis must be in (0, 1].");
        }

        if (WorldBounds.Width <= 0f || WorldBounds.Height <= 0f)
        {
            throw new ArgumentException("WorldBounds must have positive extent.", nameof(WorldBounds));
        }
    }
}
