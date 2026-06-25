using FluentAssertions;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class RoomShellTests
{
    private static (SceneStore store, SceneTools tools) New()
    {
        var store = new SceneStore();
        return (store, new SceneTools(store));
    }

    [Fact]
    public void CreateRoom_WithCounts_PopulatesOpeningsCeilingRoof()
    {
        var (store, tools) = New();
        tools.CreateRoom("Main", 6, 8, windows: 3, doors: 1, ceiling: true, roof: "gable");

        var room = store.Current.Rooms.Should().ContainSingle().Subject;
        room.Openings.Count(o => o.Type == "window").Should().Be(3);
        room.Openings.Count(o => o.Type == "door").Should().Be(1);
        room.Ceiling.Should().BeTrue();
        room.Roof.Should().Be("gable");
    }

    [Fact]
    public void AutoWindows_SpreadAcrossDifferentWalls()
    {
        var (store, tools) = New();
        tools.CreateRoom("Main", 6, 8, windows: 3);

        var walls = store.Current.Rooms[0].Openings.Select(o => o.Wall).Distinct();
        walls.Should().HaveCount(3); // north, east, west
    }

    [Fact]
    public void AddWindow_And_AddDoor_AppendOpenings()
    {
        var (store, tools) = New();
        tools.CreateRoom("Main", 5, 5);
        tools.AddWindow(null, "east", width: 1.5f, height: 1.0f, sill: 1.0f);
        tools.AddDoor(null, "north");

        var ops = store.Current.Rooms[0].Openings;
        ops.Should().HaveCount(2);
        var window = ops.Single(o => o.Type == "window");
        window.Wall.Should().Be("east");
        window.Width.Should().Be(1.5f);
        ops.Single(o => o.Type == "door").Sill.Should().Be(0f);
    }

    [Fact]
    public void WallSynonyms_Normalize()
    {
        var (store, tools) = New();
        tools.CreateRoom("Main", 5, 5);
        tools.AddWindow(null, "left");   // -> west
        tools.AddWindow(null, "front");  // -> south

        var walls = store.Current.Rooms[0].Openings.Select(o => o.Wall).ToList();
        walls.Should().Contain("west");
        walls.Should().Contain("south");
    }

    [Fact]
    public void AddPartition_WithDoorway()
    {
        var (store, tools) = New();
        tools.CreateRoom("Main", 6, 4);
        tools.AddPartition(null, "x", 0f, doorWidth: 0.9f);

        var p = store.Current.Rooms[0].Partitions.Should().ContainSingle().Subject;
        p.Axis.Should().Be("x");
        p.DoorWidth.Should().Be(0.9f);
    }

    [Fact]
    public void SetCeiling_And_SetRoof_Mutate()
    {
        var (store, tools) = New();
        tools.CreateRoom("Main", 4, 4);
        tools.SetCeiling(null, true);
        tools.SetRoof(null, "flat");

        store.Current.Rooms[0].Ceiling.Should().BeTrue();
        store.Current.Rooms[0].Roof.Should().Be("flat");
    }
}
