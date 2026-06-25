using FluentAssertions;
using SpatialAI.Core.Analysis;
using SpatialAI.Core.Model;
using Xunit;

namespace SpatialAI.Tests;

public class AnalyzerTests
{
    private static Room Office(float size = 6f) => new()
    {
        Name = "Office",
        Center = Vec3.Zero,
        Width = size,
        Depth = size,
        Height = 2.5f
    };

    private static Item Box(string name, float x, float z, float w = 0.5f, float h = 0.5f, float d = 0.5f) => new()
    {
        Name = name,
        Shape = Shape.Box,
        Position = new Vec3(x, h / 2f, z),
        Size = new Vec3(w, h, d)
    };

    [Fact]
    public void UnusedArea_EmptyRoom_IsMostlyFree_Warning()
    {
        var result = UnusedAreaAnalyzer.Analyze(Office(6), []);

        result.HasRoom.Should().BeTrue();
        result.Regions.Should().NotBeEmpty();
        result.Regions[0].AreaM2.Should().BeGreaterThan(20f); // ~36 m² room
        result.Severity.Should().Be("warning");
    }

    [Fact]
    public void UnusedArea_NoRoom_ReturnsInfo()
    {
        var result = UnusedAreaAnalyzer.Analyze(null, []);

        result.HasRoom.Should().BeFalse();
        result.Regions.Should().BeEmpty();
        result.Severity.Should().Be("info");
    }

    [Fact]
    public void UnusedArea_ItemReducesFreeArea()
    {
        var empty = UnusedAreaAnalyzer.Analyze(Office(6), []);
        var withBox = UnusedAreaAnalyzer.Analyze(Office(6), [Box("Crate", 0, 0, 2, 1, 2)]);

        withBox.OccupiedAreaM2.Should().BeGreaterThan(empty.OccupiedAreaM2);
    }

    [Fact]
    public void UnusedArea_BelowThreshold_Excluded()
    {
        var result = UnusedAreaAnalyzer.Analyze(Office(6), [], minRegionM2: 1000f);
        result.Regions.Should().BeEmpty();
    }

    [Fact]
    public void Ergonomics_CloseItem_Warns()
    {
        var result = ErgonomicsAnalyzer.Analyze([Box("Cabinet", 0.3f, 0)], new Vec3(0, 1.6f, 0));

        result.OverallSeverity.Should().Be("warning");
        result.Findings.Should().Contain(f => f.Issue.Contains("close"));
    }

    [Fact]
    public void Ergonomics_NoItems_Info()
    {
        var result = ErgonomicsAnalyzer.Analyze([], new Vec3(0, 1.6f, 0));

        result.OverallSeverity.Should().Be("info");
        result.Findings.Should().ContainSingle().Which.Issue.Should().Contain("No items");
    }

    [Fact]
    public void Ergonomics_MonitorTooFar_Warns()
    {
        var monitor = Box("Monitor", 0, 1.0f, 0.6f, 0.4f, 0.05f);
        monitor.Position = monitor.Position with { Y = 1.2f };

        var result = ErgonomicsAnalyzer.Analyze([monitor], new Vec3(0, 1.6f, 0));

        result.Findings.Should().Contain(f => f.Severity == "warning" && f.Issue.Contains("comfort range"));
    }

    [Fact]
    public void Ergonomics_IsDeterministic()
    {
        var items = new[] { Box("Cabinet", 0.3f, 0), Box("Shelf", 0.35f, 0.1f) };
        var a = ErgonomicsAnalyzer.Describe(ErgonomicsAnalyzer.Analyze(items, new Vec3(0, 1.6f, 0)));
        var b = ErgonomicsAnalyzer.Describe(ErgonomicsAnalyzer.Analyze(items, new Vec3(0, 1.6f, 0)));
        a.Should().Be(b);
    }
}
