namespace SpatialEngine.Core.Abstractions;

/// <summary>
/// Callback contract for the streaming query overload.
/// <para>
/// Implementations are constrained to <c>struct</c> so the call is monomorphised and inlined by the JIT
/// (no interface dispatch, no boxing). The <c>allows ref struct</c> anti-constraint on the query methods
/// additionally permits <c>ref struct</c> visitors that hold a <see cref="Span{T}"/> into caller memory,
/// which is how the renderer streams hits straight into its framebuffer without an intermediate array.
/// </para>
/// </summary>
public interface IQueryVisitor
{
    /// <summary>
    /// Invoked once per match. Return <see langword="false"/> to abort the traversal early
    /// (useful for "any hit" or k-limited queries).
    /// </summary>
    bool Visit(int id, float x, float y);
}
