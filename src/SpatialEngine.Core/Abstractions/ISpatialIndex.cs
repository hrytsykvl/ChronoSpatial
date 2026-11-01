using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Core.Abstractions;

/// <summary>
/// Common surface implemented by every index under study (brute force, OOP quadtree, flat quadtree)
/// so the benchmark and test suites can drive them identically.
/// </summary>
/// <remarks>
/// Agents are <b>points</b> identified by a dense integer id in <c>[0, Capacity)</c>. The world bounds are
/// fixed at construction; the simulation reflects agents off the border, and indices additionally clamp on
/// insert, so the root never needs to grow.
/// <para>
/// Implementations are <b>not</b> thread safe for mutation. Read-only queries may run concurrently only
/// through overloads that accept a caller-owned traversal stack.
/// </para>
/// </remarks>
public interface ISpatialIndex : IDisposable
{
    /// <summary>Short display name used in benchmark and telemetry output.</summary>
    string Name { get; }

    /// <summary>World extent. Fixed for the lifetime of the index.</summary>
    Aabb WorldBounds { get; }

    /// <summary>Number of agents currently indexed.</summary>
    int Count { get; }

    /// <summary>Diagnostics recorded by the most recent query.</summary>
    QueryStats LastQueryStats { get; }

    /// <summary>
    /// Reserves enough storage for <paramref name="agentCount"/> agents so that no array growth can occur
    /// inside the simulation tick. This is the mechanism that makes the zero-allocation guarantee hold.
    /// </summary>
    void EnsureCapacity(int agentCount);

    /// <summary>Removes every agent but keeps all pooled storage.</summary>
    void Clear();

    void Insert(int id, float x, float y);

    void Remove(int id);

    /// <summary>Moves an already-inserted agent, inserting it if it is not present yet.</summary>
    void Update(int id, float x, float y);

    /// <summary>Bulk move of a dense struct-of-arrays agent set. This is the measured hot path.</summary>
    void UpdateAll(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys);

    /// <summary>Bulk (re)build from a dense struct-of-arrays agent set.</summary>
    void Build(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys);

    /// <summary>
    /// Writes matching agent ids into <paramref name="results"/> and returns the <b>total</b> match count,
    /// which may exceed the buffer length. Inspect <see cref="LastQueryStats"/> for truncation.
    /// </summary>
    int QueryRange(in Aabb box, Span<int> results);

    /// <inheritdoc cref="QueryRange(in Aabb, Span{int})"/>
    int QueryRange(in Circle circle, Span<int> results);

    /// <summary>Streaming query with no result-buffer limit.</summary>
    void QueryRange<TVisitor>(in Aabb box, ref TVisitor visitor)
        where TVisitor : struct, IQueryVisitor, allows ref struct;

    /// <inheritdoc cref="QueryRange{TVisitor}(in Aabb, ref TVisitor)"/>
    void QueryRange<TVisitor>(in Circle circle, ref TVisitor visitor)
        where TVisitor : struct, IQueryVisitor, allows ref struct;
}
