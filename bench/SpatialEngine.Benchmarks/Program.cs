using System.Diagnostics;
using BenchmarkDotNet.Running;
using SpatialEngine.Benchmarks;
using SpatialEngine.Benchmarks.Configs;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Primitives;
using SpatialEngine.Simulation;

if (args.Length > 0 && args[0].Equals("selftest", StringComparison.OrdinalIgnoreCase))
{
    int agents = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 100_000;
    return SelfTest.Run(agents);
}

BenchmarkSwitcher.FromAssembly(typeof(SelfTest).Assembly).Run(args, new ZeroQuadConfig());
return 0;

/// <summary>
/// Quick end-to-end sanity run outside BenchmarkDotNet: drives a full simulation at the target agent count
/// and reports per-tick latency plus managed bytes allocated.
/// </summary>
/// <remarks>
/// This exists because the headline claim is easy to get wrong in a way a micro-benchmark hides. Running the
/// real host for a few seconds and reading <see cref="GC.GetAllocatedBytesForCurrentThread"/> proves the
/// whole pipeline is clean, not just the method under test.
/// </remarks>
internal static class SelfTest
{
    public static int Run(int agentCount)
    {
        Aabb world = Scenario.World;
        QuadtreeOptions options = QuadtreeOptions.Default(world) with { InitialCapacity = agentCount };

        using SimulationHost host = new(world, agentCount, options);
        host.TriggerHotspot(world.CenterX, world.CenterY, strength: 2200f, timeToLive: float.PositiveInfinity);

        int[] results = new int[agentCount];
        Circle probe = new(world.CenterX, world.CenterY, world.Width * 0.05f);
        const float Dt = 1f / 60f;

        Console.WriteLine($"ZeroQuad self test: {agentCount:N0} agents, world {world.Width:N0} x {world.Height:N0}");

        // Warm up: JIT, pooled buffers and the initial split cascade all allocate exactly once.
        for (int i = 0; i < 120; i++)
        {
            host.Tick(Dt);
            host.QueryCircle(probe, results);
        }

        const int MeasuredTicks = 600;
        double worstTick = 0.0;
        double totalTick = 0.0;
        double totalQuery = 0.0;
        double totalHitRate = 0.0;

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int gen0Before = GC.CollectionCount(0);
        long start = Stopwatch.GetTimestamp();

        for (int i = 0; i < MeasuredTicks; i++)
        {
            host.Tick(Dt);
            host.QueryCircle(probe, results);

            totalTick += host.Telemetry.TickMicroseconds;
            totalQuery += host.Telemetry.QueryMicroseconds;
            totalHitRate += host.Telemetry.DirtyCheckHitRate;
            worstTick = Math.Max(worstTick, host.Telemetry.TickMicroseconds);
        }

        double wallMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        int gen0 = GC.CollectionCount(0) - gen0Before;

        FlatQuadtree tree = (FlatQuadtree)host.Index;
        Console.WriteLine($"  mean tick     : {totalTick / MeasuredTicks,8:F1} us   (budget at 60 Hz: 16666 us)");
        Console.WriteLine($"  worst tick    : {worstTick,8:F1} us");
        Console.WriteLine($"  mean query    : {totalQuery / MeasuredTicks,8:F1} us   ({host.Telemetry.QueryHits:N0} hits)");
        Console.WriteLine($"  dirty-check   : {totalHitRate / MeasuredTicks * 100,8:F2} % of agents stayed in their leaf");
        Console.WriteLine($"  nodes / depth : {tree.NodeCount:N0} / {tree.MeasureDepth()}");
        Console.WriteLine($"  array growths : {tree.GrowthCount}");
        Console.WriteLine($"  wall clock    : {wallMs:F1} ms for {MeasuredTicks} ticks " +
                          $"({MeasuredTicks / (wallMs / 1000.0):F0} ticks/s)");
        Console.WriteLine($"  allocated     : {allocated:N0} bytes over {MeasuredTicks} ticks, {gen0} gen0 collections");

        bool clean = allocated == 0 && tree.GrowthCount == 0;
        double meanTick = totalTick / MeasuredTicks;
        bool fastEnough = meanTick < 16_666.0;

        Console.WriteLine();
        Console.WriteLine(clean
            ? "  PASS: zero managed allocation in the hot path."
            : $"  FAIL: {allocated:N0} bytes allocated, {tree.GrowthCount} array growths.");
        Console.WriteLine(fastEnough
            ? "  PASS: tick fits inside the 60 Hz budget."
            : $"  FAIL: mean tick {meanTick:F1} us exceeds the 60 Hz budget.");

        return clean && fastEnough ? 0 : 1;
    }
}
