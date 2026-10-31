using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace SpatialEngine.Core.Primitives;

/// <summary>
/// Batch (struct-of-arrays) spatial predicates.
/// <para>
/// These operate directly on the simulation's <c>float[] X</c> / <c>float[] Y</c> arrays, testing
/// <see cref="Vector256{Single}.Count"/> (or 128-bit) agents per iteration. Matching lanes are turned
/// into indices with <c>ExtractMostSignificantBits</c> + <see cref="BitOperations.TrailingZeroCount(uint)"/>,
/// which avoids a per-lane branch.
/// </para>
/// <para>All methods are allocation free and have a scalar fallback used when SIMD is unavailable.</para>
/// </summary>
public static class SimdAabb
{
    /// <summary>
    /// Writes the indices of all points inside <paramref name="box"/> into <paramref name="results"/>.
    /// Returns the total number of matches, which may exceed <c>results.Length</c>; in that case only the
    /// first <c>results.Length</c> indices were written (see <c>QueryStats.Truncated</c>).
    /// </summary>
    public static int FilterPoints(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys, in Aabb box, Span<int> results)
    {
        int n = Math.Min(xs.Length, ys.Length);
        int count = 0;
        int i = 0;
        int capacity = results.Length;

        if (Vector256.IsHardwareAccelerated && n >= Vector256<float>.Count)
        {
            ref float xr = ref MemoryMarshal.GetReference(xs);
            ref float yr = ref MemoryMarshal.GetReference(ys);
            Vector256<float> minX = Vector256.Create(box.MinX);
            Vector256<float> minY = Vector256.Create(box.MinY);
            Vector256<float> maxX = Vector256.Create(box.MaxX);
            Vector256<float> maxY = Vector256.Create(box.MaxY);
            int limit = n - Vector256<float>.Count;

            for (; i <= limit; i += Vector256<float>.Count)
            {
                Vector256<float> vx = Vector256.LoadUnsafe(ref xr, (nuint)i);
                Vector256<float> vy = Vector256.LoadUnsafe(ref yr, (nuint)i);
                Vector256<float> mask =
                    Vector256.GreaterThanOrEqual(vx, minX) &
                    Vector256.LessThanOrEqual(vx, maxX) &
                    Vector256.GreaterThanOrEqual(vy, minY) &
                    Vector256.LessThanOrEqual(vy, maxY);

                uint bits = mask.ExtractMostSignificantBits();
                while (bits != 0)
                {
                    int lane = BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    if (count < capacity)
                    {
                        results[count] = i + lane;
                    }

                    count++;
                }
            }
        }
        else if (Vector128.IsHardwareAccelerated && n >= Vector128<float>.Count)
        {
            ref float xr = ref MemoryMarshal.GetReference(xs);
            ref float yr = ref MemoryMarshal.GetReference(ys);
            Vector128<float> minX = Vector128.Create(box.MinX);
            Vector128<float> minY = Vector128.Create(box.MinY);
            Vector128<float> maxX = Vector128.Create(box.MaxX);
            Vector128<float> maxY = Vector128.Create(box.MaxY);
            int limit = n - Vector128<float>.Count;

            for (; i <= limit; i += Vector128<float>.Count)
            {
                Vector128<float> vx = Vector128.LoadUnsafe(ref xr, (nuint)i);
                Vector128<float> vy = Vector128.LoadUnsafe(ref yr, (nuint)i);
                Vector128<float> mask =
                    Vector128.GreaterThanOrEqual(vx, minX) &
                    Vector128.LessThanOrEqual(vx, maxX) &
                    Vector128.GreaterThanOrEqual(vy, minY) &
                    Vector128.LessThanOrEqual(vy, maxY);

                uint bits = mask.ExtractMostSignificantBits();
                while (bits != 0)
                {
                    int lane = BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    if (count < capacity)
                    {
                        results[count] = i + lane;
                    }

                    count++;
                }
            }
        }

        for (; i < n; i++)
        {
            if (box.Contains(xs[i], ys[i]))
            {
                if (count < capacity)
                {
                    results[count] = i;
                }

                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Writes the indices of all points inside <paramref name="circle"/> into <paramref name="results"/>.
    /// Uses squared distances only, so no square roots are evaluated.
    /// </summary>
    public static int FilterPoints(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys, in Circle circle, Span<int> results)
    {
        int n = Math.Min(xs.Length, ys.Length);
        int count = 0;
        int i = 0;
        int capacity = results.Length;

        if (Vector256.IsHardwareAccelerated && n >= Vector256<float>.Count)
        {
            ref float xr = ref MemoryMarshal.GetReference(xs);
            ref float yr = ref MemoryMarshal.GetReference(ys);
            Vector256<float> cx = Vector256.Create(circle.X);
            Vector256<float> cy = Vector256.Create(circle.Y);
            Vector256<float> r2 = Vector256.Create(circle.RadiusSquared);
            int limit = n - Vector256<float>.Count;

            for (; i <= limit; i += Vector256<float>.Count)
            {
                Vector256<float> dx = Vector256.LoadUnsafe(ref xr, (nuint)i) - cx;
                Vector256<float> dy = Vector256.LoadUnsafe(ref yr, (nuint)i) - cy;
                Vector256<float> distSq = (dx * dx) + (dy * dy);
                uint bits = Vector256.LessThanOrEqual(distSq, r2).ExtractMostSignificantBits();
                while (bits != 0)
                {
                    int lane = BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    if (count < capacity)
                    {
                        results[count] = i + lane;
                    }

                    count++;
                }
            }
        }
        else if (Vector128.IsHardwareAccelerated && n >= Vector128<float>.Count)
        {
            ref float xr = ref MemoryMarshal.GetReference(xs);
            ref float yr = ref MemoryMarshal.GetReference(ys);
            Vector128<float> cx = Vector128.Create(circle.X);
            Vector128<float> cy = Vector128.Create(circle.Y);
            Vector128<float> r2 = Vector128.Create(circle.RadiusSquared);
            int limit = n - Vector128<float>.Count;

            for (; i <= limit; i += Vector128<float>.Count)
            {
                Vector128<float> dx = Vector128.LoadUnsafe(ref xr, (nuint)i) - cx;
                Vector128<float> dy = Vector128.LoadUnsafe(ref yr, (nuint)i) - cy;
                Vector128<float> distSq = (dx * dx) + (dy * dy);
                uint bits = Vector128.LessThanOrEqual(distSq, r2).ExtractMostSignificantBits();
                while (bits != 0)
                {
                    int lane = BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    if (count < capacity)
                    {
                        results[count] = i + lane;
                    }

                    count++;
                }
            }
        }

        for (; i < n; i++)
        {
            if (circle.Contains(xs[i], ys[i]))
            {
                if (count < capacity)
                {
                    results[count] = i;
                }

                count++;
            }
        }

        return count;
    }

    /// <summary>Preferred SIMD width in float lanes for the current hardware. Used by tests and telemetry.</summary>
    public static int VectorWidth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Vector256.IsHardwareAccelerated ? Vector256<float>.Count
            : Vector128.IsHardwareAccelerated ? Vector128<float>.Count
            : 1;
    }
}
