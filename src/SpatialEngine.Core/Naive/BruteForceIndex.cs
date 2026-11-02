using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Internal;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Core.Naive;

/// <summary>
/// O(n) baseline. There is no index at all: positions are held in two flat float arrays and every query is a
/// full SIMD-vectorised sweep.
/// <para>
/// This is deliberately a *strong* baseline rather than a straw man. A vectorised linear scan has perfect
/// memory locality and no pointer chasing, so it beats a tree for small <c>n</c> or very large query radii.
/// The crossover point it establishes is one of the headline results of the study.
/// </para>
/// <para><c>UpdateAll</c> here is a pure memory copy, which is the theoretical floor for update cost.</para>
/// </summary>
public sealed class BruteForceIndex : ISpatialIndex
{
    private readonly Aabb _world;
    private float[] _x;
    private float[] _y;
    private bool[] _alive;
    private int _capacity;
    private int _count;
    private int _highWater;
    private QueryStats _lastStats;
    private bool _disposed;

    public BruteForceIndex(Aabb world, int initialCapacity = 1024)
    {
        _world = world;
        int cap = Math.Max(16, initialCapacity);
        _x = PooledArray.Rent<float>(cap);
        _y = PooledArray.Rent<float>(cap);
        _alive = PooledArray.Rent<bool>(cap);
        _capacity = Math.Min(_x.Length, Math.Min(_y.Length, _alive.Length));
        _alive.AsSpan().Clear();
    }

    public string Name => "BruteForce";

    public Aabb WorldBounds => _world;

    public int Count => _count;

    public QueryStats LastQueryStats => _lastStats;

    public void EnsureCapacity(int agentCount)
    {
        if (agentCount <= _capacity)
        {
            return;
        }

        int before = _capacity;
        PooledArray.Grow(ref _x, agentCount);
        PooledArray.Grow(ref _y, agentCount);
        PooledArray.Grow(ref _alive, agentCount);
        _capacity = Math.Min(_x.Length, Math.Min(_y.Length, _alive.Length));
        _alive.AsSpan(before).Clear();
    }

    public void Clear()
    {
        _alive.AsSpan(0, _capacity).Clear();
        _count = 0;
        _highWater = 0;
    }

    public void Insert(int id, float x, float y)
    {
        EnsureCapacity(id + 1);
        _world.ClampPoint(ref x, ref y);
        if (!_alive[id])
        {
            _alive[id] = true;
            _count++;
        }

        _x[id] = x;
        _y[id] = y;
        if (id >= _highWater)
        {
            _highWater = id + 1;
        }
    }

    public void Remove(int id)
    {
        if ((uint)id >= (uint)_capacity || !_alive[id])
        {
            return;
        }

        _alive[id] = false;
        _count--;

        // NaN makes every SIMD comparison in the batch filter evaluate false, so removed agents drop out of
        // the vectorised sweep without needing a separate liveness mask on the hot path.
        _x[id] = float.NaN;
        _y[id] = float.NaN;
    }

    public void Update(int id, float x, float y) => Insert(id, x, y);

    public void UpdateAll(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys)
    {
        int n = Math.Min(xs.Length, ys.Length);
        EnsureCapacity(n);
        xs[..n].CopyTo(_x);
        ys[..n].CopyTo(_y);
        if (n > _highWater)
        {
            _alive.AsSpan(_highWater, n - _highWater).Fill(true);
            _count += n - _highWater;
            _highWater = n;
        }
    }

    public void Build(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys)
    {
        int n = Math.Min(xs.Length, ys.Length);
        EnsureCapacity(n);
        Clear();
        xs[..n].CopyTo(_x);
        ys[..n].CopyTo(_y);
        _alive.AsSpan(0, n).Fill(true);
        _count = n;
        _highWater = n;
    }

    public int QueryRange(in Aabb box, Span<int> results)
    {
        _lastStats.Reset();
        _lastStats.NodesVisited = 1;
        _lastStats.CandidatesTested = _highWater;
        int hits = SimdAabb.FilterPoints(_x.AsSpan(0, _highWater), _y.AsSpan(0, _highWater), box, results);
        _lastStats.Hits = hits;
        _lastStats.Truncated = hits > results.Length;
        return hits;
    }

    public int QueryRange(in Circle circle, Span<int> results)
    {
        _lastStats.Reset();
        _lastStats.NodesVisited = 1;
        _lastStats.CandidatesTested = _highWater;
        int hits = SimdAabb.FilterPoints(_x.AsSpan(0, _highWater), _y.AsSpan(0, _highWater), circle, results);
        _lastStats.Hits = hits;
        _lastStats.Truncated = hits > results.Length;
        return hits;
    }

    public void QueryRange<TVisitor>(in Aabb box, ref TVisitor visitor)
        where TVisitor : struct, IQueryVisitor, allows ref struct
    {
        _lastStats.Reset();
        _lastStats.NodesVisited = 1;
        for (int i = 0; i < _highWater; i++)
        {
            _lastStats.CandidatesTested++;
            if (_alive[i] && box.Contains(_x[i], _y[i]))
            {
                _lastStats.Hits++;
                if (!visitor.Visit(i, _x[i], _y[i]))
                {
                    return;
                }
            }
        }
    }

    public void QueryRange<TVisitor>(in Circle circle, ref TVisitor visitor)
        where TVisitor : struct, IQueryVisitor, allows ref struct
    {
        _lastStats.Reset();
        _lastStats.NodesVisited = 1;
        for (int i = 0; i < _highWater; i++)
        {
            _lastStats.CandidatesTested++;
            if (_alive[i] && circle.Contains(_x[i], _y[i]))
            {
                _lastStats.Hits++;
                if (!visitor.Visit(i, _x[i], _y[i]))
                {
                    return;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        float[]? x = _x;
        float[]? y = _y;
        bool[]? alive = _alive;
        PooledArray.Return(ref x);
        PooledArray.Return(ref y);
        PooledArray.Return(ref alive);
        _x = [];
        _y = [];
        _alive = [];
        _capacity = 0;
    }
}
