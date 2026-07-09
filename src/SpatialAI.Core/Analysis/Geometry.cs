using SpatialAI.Core.Model;

namespace SpatialAI.Core.Analysis;

/// <summary>XZ-plane geometry helpers. Positions are center-based; sizes are [X, Y, Z] meters.</summary>
public static class Geometry
{
    public readonly record struct Footprint(float MinX, float MinZ, float MaxX, float MaxZ)
    {
        public float Width => MaxX - MinX;
        public float Depth => MaxZ - MinZ;
        public float Area => Width * Depth;
    }

    public static Footprint FootprintOf(Vec3 center, Vec3 size)
    {
        var hw = size.X / 2f;
        var hd = size.Z / 2f;
        return new Footprint(center.X - hw, center.Z - hd, center.X + hw, center.Z + hd);
    }

    /// <summary>
    /// Axis-aligned footprint of a box rotated about Y. For a near-quarter-turn (within ±45° of 90°/270°)
    /// the X/Z extents swap; otherwise it's treated axis-aligned. Good enough for right-angle furniture.
    /// </summary>
    public static Footprint FootprintOf(Vec3 center, Vec3 size, float rotationY)
    {
        var a = ((rotationY % 180f) + 180f) % 180f; // fold to [0, 180)
        var swap = a > 45f && a < 135f;
        var hw = (swap ? size.Z : size.X) / 2f;
        var hd = (swap ? size.X : size.Z) / 2f;
        return new Footprint(center.X - hw, center.Z - hd, center.X + hw, center.Z + hd);
    }

    public static Footprint FootprintOf(Item item) => FootprintOf(item.Position, item.Size, item.RotationY);

    /// <summary>Shrinks a footprint inward by <paramref name="margin"/> on every side.</summary>
    public static Footprint Inset(Footprint f, float margin) =>
        new(f.MinX + margin, f.MinZ + margin, f.MaxX - margin, f.MaxZ - margin);

    public static Footprint FootprintOf(Room room) =>
        FootprintOf(room.Center, new Vec3(room.Width, 0, room.Depth));

    /// <summary>Horizontal (XZ) distance between two points, ignoring height.</summary>
    public static float HorizontalDistance(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Shortest gap between two footprints; 0 if they overlap.</summary>
    public static float Gap(Footprint a, Footprint b)
    {
        var dx = MathF.Max(0f, MathF.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
        var dz = MathF.Max(0f, MathF.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
