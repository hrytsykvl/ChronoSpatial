using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpatialEngine.Core.Primitives;

/// <summary>
/// 12-byte circle used for radius queries. Broad phase goes through <see cref="Bounds"/>;
/// the narrow phase compares squared distances so no square roots are taken on the hot path.
/// </summary>
[DebuggerDisplay("({X}, {Y}) r={Radius}")]
[StructLayout(LayoutKind.Sequential)]
public readonly struct Circle
{
    public readonly float X;
    public readonly float Y;
    public readonly float Radius;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Circle(float x, float y, float radius)
    {
        X = x;
        Y = y;
        Radius = radius;
    }

    public float RadiusSquared
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Radius * Radius;
    }

    public Aabb Bounds
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(X - Radius, Y - Radius, X + Radius, Y + Radius);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(float x, float y)
    {
        float dx = x - X;
        float dy = y - Y;
        return (dx * dx) + (dy * dy) <= RadiusSquared;
    }

    /// <summary>Closest-point test against a box; used to prune tree nodes during a radius query.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(in Aabb box)
    {
        float nearestX = Math.Clamp(X, box.MinX, box.MaxX);
        float nearestY = Math.Clamp(Y, box.MinY, box.MaxY);
        float dx = X - nearestX;
        float dy = Y - nearestY;
        return (dx * dx) + (dy * dy) <= RadiusSquared;
    }
}
