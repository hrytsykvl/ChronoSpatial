using SpatialEngine.Core.Primitives;

namespace SpatialEngine.Tests;

public class AabbTests
{
    [Fact]
    public void Contains_IsInclusiveOnBothBounds()
    {
        Aabb box = new(0f, 0f, 10f, 10f);

        Assert.True(box.Contains(0f, 0f));
        Assert.True(box.Contains(10f, 10f));
        Assert.True(box.Contains(5f, 5f));
        Assert.False(box.Contains(-0.001f, 5f));
        Assert.False(box.Contains(5f, 10.001f));
    }

    [Fact]
    public void Intersects_IsSymmetricAndInclusiveOnTouchingEdges()
    {
        Aabb a = new(0f, 0f, 10f, 10f);
        Aabb touching = new(10f, 0f, 20f, 10f);
        Aabb disjoint = new(10.001f, 0f, 20f, 10f);

        Assert.True(a.Intersects(touching));
        Assert.True(touching.Intersects(a));
        Assert.False(a.Intersects(disjoint));
        Assert.False(disjoint.Intersects(a));
    }

    [Fact]
    public void ContainsBox_DetectsFullEnclosureOnly()
    {
        Aabb outer = new(0f, 0f, 10f, 10f);

        Assert.True(outer.Contains(new Aabb(1f, 1f, 9f, 9f)));
        Assert.True(outer.Contains(outer));
        Assert.False(outer.Contains(new Aabb(1f, 1f, 11f, 9f)));
    }

    [Fact]
    public void Children_TileTheParentExactlyAndShareEdgeValues()
    {
        Aabb parent = new(-3.5f, 7.25f, 11.75f, 40.5f);
        Aabb nw = parent.Child(0);
        Aabb ne = parent.Child(1);
        Aabb sw = parent.Child(2);
        Aabb se = parent.Child(3);

        // Shared split lines must be the identical float in both siblings: no gaps, no overlap, no drift.
        Assert.Equal(nw.MaxX, ne.MinX);
        Assert.Equal(nw.MaxY, sw.MinY);
        Assert.Equal(se.MinX, ne.MinX);
        Assert.Equal(se.MinY, sw.MinY);

        Assert.True(parent.Contains(nw));
        Assert.True(parent.Contains(ne));
        Assert.True(parent.Contains(sw));
        Assert.True(parent.Contains(se));

        float childArea = (nw.Width * nw.Height) + (ne.Width * ne.Height)
            + (sw.Width * sw.Height) + (se.Width * se.Height);
        Assert.Equal(parent.Width * parent.Height, childArea, 2);
    }

    [Theory]
    [InlineData(2.5f, 2.5f, 0)]
    [InlineData(7.5f, 2.5f, 1)]
    [InlineData(2.5f, 7.5f, 2)]
    [InlineData(7.5f, 7.5f, 3)]
    public void QuadrantIndex_UsesCanvasOrientation(float x, float y, int expected)
    {
        Aabb box = new(0f, 0f, 10f, 10f);
        Assert.Equal(expected, box.QuadrantIndex(x, y));
    }

    [Fact]
    public void QuadrantIndex_IsHalfOpenSoSplitLinesHaveExactlyOneOwner()
    {
        Aabb box = new(0f, 0f, 10f, 10f);

        // Exactly on the centre lines: must resolve to the high side, giving a single owning child.
        Assert.Equal(3, box.QuadrantIndex(5f, 5f));
        Assert.Equal(1, box.QuadrantIndex(5f, 4.999f));
        Assert.Equal(2, box.QuadrantIndex(4.999f, 5f));
    }

    [Fact]
    public void CircleIntersects_UsesClosestPointNotCornerDistance()
    {
        Aabb box = new(0f, 0f, 10f, 10f);

        Assert.True(new Circle(-1f, 5f, 1.5f).Intersects(box));
        Assert.False(new Circle(-2f, 5f, 1.5f).Intersects(box));

        // Diagonally outside the corner: inside the corner's bounding box but outside the radius.
        Assert.False(new Circle(12f, 12f, 2f).Intersects(box));
        Assert.True(new Circle(11f, 11f, 2f).Intersects(box));
    }

    [Fact]
    public void SimdFilter_MatchesScalarReference()
    {
        const int N = 1000;
        float[] xs = new float[N];
        float[] ys = new float[N];
        Random rng = new(1234);
        for (int i = 0; i < N; i++)
        {
            xs[i] = (float)(rng.NextDouble() * 100.0);
            ys[i] = (float)(rng.NextDouble() * 100.0);
        }

        Aabb box = new(17.5f, 22.25f, 63.75f, 81f);
        Circle circle = new(50f, 50f, 20f);

        int[] boxHits = new int[N];
        int[] circleHits = new int[N];
        int boxCount = SimdAabb.FilterPoints(xs, ys, box, boxHits);
        int circleCount = SimdAabb.FilterPoints(xs, ys, circle, circleHits);

        List<int> expectedBox = [];
        List<int> expectedCircle = [];
        for (int i = 0; i < N; i++)
        {
            if (box.Contains(xs[i], ys[i]))
            {
                expectedBox.Add(i);
            }

            if (circle.Contains(xs[i], ys[i]))
            {
                expectedCircle.Add(i);
            }
        }

        Assert.Equal(expectedBox.Count, boxCount);
        Assert.Equal(expectedCircle.Count, circleCount);
        Assert.Equal(expectedBox, boxHits.Take(boxCount).ToList());
        Assert.Equal(expectedCircle, circleHits.Take(circleCount).ToList());
    }
}
