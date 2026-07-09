using FluentAssertions;
using SpatialAI.Core.Analysis;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class SuggestionTests
{
    private static (SceneStore store, SceneTools tools) New()
    {
        var store = new SceneStore();
        return (store, new SceneTools(store));
    }

    [Fact]
    public void Empty_scene_offers_openers()
    {
        var (store, _) = New();
        SuggestionEngine.Suggest(store.Current).Should().Contain("Create a 6x5 office");
    }

    [Fact]
    public void Room_with_no_items_suggests_furnishing()
    {
        var (store, t) = New();
        t.CreateRoom("Office", 6, 5);
        SuggestionEngine.Suggest(store.Current).Should().Contain("Add a desk and a chair");
    }

    [Fact]
    public void Desk_without_a_chair_suggests_adding_a_chair()
    {
        var (store, t) = New();
        t.CreateRoom("Office", 6, 5);
        t.CreateItem("Desk", "desk", roomName: "Office");
        SuggestionEngine.Suggest(store.Current).Should().Contain("Add a chair at each desk");
    }

    [Fact]
    public void Desk_with_a_chair_does_not_suggest_adding_a_chair()
    {
        var (store, t) = New();
        t.CreateRoom("Office", 6, 5);
        t.CreateItem("Desk", "desk", roomName: "Office");
        t.CreateItem("Chair", "chair", roomName: "Office");
        SuggestionEngine.Suggest(store.Current).Should().NotContain("Add a chair at each desk");
    }

    [Fact]
    public void Yard_suggests_enclosing_it_with_a_fence()
    {
        var (store, t) = New();
        t.CreateRoom("Yard", 12, 10);
        SuggestionEngine.Suggest(store.Current).Should().Contain("Put a fence around the yard");
    }

    [Fact]
    public void Result_is_capped_and_deduplicated()
    {
        var (store, t) = New();
        t.CreateRoom("Office", 6, 5);
        t.CreateItem("Desk", "desk", roomName: "Office");

        var result = SuggestionEngine.Suggest(store.Current);
        result.Count.Should().BeLessThanOrEqualTo(3);
        result.Should().OnlyHaveUniqueItems();
    }
}
