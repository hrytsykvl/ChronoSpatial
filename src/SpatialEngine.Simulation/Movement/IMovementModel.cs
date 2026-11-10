namespace SpatialEngine.Simulation.Movement;

/// <summary>
/// A per-tick behaviour applied to the whole swarm. Implementations must be allocation free: they are invoked
/// 20-60 times per second with 100k+ agents.
/// </summary>
/// <remarks>
/// The API is split so the swarm can optionally be partitioned across threads:
/// <see cref="AdvanceState"/> mutates model-level state (well lifetimes, random sequence counters) exactly once
/// per tick, while <see cref="StepRange"/> touches only the agents in its own half-open range and never model
/// state, and is therefore safe to run concurrently on disjoint partitions.
/// </remarks>
public interface IMovementModel
{
    string Name { get; }

    /// <summary>Advances model-level state by <paramref name="dt"/> seconds. Called exactly once per tick.</summary>
    void AdvanceState(float dt);

    /// <summary>
    /// Applies the behaviour to agents in <c>[start, end)</c>. Must not mutate model-level state, so disjoint
    /// ranges can be processed in parallel.
    /// </summary>
    void StepRange(AgentWorld world, float dt, int start, int end);

    /// <summary>Serial whole-swarm step.</summary>
    void Step(AgentWorld world, float dt)
    {
        AdvanceState(dt);
        StepRange(world, dt, 0, world.Count);
    }
}
