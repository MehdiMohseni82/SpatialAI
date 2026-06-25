using System.Globalization;
using System.Text;
using SpatialAI.Core.Model;

namespace SpatialAI.Core.Analysis;

/// <summary>
/// Flags ergonomic issues among items relative to a user position: items too close or out of reach,
/// monitor distance/height, and tight clearances.
/// </summary>
public static class ErgonomicsAnalyzer
{
    private const float ReachMin = 0.45f;
    private const float DeskReachMax = 1.2f;
    private const float MonitorDistMin = 0.5f;
    private const float MonitorDistMax = 0.8f;
    private const float ClearanceMin = 0.7f;
    private const float EyeLevelTolerance = 0.15f;

    public sealed record Finding(string Issue, string Severity);
    public sealed record Result(IReadOnlyList<Finding> Findings, string OverallSeverity);

    public static Result Analyze(IReadOnlyList<Item> items, Vec3 userPosition)
    {
        if (items.Count == 0)
            return new Result([new Finding("No items to evaluate.", "info")], "info");

        var ci = CultureInfo.InvariantCulture;
        var findings = new List<Finding>();

        foreach (var item in items)
        {
            var d = Geometry.HorizontalDistance(userPosition, item.Position);
            if (d < ReachMin)
                findings.Add(new Finding($"'{item.Name}' is uncomfortably close ({d.ToString("F2", ci)} m).", "warning"));
            else if (IsDesk(item) && d > DeskReachMax)
                findings.Add(new Finding($"'{item.Name}' (desk) is out of easy reach ({d.ToString("F2", ci)} m).", "info"));
        }

        var monitor = items.Where(IsMonitor)
            .OrderBy(i => Geometry.HorizontalDistance(userPosition, i.Position))
            .FirstOrDefault();
        if (monitor != null)
        {
            var d = Geometry.HorizontalDistance(userPosition, monitor.Position);
            if (d < MonitorDistMin || d > MonitorDistMax)
                findings.Add(new Finding($"Monitor '{monitor.Name}' distance {d.ToString("F2", ci)} m is outside the 0.5-0.8 m comfort range.", "warning"));
            if (monitor.Position.Y > userPosition.Y + EyeLevelTolerance)
                findings.Add(new Finding($"Monitor '{monitor.Name}' is above eye level; consider lowering it.", "info"));
        }

        for (var i = 0; i < items.Count; i++)
            for (var j = i + 1; j < items.Count; j++)
            {
                var gap = Geometry.Gap(Geometry.FootprintOf(items[i]), Geometry.FootprintOf(items[j]));
                if (gap is > 0f and < ClearanceMin)
                    findings.Add(new Finding($"Tight clearance ({gap.ToString("F2", ci)} m) between '{items[i].Name}' and '{items[j].Name}'.", "info"));
            }

        if (findings.Count == 0)
            findings.Add(new Finding("No ergonomic issues detected.", "info"));

        findings = findings
            .OrderBy(f => Rank(f.Severity))
            .ThenBy(f => f.Issue, StringComparer.Ordinal)
            .ToList();

        var overall = findings.Any(f => f.Severity == "warning") ? "warning" : "info";
        return new Result(findings, overall);
    }

    public static string Describe(Result result)
    {
        var sb = new StringBuilder();
        foreach (var f in result.Findings)
        {
            var icon = f.Severity == "warning" ? "[!]" : "[i]";
            sb.AppendLine($"{icon} {f.Issue}");
        }
        return sb.ToString().TrimEnd();
    }

    private static bool IsDesk(Item i) => i.Size.Y is >= 0.68f and <= 0.90f && i.Size.X * i.Size.Z >= 0.30f;

    private static bool IsMonitor(Item i)
    {
        var thickness = MathF.Min(i.Size.X, i.Size.Z);
        return thickness <= 0.12f && i.Size.Y is >= 0.25f and <= 0.70f;
    }

    private static int Rank(string severity) => severity == "warning" ? 0 : 1;
}
