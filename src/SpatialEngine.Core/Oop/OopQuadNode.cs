using SpatialEngine.Core.Abstractions;
using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Core.Oop;

/// <summary>
/// Textbook pointer-based quadtree node: a class with four child references and a <see cref="List{T}"/> of
/// entries. Present purely as the control group for the benchmark study.
/// </summary>
internal sealed class OopQuadNode
{
    public OopQuadNode(Aabb bounds, int depth)
    {
        Bounds = bounds;
        Depth = depth;
        Entries = new List<OopEntry>();
    }

    public Aabb Bounds { get; }

    public int Depth { get; }

    public OopQuadNode? Parent { get; set; }

    public List<OopEntry> Entries { get; }

    public OopQuadNode? NW { get; set; }

    public OopQuadNode? NE { get; set; }

    public OopQuadNode? SW { get; set; }

    public OopQuadNode? SE { get; set; }

    public bool IsLeaf => NW is null;

    public OopQuadNode ChildFor(float x, float y) => Bounds.QuadrantIndex(x, y) switch
    {
        0 => NW!,
        1 => NE!,
        2 => SW!,
        _ => SE!,
    };
}

/// <summary>Boxed-by-design entry record. Each insert of a new agent allocates one of these.</summary>
internal sealed class OopEntry
{
    public OopEntry(int id, float x, float y)
    {
        Id = id;
        X = x;
        Y = y;
    }

    public int Id { get; }

    public float X { get; set; }

    public float Y { get; set; }
}
