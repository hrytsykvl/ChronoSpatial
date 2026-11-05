using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpatialEngine.Core.Internal;

namespace SpatialEngine.Core.Flat;

/// <summary>
/// One agent's membership record inside a leaf, 20 bytes.
/// <para>
/// The chain is <b>doubly</b> linked. A singly-linked chain would make <c>Unlink</c> O(chain length), which is
/// fine for a well-balanced tree but degenerates badly for the hotspot scenario this engine is built to show
/// off: once a cluster bottoms out at <c>MaxDepth</c>, a single leaf can legitimately hold thousands of agents,
/// and every one of them unlinks on the tick it leaves. The extra 4 bytes buy a hard O(1) bound.
/// </para>
/// <para>
/// <see cref="X"/>/<see cref="Y"/> are cached here rather than read back from the simulation's SoA arrays, so a
/// leaf scan during a query is a linear walk over 20-byte records instead of random access into two float arrays.
/// </para>
/// </summary>
[DebuggerDisplay("Id={Id} ({X},{Y}) prev={Prev} next={Next}")]
[StructLayout(LayoutKind.Sequential)]
internal struct Cell : IFreeListSlot
{
    public int Next;
    public int Prev;
    public int Id;
    public float X;
    public float Y;

    public int NextFree
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => Next;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Next = value;
    }
}
