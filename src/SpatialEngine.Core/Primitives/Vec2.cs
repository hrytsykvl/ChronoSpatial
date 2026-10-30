using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SpatialEngine.Core.Primitives;

/// <summary>
/// 8-byte 2D vector value type. Deliberately minimal: no heap identity, no interfaces on the hot path.
/// </summary>
[DebuggerDisplay("({X}, {Y})")]
public readonly struct Vec2 : IEquatable<Vec2>
{
    public readonly float X;
    public readonly float Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static Vec2 Zero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec2 operator /(Vec2 a, float s) => new(a.X / s, a.Y / s);

    public float LengthSquared
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (X * X) + (Y * Y);
    }

    public float Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => MathF.Sqrt(LengthSquared);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DistanceSquared(Vec2 a, Vec2 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec2 Normalized()
    {
        float lenSq = LengthSquared;
        if (lenSq < 1e-12f)
        {
            return Zero;
        }

        float inv = 1f / MathF.Sqrt(lenSq);
        return new Vec2(X * inv, Y * inv);
    }

    public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public override string ToString() => $"({X}, {Y})";
}
