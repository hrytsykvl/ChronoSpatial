using System.Numerics;

namespace SpatialEngine.Simulation.Movement;

/// <summary>
/// A single attractor: a traffic hotspot, an emergency scene, or the target a drone swarm converges on.
/// </summary>
public struct GravityWell
{
    public float X;
    public float Y;

    /// <summary>Peak acceleration in world units per second squared, reached at the well centre. Negative repels.</summary>
    public float Strength;

    /// <summary>Influence radius; agents beyond it are untouched, which keeps the cost bounded.</summary>
    public float Radius;

    /// <summary>Remaining lifetime in seconds. <see cref="float.PositiveInfinity"/> means permanent.</summary>
    public float TimeToLive;

    public readonly bool IsAlive => TimeToLive > 0f && Radius > 0f;
}

/// <summary>
/// Velocity-only force field: it accelerates agents toward active wells but never integrates position, which
/// is left to <see cref="BrownianWalk"/>.
/// </summary>
/// <remarks>
/// This is what makes the index work hard. A hotspot drags tens of thousands of agents into a handful of
/// quadrants, which drives the tree to <c>MaxDepth</c> in that region and leaves leaves well over capacity.
/// It is the degenerate-clustering case the structure is explicitly designed to survive.
/// <para>
/// Wells live in a fixed-size array allocated once, so adding and expiring hotspots never allocates.
/// The inner loop is vectorised across agents for each active well.
/// </para>
/// <para>
/// The falloff is a <b>linear ramp</b>, full strength at the centre and exactly zero at the influence
/// radius, rather than true inverse-square gravity. Inverse-square is unusable here: it concentrates almost
/// all of its effect in a tiny core and leaves agents at demo-scale distances essentially unmoved, so the
/// strength value would have no intuitive meaning for a UI slider. A ramp makes <c>Strength</c> literally
/// "peak acceleration" and reaches zero continuously at the edge, avoiding a force discontinuity as agents
/// drift in and out of range.
/// </para>
/// </remarks>
public sealed class GravityWellField : IMovementModel
{
    public const int MaxWells = 16;

    private readonly GravityWell[] _wells = new GravityWell[MaxWells];
    private int _activeCount;

    public string Name => "GravityWells";

    /// <summary>Distance softening; keeps the direction vector finite at the exact centre of a well.</summary>
    public float Softening { get; set; } = 4f;

    public int ActiveCount => _activeCount;

    public ReadOnlySpan<GravityWell> Wells => _wells.AsSpan(0, _activeCount);

    /// <summary>Adds a well, recycling the oldest slot when the field is full. Returns its index.</summary>
    public int Add(float x, float y, float strength, float radius, float timeToLive = float.PositiveInfinity)
    {
        int slot;
        if (_activeCount < MaxWells)
        {
            slot = _activeCount++;
        }
        else
        {
            slot = 0;
            for (int i = 1; i < MaxWells; i++)
            {
                if (_wells[i].TimeToLive < _wells[slot].TimeToLive)
                {
                    slot = i;
                }
            }
        }

        _wells[slot] = new GravityWell
        {
            X = x,
            Y = y,
            Strength = strength,
            Radius = radius,
            TimeToLive = timeToLive,
        };

        return slot;
    }

    public void Clear() => _activeCount = 0;

    /// <summary>Ages every well and retires the expired ones. Model-level state, so called once per tick.</summary>
    public void AdvanceState(float dt)
    {
        for (int w = 0; w < _activeCount; w++)
        {
            _wells[w].TimeToLive -= dt;
        }

        CompactExpired();
    }

    public void Step(AgentWorld world, float dt)
    {
        AdvanceState(dt);
        StepRange(world, dt, 0, world.Count);
    }

    public void StepRange(AgentWorld world, float dt, int start, int end)
    {
        if (_activeCount == 0)
        {
            return;
        }

        int n = Math.Min(end, world.Count);
        if (start >= n)
        {
            return;
        }

        Span<float> xs = world.X;
        Span<float> ys = world.Y;
        Span<float> vxs = world.VX;
        Span<float> vys = world.VY;

        float softeningSq = Softening * Softening;

        for (int w = 0; w < _activeCount; w++)
        {
            GravityWell well = _wells[w];
            if (!well.IsAlive)
            {
                continue;
            }

            float radiusSq = well.Radius * well.Radius;
            float invRadius = 1f / well.Radius;
            float accel = well.Strength * dt;
            int i = start;
            int width = Vector<float>.Count;

            if (Vector.IsHardwareAccelerated && n - start >= width)
            {
                Vector<float> wx = new(well.X);
                Vector<float> wy = new(well.Y);
                Vector<float> vRadiusSq = new(radiusSq);
                Vector<float> vInvRadius = new(invRadius);
                Vector<float> vSofteningSq = new(softeningSq);
                Vector<float> vAccel = new(accel);
                Vector<float> one = Vector<float>.One;
                Vector<float> zero = Vector<float>.Zero;

                for (; i <= n - width; i += width)
                {
                    Vector<float> dx = wx - new Vector<float>(xs.Slice(i, width));
                    Vector<float> dy = wy - new Vector<float>(ys.Slice(i, width));
                    Vector<float> distSq = (dx * dx) + (dy * dy);

                    // Softening only guards the direction vector; the ramp itself is well behaved at r = 0.
                    Vector<float> invDist = one / Vector.SquareRoot(distSq + vSofteningSq);
                    Vector<float> dist = distSq * invDist;
                    Vector<float> ramp = Vector.Max(zero, one - (dist * vInvRadius));
                    Vector<float> magnitude = vAccel * ramp * invDist;

                    Vector<int> inRange = Vector.LessThanOrEqual(distSq, vRadiusSq);
                    Vector<float> ax = Vector.ConditionalSelect(inRange, dx * magnitude, zero);
                    Vector<float> ay = Vector.ConditionalSelect(inRange, dy * magnitude, zero);

                    (new Vector<float>(vxs.Slice(i, width)) + ax).CopyTo(vxs.Slice(i, width));
                    (new Vector<float>(vys.Slice(i, width)) + ay).CopyTo(vys.Slice(i, width));
                }
            }

            for (; i < n; i++)
            {
                float dx = well.X - xs[i];
                float dy = well.Y - ys[i];
                float distSq = (dx * dx) + (dy * dy);
                if (distSq > radiusSq)
                {
                    continue;
                }

                float invDist = 1f / MathF.Sqrt(distSq + softeningSq);
                float dist = distSq * invDist;
                float ramp = MathF.Max(0f, 1f - (dist * invRadius));
                float magnitude = accel * ramp * invDist;
                vxs[i] += dx * magnitude;
                vys[i] += dy * magnitude;
            }
        }
    }

    private void CompactExpired()
    {
        int write = 0;
        for (int read = 0; read < _activeCount; read++)
        {
            if (_wells[read].IsAlive)
            {
                if (write != read)
                {
                    _wells[write] = _wells[read];
                }

                write++;
            }
        }

        _activeCount = write;
    }
}
