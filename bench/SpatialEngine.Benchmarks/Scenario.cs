using SpatialEngine.Core.Primitives;
using SpatialEngine.Simulation;

namespace SpatialEngine.Benchmarks;

/// <summary>Spatial distribution of a synthetic agent set.</summary>
public enum Distribution
{
    /// <summary>Agents spread evenly over the world. The best case for a quadtree.</summary>
    Uniform,

    /// <summary>Most agents packed into a few tight clusters. Drives leaves past capacity at MaxDepth.</summary>
    Clustered,
}

/// <summary>
/// Deterministic agent-set generator shared by every benchmark, so results are comparable across suites and
/// across runs.
/// </summary>
/// <remarks>
/// Generation happens in <c>[GlobalSetup]</c> only. The benchmarked methods receive already-populated
/// struct-of-arrays buffers, so no benchmark measures its own data generation.
/// </remarks>
public static class Scenario
{
    public static readonly Aabb World = new(0f, 0f, 4096f, 4096f);

    public static void Fill(float[] xs, float[] ys, int count, Distribution distribution, uint seed = 20260101u)
    {
        XorShiftRandom rng = new(seed);

        switch (distribution)
        {
            case Distribution.Uniform:
                for (int i = 0; i < count; i++)
                {
                    xs[i] = rng.NextRange(World.MinX, World.MaxX);
                    ys[i] = rng.NextRange(World.MinY, World.MaxY);
                }

                break;

            case Distribution.Clustered:
            {
                const int Clusters = 12;
                Span<float> cx = stackalloc float[Clusters];
                Span<float> cy = stackalloc float[Clusters];
                for (int c = 0; c < Clusters; c++)
                {
                    cx[c] = rng.NextRange(World.MinX, World.MaxX);
                    cy[c] = rng.NextRange(World.MinY, World.MaxY);
                }

                float spread = World.Width * 0.01f;
                for (int i = 0; i < count; i++)
                {
                    // One in twenty agents stays uniform so the tree still has to cover the whole world.
                    if (rng.NextInt(20) == 0)
                    {
                        xs[i] = rng.NextRange(World.MinX, World.MaxX);
                        ys[i] = rng.NextRange(World.MinY, World.MaxY);
                        continue;
                    }

                    int c = rng.NextInt(Clusters);
                    xs[i] = Math.Clamp(cx[c] + (rng.NextSigned() * spread), World.MinX, World.MaxX);
                    ys[i] = Math.Clamp(cy[c] + (rng.NextSigned() * spread), World.MinY, World.MaxY);
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(distribution), distribution, "Unknown distribution.");
        }
    }

    /// <summary>Query circles covering a given fraction of the world area, spread over the world.</summary>
    public static Circle[] BuildProbes(int count, float areaFraction, uint seed = 7654321u)
    {
        XorShiftRandom rng = new(seed);
        float radius = MathF.Sqrt(World.Width * World.Height * areaFraction / MathF.PI);

        Circle[] probes = new Circle[count];
        for (int i = 0; i < count; i++)
        {
            probes[i] = new Circle(
                rng.NextRange(World.MinX, World.MaxX),
                rng.NextRange(World.MinY, World.MaxY),
                radius);
        }

        return probes;
    }
}
