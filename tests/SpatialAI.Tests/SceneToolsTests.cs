using FluentAssertions;
using SpatialAI.Core.Model;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class SceneToolsTests
{
    private static (SceneStore store, SceneTools tools) New()
    {
        var store = new SceneStore();
        return (store, new SceneTools(store));
    }

    [Fact]
    public void CreateRoom_AddsRoom()
    {
        var (store, tools) = New();
        var msg = tools.CreateRoom("Office", 5, 4);

        msg.Should().Contain("Office");
        store.Current.Rooms.Should().ContainSingle().Which.Width.Should().Be(5);
    }

    [Fact]
    public void CreateItem_RestsOnFloor_AndJoinsRoom()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 5, 5);
        tools.CreateItem("Chair", "box", 0.5f, 0.9f, 0.5f);

        var item = store.Current.Items.Should().ContainSingle().Subject;
        item.Name.Should().Be("Chair");
        item.Position.Y.Should().BeApproximately(0.45f, 0.001f); // height/2
        item.RoomId.Should().Be(store.Current.Rooms[0].Id);
    }

    [Fact]
    public void CreateItem_AutoPlacesInsideRoomBounds()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 6);
        tools.CreateItem("Table", "box", 1.0f, 0.75f, 0.6f); // no position given

        var item = store.Current.Items[0];
        item.Position.X.Should().BeInRange(-3f, 3f);
        item.Position.Z.Should().BeInRange(-3f, 3f);
    }

    [Fact]
    public void MoveRotateScaleRecolor_MutateItem()
    {
        var (store, tools) = New();
        tools.CreateRoom("R");
        tools.CreateItem("Box");

        tools.MoveItem("Box", 1.5f, -1f);
        tools.RotateItem("Box", 90);
        tools.ScaleItem("Box", 2f);
        tools.RecolorItem("Box", 1, 0, 0);

        var item = store.Current.Items[0];
        item.Position.X.Should().Be(1.5f);
        item.RotationY.Should().Be(90);
        item.Size.X.Should().BeApproximately(1.0f, 0.001f); // 0.5 * 2
        item.Color.Should().Be(new Rgba(1, 0, 0));
    }

    [Fact]
    public void DeleteItem_RemovesIt()
    {
        var (store, tools) = New();
        tools.CreateRoom("R");
        tools.CreateItem("Box");
        tools.DeleteItem("Box");

        store.Current.Items.Should().BeEmpty();
    }

    [Fact]
    public void FindUnusedAreas_PopulatesHighlights()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 6);
        tools.CreateItem("Crate", "box", 1, 1, 1, positionX: 0, positionZ: 0);

        var msg = tools.FindUnusedAreas();

        msg.Should().Contain("Floor area");
        store.Current.Highlights.Should().NotBeEmpty();
    }

    [Fact]
    public void Mutations_RaiseChangedEvent()
    {
        var (store, tools) = New();
        var count = 0;
        store.Changed += () => count++;

        tools.CreateRoom("R");
        tools.CreateItem("Box");

        count.Should().Be(2);
    }
}
