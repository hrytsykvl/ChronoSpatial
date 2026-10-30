using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace SpatialEngine.Core.Primitives;

/// <summary>
/// Axis-aligned bounding box packed as a single <see cref="Vector128{Single}"/> laid out as
/// (MinX, MinY, MaxX, MaxY). 16 bytes, four boxes per cache line.
/// <para>
/// All overlap/containment predicates are expressed as one vector compare plus a mask test, which keeps
/// them branchless and allocation free. A scalar fallback is used automatically by the JIT when
/// <see cref="Vector128.IsHardwareAccelerated"/> is false (WASM without SIMD, for example).
/// </para>
/// <para>
/// Coordinate convention: Y grows downward (HTML canvas convention), so quadrant 0 is the top-left (NW).
/// All predicates are inclusive on both bounds. Children produced by <see cref="Child"/> share edges with
/// their siblings; descent always uses <see cref="QuadrantIndex"/>, which is half-open, so a point lying
/// exactly on a split line has exactly one owning leaf while still being found by queries touching either side.
/// </para>
/// </summary>
[DebuggerDisplay("[{MinX},{MinY} .. {MaxX},{MaxY}]")]
[StructLayout(LayoutKind.Sequential)]
public readonly struct Aabb : IEquatable<Aabb>
{
    private readonly Vector128<float> _v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Aabb(Vector128<float> v) => _v = v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Aabb(float minX, float minY, float maxX, float maxY)
        => _v = Vector128.Create(minX, minY, maxX, maxY);

    public float MinX { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _v.GetElement(0); }

    public float MinY { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _v.GetElement(1); }

    public float MaxX { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _v.GetElement(2); }

    public float MaxY { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _v.GetElement(3); }

    public float Width { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => MaxX - MinX; }

    public float Height { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => MaxY - MinY; }

    public float CenterX { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (MinX + MaxX) * 0.5f; }

    public float CenterY { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (MinY + MaxY) * 0.5f; }

    public Vec2 Center { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(CenterX, CenterY); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Aabb FromCenter(float centerX, float centerY, float halfWidth, float halfHeight)
        => new(centerX - halfWidth, centerY - halfHeight, centerX + halfWidth, centerY + halfHeight);

    /// <summary>
    /// Inclusive point containment: MinX &lt;= x &lt;= MaxX and MinY &lt;= y &lt;= MaxY.
    /// Implemented as a single 128-bit compare: (MinX, MinY, x, y) &lt;= (x, y, MaxX, MaxY).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(float x, float y)
    {
        Vector64<float> p = Vector64.Create(x, y);
        Vector128<float> lhs = Vector128.Create(_v.GetLower(), p);
        Vector128<float> rhs = Vector128.Create(p, _v.GetUpper());
        return Vector128.LessThanOrEqualAll(lhs, rhs);
    }

    /// <summary>Inclusive overlap test: (A.Min, B.Min) &lt;= (B.Max, A.Max).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(in Aabb other)
    {
        Vector128<float> lhs = Vector128.Create(_v.GetLower(), other._v.GetLower());
        Vector128<float> rhs = Vector128.Create(other._v.GetUpper(), _v.GetUpper());
        return Vector128.LessThanOrEqualAll(lhs, rhs);
    }

    /// <summary>True when <paramref name="other"/> lies entirely inside this box.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(in Aabb other)
    {
        Vector128<float> lhs = Vector128.Create(_v.GetLower(), other._v.GetUpper());
        Vector128<float> rhs = Vector128.Create(other._v.GetLower(), _v.GetUpper());
        return Vector128.LessThanOrEqualAll(lhs, rhs);
    }

    /// <summary>
    /// Half-open quadrant selection used for tree descent: 0 = NW, 1 = NE, 2 = SW, 3 = SE.
    /// A coordinate exactly on the split line resolves to the high-side child, guaranteeing a single owner.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int QuadrantIndex(float x, float y)
    {
        int right = x >= CenterX ? 1 : 0;
        int bottom = y >= CenterY ? 2 : 0;
        return right | bottom;
    }

    /// <summary>
    /// Exact child box for quadrant <paramref name="index"/>. Children are derived by splitting at the
    /// arithmetic center, so the four children tile the parent exactly with no floating-point drift
    /// (the shared edge value is literally the same float in both siblings).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Aabb Child(int index)
    {
        float cx = CenterX;
        float cy = CenterY;
        return index switch
        {
            0 => new Aabb(MinX, MinY, cx, cy),
            1 => new Aabb(cx, MinY, MaxX, cy),
            2 => new Aabb(MinX, cy, cx, MaxY),
            _ => new Aabb(cx, cy, MaxX, MaxY),
        };
    }

    /// <summary>Uniformly expands the box, used by the optional loose-quadtree variant.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Aabb Inflate(float amount)
        => new(MinX - amount, MinY - amount, MaxX + amount, MaxY + amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClampPoint(ref float x, ref float y)
    {
        x = Math.Clamp(x, MinX, MaxX);
        y = Math.Clamp(y, MinY, MaxY);
    }

    public bool Equals(Aabb other) => _v.Equals(other._v);

    public override bool Equals(object? obj) => obj is Aabb other && Equals(other);

    public override int GetHashCode() => _v.GetHashCode();

    public override string ToString() => $"[{MinX}, {MinY} .. {MaxX}, {MaxY}]";
}
