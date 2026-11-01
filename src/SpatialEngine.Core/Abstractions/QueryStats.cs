namespace SpatialEngine.Core.Abstractions;

/// <summary>
/// Per-query diagnostics. A plain value type so recording it costs nothing on the managed heap.
/// </summary>
public struct QueryStats
{
    /// <summary>Tree nodes popped from the traversal stack.</summary>
    public int NodesVisited;

    /// <summary>Leaf elements examined by the narrow phase.</summary>
    public int CandidatesTested;

    /// <summary>Total matches found, regardless of how many fit in the caller's buffer.</summary>
    public int Hits;

    /// <summary>True when <see cref="Hits"/> exceeded the caller-provided result buffer.</summary>
    public bool Truncated;

    public void Reset()
    {
        NodesVisited = 0;
        CandidatesTested = 0;
        Hits = 0;
        Truncated = false;
    }

    public override readonly string ToString()
        => $"nodes={NodesVisited} candidates={CandidatesTested} hits={Hits} truncated={Truncated}";
}
