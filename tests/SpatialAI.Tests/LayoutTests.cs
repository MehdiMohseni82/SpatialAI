using System.Text.Json;
using FluentAssertions;
using SpatialAI.Core.Analysis;
using SpatialAI.Core.Model;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class LayoutTests
{
    private static readonly string[] MachineKinds = ["cnc_machine", "robot_arm", "press", "workbench"];

    private static (SceneStore store, SceneTools tools) New()
    {
        var store = new SceneStore();
        return (store, new SceneTools(store));
    }

    private static bool Inside(Item item, Room room, float margin = 0f)
    {
        var fp = Geometry.FootprintOf(item);
        return fp.MinX >= room.Center.X - room.Width / 2f - margin
            && fp.MaxX <= room.Center.X + room.Width / 2f + margin
            && fp.MinZ >= room.Center.Z - room.Depth / 2f - margin
            && fp.MaxZ <= room.Center.Z + room.Depth / 2f + margin;
    }

    [Fact]
    public void CreateWarehouse_BuildsShellWithDockDoors()
    {
        var (store, tools) = New();
        tools.CreateWarehouse("WH", 24f, 36f, 8f, dockDoors: 3);

        var room = store.Current.Rooms.Should().ContainSingle().Subject;
        room.Width.Should().Be(24f);
        room.Depth.Should().Be(36f);
        room.Height.Should().Be(8f);
        room.Roof.Should().Be("flat");
        room.FloorColor.R.Should().BeApproximately(0.62f, 0.01f); // concrete, not default gray
        room.Openings.Where(o => o.Type == "door").Should().HaveCount(3).And.OnlyContain(o => o.Wall == "south");
    }

    [Fact]
    public void CreateProductionLine_GroupsStationsAndConveyors_InsideRoom()
    {
        var (store, tools) = New();
        tools.CreateRoom("Floor", 30, 30);
        tools.CreateProductionLine("Line", stations: 5);

        var group = store.Current.Groups.Should().ContainSingle(g => g.Name == "Line").Subject;
        var members = store.Current.Items.Where(i => i.GroupId == group.Id).ToList();

        members.Count(i => MachineKinds.Contains(i.Kind)).Should().Be(5);   // one machine per station
        members.Count(i => i.Kind == "conveyor").Should().Be(5);            // a conveyor spine segment each
        var room = store.Current.Rooms[0];
        members.Should().OnlyContain(i => Inside(i, room, 0.05f));
    }

    [Fact]
    public void CreateRackAisles_PlacesGridGrouped_WithoutOverlap()
    {
        var (store, tools) = New();
        tools.CreateRoom("Floor", 30, 30);
        tools.CreateRackAisles("Racking", rows: 3, racksPerRow: 4);

        var group = store.Current.Groups.Single(g => g.Name == "Racking");
        var racks = store.Current.Items.Where(i => i.GroupId == group.Id).ToList();

        racks.Should().HaveCount(12);
        var room = store.Current.Rooms[0];
        racks.Should().OnlyContain(i => Inside(i, room, 0.05f));

        // no two racks overlap
        for (var i = 0; i < racks.Count; i++)
            for (var j = i + 1; j < racks.Count; j++)
            {
                var a = Geometry.FootprintOf(racks[i]);
                var b = Geometry.FootprintOf(racks[j]);
                var overlap = a.MinX < b.MaxX - 0.001f && a.MaxX > b.MinX + 0.001f
                           && a.MinZ < b.MaxZ - 0.001f && a.MaxZ > b.MinZ + 0.001f;
                overlap.Should().BeFalse();
            }
    }

    [Fact] // guards the LLM/MCP wiring for a layout tool
    public void Router_CreateProductionLine_BuildsGroupedLine()
    {
        var (store, tools) = New();
        tools.CreateRoom("Floor", 30, 30);

        var args = JsonDocument.Parse("""{"name":"Line A","stations":3}""").RootElement;
        SceneToolRouter.Invoke(tools, "create_production_line", args);

        var group = store.Current.Groups.Single(g => g.Name == "Line A");
        store.Current.Items.Count(i => i.GroupId == group.Id && MachineKinds.Contains(i.Kind)).Should().Be(3);
    }
}
