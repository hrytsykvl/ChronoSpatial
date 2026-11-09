using System.Buffers;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Simulation;

/// <summary>
/// Struct-of-arrays agent state: four parallel <c>float[]</c> buffers rather than an array of agent objects.
/// </summary>
/// <remarks>
/// The layout is chosen for the movement integrator, which is the only part of a tick that touches every agent
/// unconditionally. With SoA, <c>X</c> and <c>VX</c> stream linearly through the cache and a single
/// <see cref="System.Numerics.Vector{T}"/> load covers 8 agents; an array of structs would interleave four
/// unrelated floats per agent and halve the useful bytes per cache line.
/// <para>
/// Buffers come from <see cref="ArrayPool{T}"/> and are only resized when the agent count changes, never
/// during a tick.
/// </para>
/// </remarks>
public sealed class AgentWorld : IDisposable
{
    private float[] _x;
    private float[] _y;
    private float[] _vx;
    private float[] _vy;
    private int _count;
    private uint _seed;
    private bool _disposed;

    public AgentWorld(Aabb bounds, int count, uint seed = 20260101u)
    {
        Bounds = bounds;
        _seed = seed;
        int capacity = Math.Max(16, count);
        _x = ArrayPool<float>.Shared.Rent(capacity);
        _y = ArrayPool<float>.Shared.Rent(capacity);
        _vx = ArrayPool<float>.Shared.Rent(capacity);
        _vy = ArrayPool<float>.Shared.Rent(capacity);
        _count = 0;
        Resize(count);
    }

    public Aabb Bounds { get; }

    public int Count => _count;

    public int Capacity => Math.Min(Math.Min(_x.Length, _y.Length), Math.Min(_vx.Length, _vy.Length));

    public Span<float> X => _x.AsSpan(0, _count);

    public Span<float> Y => _y.AsSpan(0, _count);

    public Span<float> VX => _vx.AsSpan(0, _count);

    public Span<float> VY => _vy.AsSpan(0, _count);

    /// <summary>Maximum initial speed in world units per second.</summary>
    public float SpawnSpeed { get; set; } = 60f;

    /// <summary>
    /// Changes the agent population. Shrinking simply drops the tail; growing seeds only the new agents, so the
    /// existing swarm keeps moving uninterrupted when the user drags the count slider.
    /// </summary>
    public void Resize(int newCount)
    {
        newCount = Math.Max(0, newCount);
        if (newCount == _count)
        {
            return;
        }

        if (newCount > Capacity)
        {
            GrowTo(newCount);
        }

        if (newCount > _count)
        {
            SeedRange(_count, newCount);
        }

        _count = newCount;
    }

    /// <summary>Re-seeds every agent from scratch with a fresh deterministic stream.</summary>
    public void Reseed(uint seed)
    {
        _seed = seed;
        SeedRange(0, _count);
    }

    private void SeedRange(int start, int end)
    {
        XorShiftRandom rng = new(_seed ^ (uint)(start * 2654435761u));
        float width = Bounds.Width;
        float height = Bounds.Height;

        for (int i = start; i < end; i++)
        {
            _x[i] = Bounds.MinX + (rng.NextFloat() * width);
            _y[i] = Bounds.MinY + (rng.NextFloat() * height);
            _vx[i] = rng.NextSigned() * SpawnSpeed;
            _vy[i] = rng.NextSigned() * SpawnSpeed;
        }
    }

    private void GrowTo(int required)
    {
        Grow(ref _x, required, _count);
        Grow(ref _y, required, _count);
        Grow(ref _vx, required, _count);
        Grow(ref _vy, required, _count);

        static void Grow(ref float[] array, int required, int liveCount)
        {
            if (array.Length >= required)
            {
                return;
            }

            float[] next = ArrayPool<float>.Shared.Rent(Math.Max(required, array.Length * 2));
            array.AsSpan(0, liveCount).CopyTo(next);
            ArrayPool<float>.Shared.Return(array);
            array = next;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ArrayPool<float>.Shared.Return(_x);
        ArrayPool<float>.Shared.Return(_y);
        ArrayPool<float>.Shared.Return(_vx);
        ArrayPool<float>.Shared.Return(_vy);
        _x = [];
        _y = [];
        _vx = [];
        _vy = [];
        _count = 0;
    }
}
