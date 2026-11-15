using System.Runtime.InteropServices;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Web.Rendering;

/// <summary>
/// CPU-side RGBA framebuffer. Agents are rasterised here in .NET and handed to JavaScript as a single
/// memory view, so the browser does one <c>putImageData</c> per frame regardless of agent count.
/// </summary>
/// <remarks>
/// The alternative, issuing a canvas draw call per agent, costs one JS interop crossing and one path
/// operation per agent and collapses long before 100k. Splatting is a clamp, an index and a 4-byte store per
/// agent, entirely in managed code, and the per-frame cost becomes a fixed memcpy of the framebuffer.
/// <para>
/// Pixels are addressed as <see cref="uint"/> through <see cref="MemoryMarshal"/> so clearing is a vectorised
/// <see cref="Span{T}.Fill"/> and a splat is a single store. Colours are packed little-endian
/// <c>0xAABBGGRR</c>, matching the byte order <c>ImageData</c> expects.
/// </para>
/// </remarks>
public sealed class FrameBuffer
{
    private byte[] _pixels = [];
    private float _scaleX;
    private float _scaleY;

    public FrameBuffer(Aabb world, int width, int height)
    {
        World = world;
        Resize(width, height);
    }

    public Aabb World { get; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The raw RGBA bytes handed to JavaScript. Length is always <c>Width * Height * 4</c>.</summary>
    public ArraySegment<byte> Pixels => new(_pixels, 0, Width * Height * 4);

    /// <summary>Packs a colour into the little-endian RGBA word used by <c>ImageData</c>.</summary>
    public static uint Rgba(byte r, byte g, byte b, byte a = 255)
        => r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    public void Resize(int width, int height)
    {
        Width = Math.Clamp(width, 1, 8192);
        Height = Math.Clamp(height, 1, 8192);

        int required = Width * Height * 4;
        if (_pixels.Length < required)
        {
            _pixels = new byte[required];
        }

        _scaleX = Width / World.Width;
        _scaleY = Height / World.Height;
    }

    public void Clear(uint colour) => AsPixels().Fill(colour);

    /// <summary>Rasterises every agent in a dense struct-of-arrays set.</summary>
    public void SplatAll(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys, uint colour)
    {
        Span<uint> pixels = AsPixels();
        int width = Width;
        int maxX = Width - 1;
        int maxY = Height - 1;
        float minWorldX = World.MinX;
        float minWorldY = World.MinY;
        float scaleX = _scaleX;
        float scaleY = _scaleY;

        for (int i = 0; i < xs.Length; i++)
        {
            int px = (int)((xs[i] - minWorldX) * scaleX);
            int py = (int)((ys[i] - minWorldY) * scaleY);
            if ((uint)px > (uint)maxX || (uint)py > (uint)maxY)
            {
                continue;
            }

            pixels[(py * width) + px] = colour;
        }
    }

    /// <summary>
    /// Rasterises a subset identified by id, drawn as a small cross so individual hits stay visible against
    /// the agent field.
    /// </summary>
    public void SplatSelected(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys, ReadOnlySpan<int> ids, int count, uint colour)
    {
        Span<uint> pixels = AsPixels();
        int width = Width;
        int maxX = Width - 1;
        int maxY = Height - 1;
        count = Math.Min(count, ids.Length);

        for (int k = 0; k < count; k++)
        {
            int id = ids[k];
            if ((uint)id >= (uint)xs.Length)
            {
                continue;
            }

            int px = (int)((xs[id] - World.MinX) * _scaleX);
            int py = (int)((ys[id] - World.MinY) * _scaleY);
            if ((uint)px > (uint)maxX || (uint)py > (uint)maxY)
            {
                continue;
            }

            pixels[(py * width) + px] = colour;
            if (px > 0)
            {
                pixels[(py * width) + px - 1] = colour;
            }

            if (px < maxX)
            {
                pixels[(py * width) + px + 1] = colour;
            }

            if (py > 0)
            {
                pixels[((py - 1) * width) + px] = colour;
            }

            if (py < maxY)
            {
                pixels[((py + 1) * width) + px] = colour;
            }
        }
    }

    private Span<uint> AsPixels() => MemoryMarshal.Cast<byte, uint>(_pixels.AsSpan(0, Width * Height * 4));
}
