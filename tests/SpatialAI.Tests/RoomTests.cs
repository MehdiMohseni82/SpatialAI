using FluentAssertions;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class RoomTests
{
    private static SceneTools New() => new(new SceneStore());

    [Fact]
    public void Analysis_with_no_room_picks_the_room_with_furniture_not_the_last_empty_room()
    {
        var t = New();
        t.CreateRoom("Studio", 5, 4);
        t.CreateItem("Chair", "chair", roomName: "Studio");
        t.CreateItem("Desk", "desk", roomName: "Studio");
        t.CreateRoom("Empty");   // added last, has no items — the old default would pick this

        // No room specified → should analyze Studio (which has furniture), not the empty last room.
        t.AnalyzeErgonomics().Should().NotContain("No items to evaluate");
        t.FindUnusedAreas().Should().NotContain("No room");
    }

    [Fact]
    public void Delete_room_removes_the_room_and_the_items_inside_it()
    {
        var t = New();
        t.CreateRoom("Studio", 5, 4);
        t.CreateItem("Chair", "chair", roomName: "Studio");
        t.CreateRoom("Marker", 3, 3);

        t.DeleteRoom("Marker").Should().Contain("Deleted room 'Marker'");
        t.ListScene().Should().NotContain("Marker");

        var msg = t.DeleteRoom("Studio");
        msg.Should().Contain("Deleted room 'Studio'");
        msg.Should().Contain("item");                 // reports it removed the chair too
        t.ListScene().Should().NotContain("Chair");
    }

    [Fact]
    public void Delete_room_with_unknown_name_reports_no_match()
    {
        var t = New();
        t.CreateRoom("Studio");
        t.DeleteRoom("does-not-exist").Should().Contain("No room matching");
    }
}
