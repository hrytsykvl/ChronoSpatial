namespace SpatialEngine.Simulation;

/// <summary>Selects which spatial index implementation a <see cref="SimulationHost"/> drives.</summary>
public enum SpatialIndexKind
{
    /// <summary>Array-backed, zero-allocation quadtree. The production path.</summary>
    Flat = 0,

    /// <summary>Pointer/class-based quadtree. Kept for the GC-pressure comparison.</summary>
    Oop = 1,

    /// <summary>O(n) SIMD scan. Correctness oracle and latency baseline.</summary>
    BruteForce = 2,
}
