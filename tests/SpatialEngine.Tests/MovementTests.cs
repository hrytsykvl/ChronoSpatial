using SpatialEngine.Core.Primitives;
using SpatialEngine.Simulation;
using SpatialEngine.Simulation.Movement;

namespace SpatialEngine.Tests;

public sealed class MovementTests
{
    private static readonly Aabb World = new(0f, 0f, 1000f, 1000f);

    [Fact]
    public void BrownianWalk_KeepsEveryAgentInsideTheWorld()
    {
        using AgentWorld world = new(World, 5000, seed: 7u);
        BrownianWalk walk = new(seed: 99u) { Jitter = 4000f, MaxSpeed = 5000f };

        for (int tick = 0; tick < 300; tick++)
        {
            walk.Step(world, 1f / 60f);
        }

        Span<float> xs = world.X;
        Span<float> ys = world.Y;
        for (int i = 0; i < xs.Length; i++)
        {
            Assert.True(World.Contains(xs[i], ys[i]), $"agent {i} escaped to ({xs[i]}, {ys[i]})");
        }
    }

    [Fact]
    public void BrownianWalk_IsDeterministicForAFixedSeed()
    {
        float[] first = RunWalk();
        float[] second = RunWalk();

        Assert.Equal(first, second);

        static float[] RunWalk()
        {
            using AgentWorld world = new(World, 512, seed: 1234u);
            BrownianWalk walk = new(seed: 4321u);
            for (int tick = 0; tick < 50; tick++)
            {
                walk.Step(world, 1f / 60f);
            }

            return world.X.ToArray();
        }
    }

    [Fact]
    public void BrownianWalk_PartitionedStepMatchesSerialStep()
    {
        using AgentWorld serial = new(World, 1024, seed: 55u);
        using AgentWorld partitioned = new(World, 1024, seed: 55u);

        BrownianWalk a = new(seed: 777u);
        BrownianWalk b = new(seed: 777u);
        const float Dt = 1f / 60f;

        for (int tick = 0; tick < 20; tick++)
        {
            a.Step(serial, Dt);

            // Splitting on a vector-width boundary must not change a single coordinate.
            b.AdvanceState(Dt);
            b.StepRange(partitioned, Dt, 0, 512);
            b.StepRange(partitioned, Dt, 512, 1024);
        }

        Assert.Equal(serial.X.ToArray(), partitioned.X.ToArray());
        Assert.Equal(serial.Y.ToArray(), partitioned.Y.ToArray());
    }

    [Fact]
    public void GravityWell_PullsAgentsTowardItsCentre()
    {
        using AgentWorld world = new(World, 2000, seed: 11u);
        GravityWellField field = new();
        BrownianWalk walk = new(seed: 3u) { Jitter = 0f, MaxSpeed = 4000f };

        Vec2 centre = new(500f, 500f);
        double before = MeanDistance(world, centre);

        field.Add(centre.X, centre.Y, strength: 6000f, radius: 2000f);
        for (int tick = 0; tick < 120; tick++)
        {
            field.Step(world, 1f / 60f);
            walk.Step(world, 1f / 60f);
        }

        double after = MeanDistance(world, centre);
        Assert.True(after < before * 0.75, $"expected convergence, mean distance went {before:F1} -> {after:F1}");
    }

    [Fact]
    public void GravityWell_ExpiresAfterItsTimeToLive()
    {
        GravityWellField field = new();
        field.Add(1f, 1f, strength: 10f, radius: 10f, timeToLive: 0.5f);
        Assert.Equal(1, field.ActiveCount);

        for (int tick = 0; tick < 60; tick++)
        {
            field.AdvanceState(1f / 60f);
        }

        Assert.Equal(0, field.ActiveCount);
    }

    [Fact]
    public void GravityWell_LeavesAgentsOutsideItsRadiusUntouched()
    {
        using AgentWorld world = new(World, 64, seed: 2u);
        world.X[0] = 10f;
        world.Y[0] = 10f;
        world.VX[0] = 0f;
        world.VY[0] = 0f;

        GravityWellField field = new();
        field.Add(990f, 990f, strength: 50_000f, radius: 50f);
        field.Step(world, 1f / 60f);

        Assert.Equal(0f, world.VX[0]);
        Assert.Equal(0f, world.VY[0]);
    }

    private static double MeanDistance(AgentWorld world, Vec2 centre)
    {
        double total = 0.0;
        Span<float> xs = world.X;
        Span<float> ys = world.Y;
        for (int i = 0; i < xs.Length; i++)
        {
            double dx = xs[i] - centre.X;
            double dy = ys[i] - centre.Y;
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total / xs.Length;
    }
}
