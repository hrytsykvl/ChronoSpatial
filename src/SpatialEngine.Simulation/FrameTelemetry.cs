namespace SpatialEngine.Simulation;

/// <summary>
/// Per-tick measurements. A plain struct so recording a frame costs nothing on the managed heap and the whole
/// record can be blitted straight into the renderer's telemetry buffer.
/// </summary>
public struct FrameTelemetry
{
    /// <summary>Movement integration time in microseconds.</summary>
    public double MoveMicroseconds;

    /// <summary>Spatial index update time in microseconds.</summary>
    public double UpdateMicroseconds;

    /// <summary>Most recent query time in microseconds.</summary>
    public double QueryMicroseconds;

    /// <summary>Total tick time in microseconds.</summary>
    public double TickMicroseconds;

    public int AgentCount;

    public int NodeCount;

    public int TreeDepth;

    /// <summary>Agents that left their leaf during this tick. The dirty-check hit rate is 1 - Escapes/Agents.</summary>
    public int Escapes;

    public int QueryHits;

    public long TickIndex;

    public readonly double DirtyCheckHitRate => AgentCount == 0 ? 1.0 : 1.0 - ((double)Escapes / AgentCount);

    public override readonly string ToString()
        => $"tick {TickIndex}: move {MoveMicroseconds:F0}us, update {UpdateMicroseconds:F0}us, " +
           $"agents {AgentCount}, nodes {NodeCount}, depth {TreeDepth}, " +
           $"escapes {Escapes} ({DirtyCheckHitRate * 100:F1}% stayed put)";
}
