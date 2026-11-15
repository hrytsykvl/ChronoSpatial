using System.Runtime.InteropServices;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Web.Rendering;

/// <summary>
/// Packs the quadtree's node rectangles into a flat <c>float32</c> stream for the overlay canvas, as
/// <c>(x, y, width, height)</c> quads already converted to screen pixels.
/// </summary>
/// <remarks>
/// The buffer is only refreshed when <see cref="FlatQuadtree.TopologyVersion"/> changes. Node bounds are
/// invariant between splits and merges, so redrawing the wireframe every frame would repeat identical work;
/// on a settled tree the overlay is effectively free.
/// <para>
/// Conversion to screen space happens here rather than in JavaScript so the browser side stays a single
/// <c>Path2D</c> build with no per-rectangle arithmetic beyond <c>rect()</c>.
/// </para>
/// </remarks>
public sealed class WireframeBuffer
{
    private byte[] _bytes = [];
    private int _lastTopologyVersion = -1;

    /// <summary>Number of rectangles currently packed.</summary>
    public int RectCount { get; private set; }

    /// <summary>The packed float32 quads, as bytes for the memory-view marshaller.</summary>
    public ArraySegment<byte> Rects => new(_bytes, 0, RectCount * 4 * sizeof(float));

    /// <summary>Forces the next <see cref="Refresh"/> to rebuild even if the topology has not changed.</summary>
    public void Invalidate() => _lastTopologyVersion = -1;

    /// <summary>
    /// Rebuilds the rectangle stream if the tree's topology changed since the last call. Returns
    /// <see langword="true"/> when the buffer was rewritten.
    /// </summary>
    public bool Refresh(FlatQuadtree tree, int screenWidth, int screenHeight, bool leavesOnly = false)
    {
        if (tree.TopologyVersion == _lastTopologyVersion)
        {
            return false;
        }

        _lastTopologyVersion = tree.TopologyVersion;

        int requiredFloats = Math.Max(4, tree.NodeCount * 4);
        if (_bytes.Length < requiredFloats * sizeof(float))
        {
            // Topology changes are rare and never on the tick path, so growing here is safe.
            _bytes = new byte[requiredFloats * sizeof(float) * 2];
        }

        Span<float> floats = MemoryMarshal.Cast<byte, float>(_bytes.AsSpan());
        RectCount = tree.CollectNodeBounds(floats, leavesOnly);

        Aabb world = tree.WorldBounds;
        float scaleX = screenWidth / world.Width;
        float scaleY = screenHeight / world.Height;

        for (int r = 0; r < RectCount; r++)
        {
            int o = r * 4;
            floats[o] = (floats[o] - world.MinX) * scaleX;
            floats[o + 1] = (floats[o + 1] - world.MinY) * scaleY;
            floats[o + 2] *= scaleX;
            floats[o + 3] *= scaleY;
        }

        return true;
    }
}
