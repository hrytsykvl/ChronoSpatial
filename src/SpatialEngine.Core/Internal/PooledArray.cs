using System.Buffers;
using System.Runtime.CompilerServices;

namespace SpatialEngine.Core.Internal;

/// <summary>
/// Thin <see cref="ArrayPool{T}"/> helpers used by every flat container in the engine.
/// <para>
/// Two rules are enforced everywhere these are used:
/// (1) the logical element count is tracked separately, because a rented array is usually
/// <i>larger</i> than requested; (2) growth is only legal outside the simulation tick, which callers
/// assert via <c>GrowthCount</c>.
/// </para>
/// </summary>
internal static class PooledArray
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T[] Rent<T>(int minimumLength) => ArrayPool<T>.Shared.Rent(Math.Max(1, minimumLength));

    public static void Return<T>(ref T[]? array)
    {
        if (array is null)
        {
            return;
        }

        ArrayPool<T>.Shared.Return(array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        array = null;
    }

    /// <summary>
    /// Grows <paramref name="array"/> to at least <paramref name="required"/> elements by doubling,
    /// copying the existing contents and returning the old buffer to the pool.
    /// Returns <see langword="true"/> when a reallocation actually happened.
    /// </summary>
    public static bool Grow<T>(ref T[] array, int required, bool clearNewTail = false)
    {
        if (array.Length >= required)
        {
            return false;
        }

        int target = Math.Max(required, Math.Max(4, array.Length * 2));
        T[] next = ArrayPool<T>.Shared.Rent(target);
        array.AsSpan().CopyTo(next);
        if (clearNewTail)
        {
            Array.Clear(next, array.Length, next.Length - array.Length);
        }

        ArrayPool<T>.Shared.Return(array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        array = next;
        return true;
    }
}
