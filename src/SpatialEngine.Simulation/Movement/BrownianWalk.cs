using System.Numerics;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Simulation.Movement;

/// <summary>
/// Random-walk integrator with reflecting world boundaries. This model owns integration for the whole
/// pipeline: force-style models such as <see cref="GravityWellField"/> only modify velocity, and this step
/// turns velocity into position.
/// </summary>
/// <remarks>
/// The whole step is vectorised over <see cref="Vector{T}.Count"/> agents. Jitter comes from
/// <see cref="CounterRandom"/>, a stateless hash of (seed, tick, agent index), which makes the walk
/// deterministic and bit-identical whether the swarm is stepped serially or split across threads. Boundary
/// reflection is expressed with <see cref="Vector.ConditionalSelect(Vector{int}, Vector{float}, Vector{float})"/>
/// rather than branches, so the world edges cost no mispredictions.
/// </remarks>
public sealed class BrownianWalk(uint seed = 0xC0FFEEu) : IMovementModel
{
    private readonly uint _seed = seed;
    private uint _sequence;

    public string Name => "BrownianWalk";

    /// <summary>Random acceleration magnitude in world units per second squared.</summary>
    public float Jitter { get; set; } = 240f;

    /// <summary>Fraction of velocity shed per second; keeps the walk from diverging.</summary>
    public float Damping { get; set; } = 0.9f;

    /// <summary>Hard speed ceiling in world units per second.</summary>
    public float MaxSpeed { get; set; } = 220f;

    public void AdvanceState(float dt) => _sequence++;

    public void Step(AgentWorld world, float dt)
    {
        AdvanceState(dt);
        StepRange(world, dt, 0, world.Count);
    }

    public void StepRange(AgentWorld world, float dt, int start, int end)
    {
        int n = Math.Min(end, world.Count);
        if (start >= n)
        {
            return;
        }

        Span<float> xs = world.X;
        Span<float> ys = world.Y;
        Span<float> vxs = world.VX;
        Span<float> vys = world.VY;

        Aabb b = world.Bounds;
        float jitter = Jitter * dt;
        float damping = MathF.Max(0f, 1f - (Damping * dt));
        float maxSpeed = MaxSpeed;
        float maxSpeedSq = maxSpeed * maxSpeed;

        // Jitter is a pure hash of (seed, tick, agent index): identical whether the swarm is stepped
        // serially or split across threads at any vector-aligned boundary.
        uint seed = _seed;
        uint sequence = _sequence;

        int i = start;
        int width = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated && n - start >= width)
        {
            Vector<float> vDt = new(dt);
            Vector<float> vJitter = new(jitter);
            Vector<float> vDamping = new(damping);
            Vector<float> vMinX = new(b.MinX);
            Vector<float> vMinY = new(b.MinY);
            Vector<float> vMaxX = new(b.MaxX);
            Vector<float> vMaxY = new(b.MaxY);
            Vector<float> vMaxSpeed = new(maxSpeed);
            Vector<float> vMaxSpeedSq = new(maxSpeedSq);
            Vector<float> epsilon = new(1e-12f);

            for (; i <= n - width; i += width)
            {
                Vector<float> vx = (new Vector<float>(vxs.Slice(i, width)) * vDamping)
                                 + (CounterRandom.Signed(seed, sequence, 0u, i) * vJitter);
                Vector<float> vy = (new Vector<float>(vys.Slice(i, width)) * vDamping)
                                 + (CounterRandom.Signed(seed, sequence, 1u, i) * vJitter);

                // Speed clamp, branch free.
                Vector<float> speedSq = (vx * vx) + (vy * vy);
                Vector<float> scale = vMaxSpeed / Vector.SquareRoot(Vector.Max(speedSq, epsilon));
                Vector<int> tooFast = Vector.GreaterThan(speedSq, vMaxSpeedSq);
                vx = Vector.ConditionalSelect(tooFast, vx * scale, vx);
                vy = Vector.ConditionalSelect(tooFast, vy * scale, vy);

                Vector<float> x = new Vector<float>(xs.Slice(i, width)) + (vx * vDt);
                Vector<float> y = new Vector<float>(ys.Slice(i, width)) + (vy * vDt);

                // Mirror about the violated edge and flip the corresponding velocity component.
                Vector<int> belowX = Vector.LessThan(x, vMinX);
                Vector<int> aboveX = Vector.GreaterThan(x, vMaxX);
                x = Vector.ConditionalSelect(belowX, (vMinX + vMinX) - x, x);
                x = Vector.ConditionalSelect(aboveX, (vMaxX + vMaxX) - x, x);
                vx = Vector.ConditionalSelect(belowX | aboveX, -vx, vx);

                Vector<int> belowY = Vector.LessThan(y, vMinY);
                Vector<int> aboveY = Vector.GreaterThan(y, vMaxY);
                y = Vector.ConditionalSelect(belowY, (vMinY + vMinY) - y, y);
                y = Vector.ConditionalSelect(aboveY, (vMaxY + vMaxY) - y, y);
                vy = Vector.ConditionalSelect(belowY | aboveY, -vy, vy);

                // One reflection can still land outside if a step exceeds the world extent; the clamp
                // guarantees the index never sees an out-of-world coordinate.
                x = Vector.Min(Vector.Max(x, vMinX), vMaxX);
                y = Vector.Min(Vector.Max(y, vMinY), vMaxY);

                x.CopyTo(xs.Slice(i, width));
                y.CopyTo(ys.Slice(i, width));
                vx.CopyTo(vxs.Slice(i, width));
                vy.CopyTo(vys.Slice(i, width));
            }
        }

        for (; i < n; i++)
        {
            float vx = (vxs[i] * damping) + (CounterRandom.Signed(CounterRandom.Key(seed, sequence, 0u, i)) * jitter);
            float vy = (vys[i] * damping) + (CounterRandom.Signed(CounterRandom.Key(seed, sequence, 1u, i)) * jitter);

            float speedSq = (vx * vx) + (vy * vy);
            if (speedSq > maxSpeedSq)
            {
                float scale = maxSpeed / MathF.Sqrt(speedSq);
                vx *= scale;
                vy *= scale;
            }

            float x = xs[i] + (vx * dt);
            float y = ys[i] + (vy * dt);

            if (x < b.MinX)
            {
                x = (b.MinX * 2f) - x;
                vx = -vx;
            }
            else if (x > b.MaxX)
            {
                x = (b.MaxX * 2f) - x;
                vx = -vx;
            }

            if (y < b.MinY)
            {
                y = (b.MinY * 2f) - y;
                vy = -vy;
            }
            else if (y > b.MaxY)
            {
                y = (b.MaxY * 2f) - y;
                vy = -vy;
            }

            xs[i] = Math.Clamp(x, b.MinX, b.MaxX);
            ys[i] = Math.Clamp(y, b.MinY, b.MaxY);
            vxs[i] = vx;
            vys[i] = vy;
        }
    }
}
