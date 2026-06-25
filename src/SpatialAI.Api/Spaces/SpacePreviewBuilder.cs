using SpatialAI.Core.Model;

namespace SpatialAI.Api.Spaces;

/// <summary>
/// Builds a compact top-down <see cref="SpacePreview"/> (room rectangles + colored item dots + bounds) for a
/// scene, so the viewer can draw a recognizable thumbnail without loading the full scene. Items are capped so
/// large scenes stay light over the wire.
/// </summary>
public static class SpacePreviewBuilder
{
    private const int MaxItems = 500;

    public static SpacePreview Build(Scene scene)
    {
        var rooms = scene.Rooms
            .Select(r => new PreviewRoom(r.Center.X, r.Center.Z, r.Width, r.Depth))
            .ToList();

        var items = Sample(scene.Items, MaxItems)
            .Select(i => new PreviewItem(i.Position.X, i.Position.Z, Hex(i.Color)))
            .ToList();

        // Bounds from room footprints; fall back to item points; else a unit box around the origin.
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var r in rooms)
        {
            minX = MathF.Min(minX, r.Cx - r.W / 2f); maxX = MathF.Max(maxX, r.Cx + r.W / 2f);
            minZ = MathF.Min(minZ, r.Cz - r.D / 2f); maxZ = MathF.Max(maxZ, r.Cz + r.D / 2f);
        }
        foreach (var it in items)
        {
            minX = MathF.Min(minX, it.X); maxX = MathF.Max(maxX, it.X);
            minZ = MathF.Min(minZ, it.Z); maxZ = MathF.Max(maxZ, it.Z);
        }
        if (minX > maxX) { minX = -1; maxX = 1; minZ = -1; maxZ = 1; }

        return new SpacePreview(minX, minZ, maxX, maxZ, rooms, items);
    }

    /// <summary>Evenly samples up to <paramref name="max"/> items, preserving spatial spread.</summary>
    private static IEnumerable<Item> Sample(IReadOnlyList<Item> items, int max)
    {
        if (items.Count <= max) return items;
        var step = (double)items.Count / max;
        return Enumerable.Range(0, max).Select(i => items[(int)(i * step)]);
    }

    private static string Hex(Rgba c)
    {
        static int B(float v) => Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
        return $"#{B(c.R):x2}{B(c.G):x2}{B(c.B):x2}";
    }
}
