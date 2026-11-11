using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Primitives;
using SpatialEngine.Simulation;

namespace SpatialEngine.Tests;

public sealed class SimulationHostTests
{
    private static readonly Aabb World = new(0f, 0f, 2000f, 2000f);

    [Fact]
    public void Advance_RunsWholeTicksAndRetainsTheRemainder()
    {
        using SimulationHost host = new(World, 2000) { TickRate = 50.0 };

        Assert.Equal(0, host.Advance(0.01));   // 0.5 of a tick
        Assert.Equal(1, host.Advance(0.012));  // crosses 0.02 s
        Assert.Equal(1, host.Telemetry.TickIndex);
    }

    [Fact]
    public void Advance_CapsCatchUpSoItCannotSpiral()
    {
        using SimulationHost host = new(World, 1000) { TickRate = 60.0, MaxCatchUpTicks = 3 };

        // A backgrounded browser tab can hand us a ten second delta.
        Assert.Equal(3, host.Advance(10.0));

        // The leftover must be discarded, otherwise the next frame inherits the debt.
        Assert.Equal(0, host.Advance(0.0001));
    }

    [Fact]
    public void Tick_KeepsTheIndexConsistentWithTheSwarm()
    {
        using SimulationHost host = new(World, 5000);
        host.TriggerHotspot(1000f, 1000f, strength: 4000f, timeToLive: 1f);

        for (int tick = 0; tick < 200; tick++)
        {
            host.Tick(1f / 60f);
        }

        FlatQuadtree tree = Assert.IsType<FlatQuadtree>(host.Index);
        tree.ValidateInvariants();
        Assert.Equal(host.AgentCount, tree.Count);

        // Every agent must be findable at its current position.
        int[] buffer = new int[host.AgentCount];
        int hits = host.QueryBox(World, buffer);
        Assert.Equal(host.AgentCount, hits);
    }

    [Fact]
    public void Tick_AllocatesNothingOnTheManagedHeap()
    {
        using SimulationHost host = new(World, 20_000);
        host.TriggerHotspot(500f, 500f, strength: 2500f, timeToLive: float.PositiveInfinity);

        // Warm up every path: JIT, pooled buffers and the first split cascade all allocate once.
        for (int tick = 0; tick < 30; tick++)
        {
            host.Tick(1f / 60f);
        }

        int[] results = new int[20_000];
        Circle probe = new(500f, 500f, 150f);
        host.QueryCircle(probe, results);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 60; tick++)
        {
            host.Tick(1f / 60f);
            host.QueryCircle(probe, results);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SetAgentCount_ResizesAndRebuildsWithoutLosingAgents()
    {
        using SimulationHost host = new(World, 1000);

        host.SetAgentCount(7500);
        Assert.Equal(7500, host.AgentCount);
        Assert.Equal(7500, host.Index.Count);

        host.SetAgentCount(300);
        Assert.Equal(300, host.AgentCount);
        Assert.Equal(300, host.Index.Count);

        for (int tick = 0; tick < 20; tick++)
        {
            host.Tick(1f / 60f);
        }

        Assert.IsType<FlatQuadtree>(host.Index).ValidateInvariants();
    }

    [Fact]
    public void SetTreeParams_RebuildsWithTheNewShape()
    {
        using SimulationHost host = new(World, 4000);
        host.SetTreeParams(maxDepth: 4, nodeCapacity: 64);

        Assert.Equal(4, host.Options.MaxDepth);
        Assert.Equal(64, host.Options.NodeCapacity);
        Assert.Equal(4000, host.Index.Count);

        FlatQuadtree tree = Assert.IsType<FlatQuadtree>(host.Index);
        Assert.True(tree.MeasureDepth() <= 4);
    }

    [Theory]
    [InlineData(SpatialIndexKind.Flat)]
    [InlineData(SpatialIndexKind.Oop)]
    [InlineData(SpatialIndexKind.BruteForce)]
    public void EveryIndexKindAgreesOnQueryResults(SpatialIndexKind kind)
    {
        using SimulationHost host = new(World, 3000, indexKind: kind);
        for (int tick = 0; tick < 25; tick++)
        {
            host.Tick(1f / 60f);
        }

        Circle probe = new(900f, 1100f, 260f);
        int[] buffer = new int[3000];
        int hits = host.QueryCircle(probe, buffer);

        int expected = 0;
        Span<float> xs = host.Agents.X;
        Span<float> ys = host.Agents.Y;
        for (int i = 0; i < xs.Length; i++)
        {
            if (probe.Contains(xs[i], ys[i]))
            {
                expected++;
            }
        }

        Assert.Equal(expected, hits);
        Assert.True(host.Telemetry.QueryMicroseconds >= 0.0);
    }

    [Fact]
    public void Paused_FreezesTheSwarm()
    {
        using SimulationHost host = new(World, 500) { Paused = true };
        float[] before = host.Agents.X.ToArray();

        for (int tick = 0; tick < 10; tick++)
        {
            host.Tick(1f / 60f);
        }

        Assert.Equal(before, host.Agents.X.ToArray());
    }
}
