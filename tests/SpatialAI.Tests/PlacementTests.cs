using FluentAssertions;
using SpatialAI.Core.Analysis;
using SpatialAI.Core.Model;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class PlacementTests
{
    private static (SceneStore store, SceneTools tools) New()
    {
        var store = new SceneStore();
        return (store, new SceneTools(store));
    }

    private static Item Only(SceneStore store, string name) =>
        store.Current.Items.Single(i => i.Name == name);

    private static void AssertInside(Item item, Room room, float margin = 0.1f)
    {
        var fp = Geometry.FootprintOf(item);
        fp.MinX.Should().BeGreaterThanOrEqualTo(room.Center.X - room.Width / 2f + margin - 0.01f);
        fp.MaxX.Should().BeLessThanOrEqualTo(room.Center.X + room.Width / 2f - margin + 0.01f);
        fp.MinZ.Should().BeGreaterThanOrEqualTo(room.Center.Z - room.Depth / 2f + margin - 0.01f);
        fp.MaxZ.Should().BeLessThanOrEqualTo(room.Center.Z + room.Depth / 2f - margin + 0.01f);
    }

    [Fact] // The original bug: a big sofa "moved to the corner" punched through two walls.
    public void Move_ClampsFootprintInsideRoom()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Sofa", "box", 1.85f, 0.80f, 0.90f);

        tools.MoveItem("Sofa", 2.8f, 2.2f); // the LLM's idea of "the corner"

        AssertInside(Only(store, "Sofa"), store.Current.Rooms[0]);
    }

    [Fact]
    public void Move_DropsItemBackToTheFloor()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Crate", "box", 1f, 1f, 1f);

        tools.MoveItem("Crate", 1f, 1f);

        Only(store, "Crate").Position.Y.Should().BeApproximately(0.5f, 0.001f); // height/2
    }

    [Fact]
    public void CeilingLight_HangsFromTheCeiling_NotTheFloor()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);                 // default wall height 2.5 m
        tools.CreateItem("Light", "ceiling_light");

        var light = Only(store, "Light");
        light.Position.Y.Should().BeApproximately(2.5f - light.Size.Y / 2f, 0.01f); // top touches the ceiling
        light.Position.Y.Should().BeGreaterThan(2.0f);                              // clearly not on the floor
    }

    [Fact]
    public void Move_OnSurfaceItem_KeepsItsHeight_DoesNotDropToFloor()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Desk", "desk", positionX: 0, positionZ: 0);
        tools.CreateItem("Monitor", "monitor", onItem: "Desk");
        var yBefore = Only(store, "Monitor").Position.Y;
        yBefore.Should().BeGreaterThan(0.5f);   // sitting up on the desk

        tools.MoveItem("Monitor", 1.0f, 0.5f);

        Only(store, "Monitor").Position.Y.Should().BeApproximately(yBefore, 0.01f); // kept its height
    }

    [Fact]
    public void Move_CeilingLight_StaysOnTheCeiling()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Light", "ceiling_light");
        var yBefore = Only(store, "Light").Position.Y;

        tools.MoveItem("Light", 1.5f, 1.5f);

        Only(store, "Light").Position.Y.Should().BeApproximately(yBefore, 0.01f);
    }

    [Fact]
    public void Move_StrayItem_IsReadoptedByTheRoom()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Box", "box", 0.5f, 0.5f, 0.5f);
        var box = Only(store, "Box");
        box.RoomId = null; // simulate an item that had drifted outdoors

        tools.MoveItem("Box", null, null, anchor: "corner");

        box.RoomId.Should().Be(store.Current.Rooms[0].Id);
        AssertInside(box, store.Current.Rooms[0]);
    }

    [Fact]
    public void Anchor_Corner_PlacesInsideAndFacesTheRoom()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Sofa", "box", 1.85f, 0.80f, 0.90f, anchor: "corner:nw");

        var sofa = Only(store, "Sofa");
        AssertInside(sofa, store.Current.Rooms[0]);
        // NW corner = (-X, +Z); back to the walls, so it faces toward the room center.
        sofa.Position.X.Should().BeLessThan(0);
        sofa.Position.Z.Should().BeGreaterThan(0);
        sofa.RotationY.Should().NotBe(0); // re-oriented to face into the room
    }

    [Fact]
    public void Anchor_Wall_SitsFlushAgainstThatWall()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Shelf", "box", 1.0f, 1.8f, 0.4f, anchor: "wall:east");

        var fp = Geometry.FootprintOf(Only(store, "Shelf"));
        var room = store.Current.Rooms[0];
        fp.MaxX.Should().BeApproximately(room.Center.X + room.Width / 2f - 0.1f, 0.05f); // flush to east wall (+X)
    }

    [Fact]
    public void Placement_AvoidsOverlappingExistingItems()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Desk", "box", 1.4f, 0.75f, 0.7f, positionX: 0, positionZ: 0);
        tools.CreateItem("Sofa", "box", 1.85f, 0.8f, 0.9f, positionX: 0, positionZ: 0); // same spot

        var desk = Geometry.FootprintOf(Only(store, "Desk"));
        var sofa = Geometry.FootprintOf(Only(store, "Sofa"));
        Geometry.Gap(desk, sofa).Should().BeGreaterThanOrEqualTo(0f);
        // They must not overlap.
        (sofa.MinX < desk.MaxX && sofa.MaxX > desk.MinX &&
         sofa.MinZ < desk.MaxZ && sofa.MaxZ > desk.MinZ).Should().BeFalse();
    }

    [Fact]
    public void Anchor_Near_PlacesBesideTheTarget()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Desk", "box", 1.4f, 0.75f, 0.7f, positionX: 0, positionZ: 0);
        tools.CreateItem("Plant", "box", 0.4f, 1.1f, 0.4f, anchor: "near:Desk");

        var desk = Only(store, "Desk");
        var plant = Only(store, "Plant");
        AssertInside(plant, store.Current.Rooms[0]);
        Geometry.HorizontalDistance(desk.Position, plant.Position).Should().BeLessThan(2.0f);
    }

    [Fact]
    public void OversizedItem_IsCentered_NotNaN()
    {
        var (store, tools) = New();
        tools.CreateRoom("Closet", 2, 2);
        tools.CreateItem("Giant", "box", 5f, 1f, 5f, anchor: "corner");

        var giant = Only(store, "Giant");
        giant.Position.X.Should().Be(0);
        giant.Position.Z.Should().Be(0);
        float.IsNaN(giant.Position.X).Should().BeFalse();
    }
}
