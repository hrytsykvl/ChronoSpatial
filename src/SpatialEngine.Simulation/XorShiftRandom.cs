using System.Numerics;
using System.Runtime.CompilerServices;

namespace SpatialEngine.Simulation;

/// <summary>
/// Allocation-free xorshift32 PRNG held by value.
/// <para>
/// <see cref="Random"/> is a class and its virtual <c>NextDouble</c> is far too slow to call 400k times per
/// tick; this is a two-instruction-per-draw alternative that can live in a register.
/// </para>
/// </summary>
public struct XorShiftRandom(uint seed)
{
    private uint _state = seed == 0 ? 0x9E3779B9u : seed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextUInt()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    /// <summary>Uniform in [0, 1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    /// <summary>Uniform in [-1, 1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextSigned() => (NextFloat() * 2f) - 1f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextRange(float min, float max) => min + (NextFloat() * (max - min));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int NextInt(int exclusiveMax) => (int)((NextUInt() >> 1) % (uint)Math.Max(1, exclusiveMax));
}

/// <summary>
/// Counter-based random source: the value for an agent is a pure hash of its index, so the result does not
/// depend on how the swarm is partitioned across threads.
/// </summary>
/// <remarks>
/// A stateful generator advanced per lane cannot be used here. Its output depends on how many draws preceded
/// it, so splitting the swarm across workers would silently change the simulation. Hashing the agent index
/// instead costs two multiplies and three shifts per draw and makes serial and parallel runs bit-identical,
/// which is what lets the benchmark suite compare them honestly.
/// </remarks>
public static class CounterRandom
{
    private static readonly Vector<uint> LaneIndices = BuildLaneIndices();

    /// <summary>Lowbias32 integer finalizer: strong avalanche for a counter input, no state.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    /// <inheritdoc cref="Mix(uint)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<uint> Mix(Vector<uint> x)
    {
        x ^= Vector.ShiftRightLogical(x, 16);
        x *= new Vector<uint>(0x7FEB352Du);
        x ^= Vector.ShiftRightLogical(x, 15);
        x *= new Vector<uint>(0x846CA68Bu);
        x ^= Vector.ShiftRightLogical(x, 16);
        return x;
    }

    /// <summary>Uniform in [-1, 1) for a single counter value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Signed(uint key) => ((Mix(key) >> 8) * (2f / 16777216f)) - 1f;

    /// <summary>Counter key for agent <paramref name="index"/> on a given stream and tick.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Key(uint seed, uint sequence, uint stream, int index)
        => seed ^ (sequence * 0x9E3779B1u) ^ (stream * 0x85EBCA6Bu) ^ ((uint)index * 0xC2B2AE35u);

    /// <summary>Uniform in [-1, 1) for <see cref="Vector{T}.Count"/> consecutive agents.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<float> Signed(uint seed, uint sequence, uint stream, int firstIndex)
    {
        Vector<uint> keys = new Vector<uint>(seed ^ (sequence * 0x9E3779B1u) ^ (stream * 0x85EBCA6Bu))
                          ^ ((new Vector<uint>((uint)firstIndex) + LaneIndices) * new Vector<uint>(0xC2B2AE35u));
        Vector<float> raw = Vector.ConvertToSingle(Vector.ShiftRightLogical(Mix(keys), 8));
        return (raw * new Vector<float>(2f / 16777216f)) - Vector<float>.One;
    }

    private static Vector<uint> BuildLaneIndices()
    {
        Span<uint> lanes = stackalloc uint[Vector<uint>.Count];
        for (int i = 0; i < lanes.Length; i++)
        {
            lanes[i] = (uint)i;
        }

        return new Vector<uint>(lanes);
    }
}

/// <summary>
/// Width-agnostic vectorised xorshift32. Each SIMD lane carries an independent generator state, so one step
/// produces <see cref="Vector{T}.Count"/> random values for the cost of three shifts and three XORs.
/// </summary>
public struct VectorXorShift
{
    private Vector<uint> _state;

    public VectorXorShift(uint seed)
    {
        Span<uint> lanes = stackalloc uint[Vector<uint>.Count];
        XorShiftRandom scalar = new(seed);
        for (int i = 0; i < lanes.Length; i++)
        {
            uint value = scalar.NextUInt();
            lanes[i] = value == 0 ? 0x9E3779B9u : value;
        }

        _state = new Vector<uint>(lanes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector<uint> NextUInt()
    {
        Vector<uint> x = _state;
        x ^= Vector.ShiftLeft(x, 13);
        x ^= Vector.ShiftRightLogical(x, 17);
        x ^= Vector.ShiftLeft(x, 5);
        _state = x;
        return x;
    }

    /// <summary>Uniform in [-1, 1) for every lane.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector<float> NextSigned()
    {
        Vector<float> raw = Vector.ConvertToSingle(NextUInt());
        return (raw * new Vector<float>(2f / 4294967296f)) - Vector<float>.One;
    }
}
