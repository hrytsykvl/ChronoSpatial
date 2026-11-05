using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpatialEngine.Core.Internal;

namespace SpatialEngine.Core.Flat;

/// <summary>
/// The 8-byte quadtree node: pure topology, nothing else.
/// <para>
/// Eight nodes fit in a single 64-byte cache line, and a split or merge rewrites only these two integers.
/// Geometry (<c>Aabb</c>) and lineage (<c>NodeMeta</c>) live in separate parallel arrays so that the
/// topology array stays as dense as possible.
/// </para>
/// <para>Field meaning depends on <see cref="Count"/>:</para>
/// <list type="bullet">
///   <item><description><c>Count &gt;= 0</c> — leaf. <see cref="FirstChild"/> is the head of the element-cell chain (-1 when empty).</description></item>
///   <item><description><c>Count == <see cref="BranchMarker"/></c> — branch. <see cref="FirstChild"/> is the base index of the four contiguous children (NW, NE, SW, SE).</description></item>
///   <item><description><c>Count == <see cref="FreeMarker"/></c> — recycled. <see cref="FirstChild"/> doubles as the free-list link.</description></item>
/// </list>
/// </summary>
[DebuggerDisplay("FirstChild={FirstChild} Count={Count}")]
[StructLayout(LayoutKind.Sequential, Size = 8)]
internal struct QuadNode : IFreeListSlot
{
    public const int BranchMarker = -1;
    public const int FreeMarker = -2;

    public int FirstChild;
    public int Count;

    public int NextFree
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => FirstChild;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => FirstChild = value;
    }

    public readonly bool IsLeaf
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Count >= 0;
    }

    public readonly bool IsBranch
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Count == BranchMarker;
    }
}

/// <summary>
/// Cold-ish per-node data kept out of <see cref="QuadNode"/>: parent link (used only when an agent escapes
/// its leaf) and depth (used only to decide whether a split is still allowed).
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
internal struct NodeMeta
{
    public int Parent;
    public int Depth;
}
