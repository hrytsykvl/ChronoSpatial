using System.Runtime.CompilerServices;

namespace SpatialEngine.Core.Internal;

/// <summary>
/// Index-based block recycler backing every flat structure in the engine.
/// <para>
/// Storage is a single pooled <c>T[]</c>. Blocks of <see cref="Stride"/> consecutive slots are handed out by
/// <see cref="Allocate"/> and returned by <see cref="Free"/>. Freed blocks form an intrusive singly-linked
/// stack threaded through <see cref="IFreeListSlot.NextFree"/> of each block's first slot, so recycling needs
/// no side table and performs no managed allocation at all.
/// </para>
/// <para>
/// The quadtree uses two instances: <c>stride = 4</c> for sibling node groups (so one integer locates all four
/// children) and <c>stride = 1</c> for element cells.
/// </para>
/// <para>
/// This is a mutable struct held as a field; never copy it, and never hold a <c>ref</c> returned by the indexer
/// across a call to <see cref="Allocate"/> or <see cref="EnsureCapacity"/>, which may reallocate the backing array.
/// </para>
/// </summary>
internal struct IndexFreeList<T> : IDisposable
    where T : struct, IFreeListSlot
{
    private T[] _items;
    private int _highWater;
    private int _freeHead;
    private int _liveBlocks;
    private int _growthCount;

    public IndexFreeList(int stride, int initialSlots)
    {
        if (stride < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        Stride = stride;
        _items = PooledArray.Rent<T>(Math.Max(stride * 4, initialSlots));
        _highWater = 0;
        _freeHead = -1;
        _liveBlocks = 0;
        _growthCount = 0;
    }

    public int Stride { get; }

    /// <summary>Slots handed out so far (high-water mark), including slots currently on the free list.</summary>
    public readonly int HighWater => _highWater;

    /// <summary>Physical slot capacity of the rented array. Always &gt;= <see cref="HighWater"/>.</summary>
    public readonly int Capacity => _items.Length;

    /// <summary>Number of blocks currently handed out and not freed.</summary>
    public readonly int LiveBlocks => _liveBlocks;

    /// <summary>
    /// How many times the backing array has been reallocated. Tests and the simulation assert this value does
    /// not change across a tick, which is what makes the zero-allocation claim verifiable.
    /// </summary>
    public readonly int GrowthCount => _growthCount;

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _items[index];
    }

    /// <summary>Permanently claims <paramref name="slots"/> slots that are never freed (used for the root node).</summary>
    public int Reserve(int slots)
    {
        EnsureCapacity(_highWater + slots);
        int start = _highWater;
        _highWater += slots;
        return start;
    }

    /// <summary>Returns the base index of a block of <see cref="Stride"/> consecutive slots.</summary>
    public int Allocate()
    {
        if (_freeHead >= 0)
        {
            int reused = _freeHead;
            _freeHead = _items[reused].NextFree;
            _liveBlocks++;
            return reused;
        }

        EnsureCapacity(_highWater + Stride);
        int index = _highWater;
        _highWater += Stride;
        _liveBlocks++;
        return index;
    }

    /// <summary>Pushes the block starting at <paramref name="baseIndex"/> back onto the free list.</summary>
    public void Free(int baseIndex)
    {
        _items[baseIndex].NextFree = _freeHead;
        _freeHead = baseIndex;
        _liveBlocks--;
    }

    public void EnsureCapacity(int slots)
    {
        if (PooledArray.Grow(ref _items, slots))
        {
            _growthCount++;
        }
    }

    public void Clear()
    {
        _highWater = 0;
        _freeHead = -1;
        _liveBlocks = 0;
    }

    /// <summary>Walks the free list; returns the number of free blocks, or -1 if a cycle or out-of-range link is found.</summary>
    public readonly int CountFreeBlocks()
    {
        int seen = 0;
        int guard = (_highWater / Stride) + 2;
        for (int cursor = _freeHead; cursor >= 0; cursor = _items[cursor].NextFree)
        {
            if (cursor >= _highWater || seen > guard)
            {
                return -1;
            }

            seen++;
        }

        return seen;
    }

    public void Dispose()
    {
        T[]? items = _items;
        PooledArray.Return(ref items);
        _items = [];
        _highWater = 0;
        _freeHead = -1;
        _liveBlocks = 0;
    }
}
