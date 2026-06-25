using System.Globalization;
using System.Text;
using SpatialAI.Core.Model;

namespace SpatialAI.Core.Analysis;

/// <summary>
/// Finds the largest UNUSED (empty) floor regions inside a room by rasterizing the floor into an
/// occupancy grid (items occupy cells) and extracting the largest empty rectangles.
/// </summary>
public static class UnusedAreaAnalyzer
{
    public sealed record Region(Vec3 Center, float Width, float Depth)
    {
        public float AreaM2 => Width * Depth;
    }

    public sealed record Result(
        bool HasRoom,
        float FloorAreaM2,
        float OccupiedAreaM2,
        IReadOnlyList<Region> Regions,
        string Severity);

    private const int MaxRegions = 3;

    public static Result Analyze(Room? room, IEnumerable<Item> items, float cellSize = 0.25f, float minRegionM2 = 1.0f)
    {
        if (room is null)
            return new Result(false, 0, 0, [], "info");

        var fp = Geometry.FootprintOf(room);
        var cols = Math.Max(1, (int)MathF.Ceiling(fp.Width / cellSize));
        var rows = Math.Max(1, (int)MathF.Ceiling(fp.Depth / cellSize));
        var occupied = new bool[cols, rows];

        foreach (var item in items)
            MarkCells(occupied, cols, rows, fp.MinX, fp.MinZ, cellSize, Geometry.FootprintOf(item));

        var occupiedCells = 0;
        for (var c = 0; c < cols; c++)
            for (var r = 0; r < rows; r++)
                if (occupied[c, r]) occupiedCells++;

        var floorArea = fp.Area;
        var cellArea = cellSize * cellSize;
        var occupiedArea = MathF.Min(floorArea, occupiedCells * cellArea);

        var regions = new List<Region>();
        for (var i = 0; i < MaxRegions; i++)
        {
            var rect = LargestFreeRectangle(occupied, cols, rows);
            if (rect is null) break;

            var (c0, r0, cc, rc) = rect.Value;
            var w = cc * cellSize;
            var d = rc * cellSize;
            if (w * d < minRegionM2) break;

            var cx = fp.MinX + (c0 + cc / 2f) * cellSize;
            var cz = fp.MinZ + (r0 + rc / 2f) * cellSize;
            regions.Add(new Region(new Vec3(cx, 0f, cz), w, d));

            for (var c = c0; c < c0 + cc; c++)
                for (var r = r0; r < r0 + rc; r++)
                    occupied[c, r] = true;
        }

        var unusedRatio = floorArea > 0 ? (floorArea - occupiedArea) / floorArea : 0f;
        var severity = unusedRatio > 0.5f ? "warning" : "info";
        return new Result(true, floorArea, occupiedArea, regions, severity);
    }

    public static string Describe(Result result)
    {
        if (!result.HasRoom)
            return "No room to analyze. Create a room first.";

        var ci = CultureInfo.InvariantCulture;
        var usedPct = result.FloorAreaM2 > 0 ? result.OccupiedAreaM2 / result.FloorAreaM2 * 100f : 0f;
        var sb = new StringBuilder();
        sb.AppendLine($"Floor area {result.FloorAreaM2.ToString("F1", ci)} m², about {usedPct.ToString("F0", ci)}% occupied.");

        if (result.Regions.Count == 0)
        {
            sb.Append("No open region of at least 1 m² — the room is fairly full.");
            return sb.ToString();
        }

        sb.AppendLine($"Largest unused areas:");
        foreach (var r in result.Regions)
            sb.AppendLine($"- {r.Width.ToString("F1", ci)} x {r.Depth.ToString("F1", ci)} m " +
                          $"({r.AreaM2.ToString("F1", ci)} m²) centered at " +
                          $"({r.Center.X.ToString("F1", ci)}, {r.Center.Z.ToString("F1", ci)})");
        return sb.ToString().TrimEnd();
    }

    private static void MarkCells(bool[,] occupied, int cols, int rows, float minX, float minZ, float cell, Geometry.Footprint fp)
    {
        var c0 = Math.Clamp((int)MathF.Floor((fp.MinX - minX) / cell), 0, cols - 1);
        var c1 = Math.Clamp((int)MathF.Ceiling((fp.MaxX - minX) / cell) - 1, 0, cols - 1);
        var r0 = Math.Clamp((int)MathF.Floor((fp.MinZ - minZ) / cell), 0, rows - 1);
        var r1 = Math.Clamp((int)MathF.Ceiling((fp.MaxZ - minZ) / cell) - 1, 0, rows - 1);
        for (var c = c0; c <= c1; c++)
            for (var r = r0; r <= r1; r++)
                occupied[c, r] = true;
    }

    private static (int col, int row, int colCount, int rowCount)? LargestFreeRectangle(bool[,] occupied, int cols, int rows)
    {
        var heights = new int[cols];
        (int, int, int, int)? best = null;
        var bestArea = 0;

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
                heights[c] = occupied[c, r] ? 0 : heights[c] + 1;

            var stack = new Stack<(int start, int height)>();
            for (var c = 0; c <= cols; c++)
            {
                var curH = c == cols ? 0 : heights[c];
                var start = c;
                while (stack.Count > 0 && stack.Peek().height > curH)
                {
                    var (s, h) = stack.Pop();
                    var area = h * (c - s);
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = (s, r - h + 1, c - s, h);
                    }
                    start = s;
                }
                stack.Push((start, curH));
            }
        }

        return bestArea > 0 ? best : null;
    }
}
