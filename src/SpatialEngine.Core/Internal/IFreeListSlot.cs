namespace SpatialEngine.Core.Internal;

/// <summary>
/// Implemented by any value type stored in an <see cref="IndexFreeList{T}"/>.
/// <para>
/// A freed slot has no live payload, so the free list reuses one of the slot's own fields as the
/// "next free block" link. That is what removes the need for a side table and keeps the recycler
/// completely allocation free. The constraint is <c>struct</c>, so the JIT devirtualizes these
/// accessors and no boxing occurs.
/// </para>
/// </summary>
internal interface IFreeListSlot
{
    /// <summary>Overlay of the next-free-block index while the slot sits on the free list.</summary>
    int NextFree { get; set; }
}
