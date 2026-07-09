using System.Text.Json;
using FluentAssertions;
using SpatialAI.Core.Analysis;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class GroupTests
{
    private static (SceneStore store, SceneTools tools) New()
    {
        var store = new SceneStore();
        return (store, new SceneTools(store));
    }

    [Fact]
    public void CreateAndAddToGroup_SetsMembership()
    {
        var (store, tools) = New();
        tools.CreateRoom("Room", 10, 10);
        tools.CreateItem("A", "box", 0.5f, 0.5f, 0.5f, positionX: 0, positionZ: 0);
        tools.CreateItem("B", "box", 0.5f, 0.5f, 0.5f, positionX: 1, positionZ: 0);

        tools.CreateGroup("Cell");
        tools.AddToGroup("Cell", ["A", "B"]);

        var group = store.Current.Groups.Single(g => g.Name == "Cell");
        store.Current.Items.Count(i => i.GroupId == group.Id).Should().Be(2);
    }

    [Fact]
    public void MoveGroup_ShiftsAllMembersRigidlyAndStaysInRoom()
    {
        var (store, tools) = New();
        tools.CreateRoom("Room", 10, 10);
        tools.CreateItem("A", "box", 0.5f, 0.5f, 0.5f, positionX: 0, positionZ: 0);
        tools.CreateItem("B", "box", 0.5f, 0.5f, 0.5f, positionX: 1, positionZ: 0);
        tools.AddToGroup("Cell", ["A", "B"]);

        var a0 = store.Current.Items.Single(i => i.Name == "A").Position;
        var b0 = store.Current.Items.Single(i => i.Name == "B").Position;

        tools.MoveGroup("Cell", null, null, anchor: "corner:nw");

        var a = store.Current.Items.Single(i => i.Name == "A");
        var b = store.Current.Items.Single(i => i.Name == "B");
        // rigid move: A->B offset preserved
        (b.Position.X - a.Position.X).Should().BeApproximately(b0.X - a0.X, 0.001f);
        (b.Position.Z - a.Position.Z).Should().BeApproximately(b0.Z - a0.Z, 0.001f);
        // both moved by the same delta
        (a.Position.X - a0.X).Should().BeApproximately(b.Position.X - b0.X, 0.001f);
        // both inside the room
        foreach (var it in new[] { a, b })
        {
            var fp = Geometry.FootprintOf(it);
            fp.MinX.Should().BeGreaterThanOrEqualTo(-5f - 0.01f);
            fp.MaxX.Should().BeLessThanOrEqualTo(5f + 0.01f);
            fp.MinZ.Should().BeGreaterThanOrEqualTo(-5f - 0.01f);
            fp.MaxZ.Should().BeLessThanOrEqualTo(5f + 0.01f);
        }
    }

    [Fact]
    public void DeleteGroup_RemovesItemsByDefault_OrDisbands()
    {
        var (store, tools) = New();
        tools.CreateRoom("Room", 10, 10);
        tools.CreateItem("A", "box");
        tools.CreateItem("B", "box");
        tools.AddToGroup("Zone", ["A", "B"]);

        tools.DeleteGroup("Zone"); // deletes items by default
        store.Current.Items.Should().BeEmpty();
        store.Current.Groups.Should().BeEmpty();

        // disband keeps the items
        tools.CreateItem("C", "box");
        tools.AddToGroup("Keep", ["C"]);
        tools.DeleteGroup("Keep", deleteItems: false);
        store.Current.Items.Should().ContainSingle(i => i.Name == "C");
        store.Current.Items.Single(i => i.Name == "C").GroupId.Should().BeNull();
        store.Current.Groups.Should().BeEmpty();
    }

    [Fact] // the router must parse the itemNames JSON array (the wiring the LLM/MCP hit)
    public void Router_AddToGroup_ParsesItemNamesArray()
    {
        var (store, tools) = New();
        tools.CreateRoom("Room", 10, 10);
        tools.CreateItem("A", "box");
        tools.CreateItem("B", "box");

        var args = JsonDocument.Parse("""{"groupName":"Cell","itemNames":["A","B"]}""").RootElement;
        SceneToolRouter.Invoke(tools, "add_to_group", args);

        var group = store.Current.Groups.Single(g => g.Name == "Cell");
        store.Current.Items.Count(i => i.GroupId == group.Id).Should().Be(2);
    }

    [Fact]
    public void ListScene_IncludesGroups()
    {
        var (store, tools) = New();
        tools.CreateRoom("Room", 10, 10);
        tools.CreateItem("A", "box");
        tools.AddToGroup("Cell", ["A"]);

        tools.ListScene().Should().Contain("Group 'Cell'");
    }
}
