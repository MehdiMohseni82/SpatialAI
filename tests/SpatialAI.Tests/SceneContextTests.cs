using FluentAssertions;
using SpatialAI.Core.Model;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class SceneContextTests
{
    [Fact]
    public void ToJson_RoomWithWindowDoorAndItem_ContainsExpectedFields_ExcludesParts()
    {
        var store = new SceneStore();
        var tools = new SceneTools(store);
        tools.CreateRoom("Main Room", 6, 10, windows: 1, doors: 1);
        tools.CreateItem("Chair", "chair", colorR: 0.2f, colorG: 0.4f, colorB: 0.7f, positionX: 1.2f, positionZ: -1.5f);

        var json = SceneContext.ToJson(store.Current);

        json.Should().Contain("\"name\":\"Main Room\"");
        json.Should().Contain("\"windows\"");
        json.Should().Contain("\"doors\"");
        json.Should().Contain("\"wall\":\"north\"");
        json.Should().Contain("\"name\":\"Chair\"");
        json.Should().Contain("\"kind\":\"chair\"");
        json.Should().Contain("\"position\"");
        json.Should().NotContain("parts");
    }

    [Fact]
    public void ToJson_EmptyScene_ReturnsMinimalJson()
    {
        var json = SceneContext.ToJson(new SpatialAI.Core.Model.Scene());

        json.Should().Be("{\"rooms\":[],\"groups\":[],\"items\":[]}");
    }

    [Fact]
    public void ToJson_LargeScene_SummarizesInsteadOfListingItems()
    {
        var scene = new SpatialAI.Core.Model.Scene();
        for (var i = 0; i < SceneContext.MaxDetailedItems + 50; i++)
            scene.Items.Add(new Item { Name = $"Tree {i}", Kind = "tree", Position = new Vec3(i, 0.5f, 0), Size = new Vec3(1, 1, 1) });

        var json = SceneContext.ToJson(scene);

        json.Should().Contain("\"summary\"");
        json.Should().Contain("\"itemCount\":" + (SceneContext.MaxDetailedItems + 50));
        json.Should().Contain("\"itemsByKind\"");
        json.Should().Contain("\"bounds\"");
        json.Should().NotContain("\"Tree 5\""); // individual items are not enumerated
    }

    [Fact]
    public void ToJson_Groups_ReportNameAndItemCount()
    {
        var scene = new SpatialAI.Core.Model.Scene();
        var garden = new Group { Name = "Garden" };
        scene.Groups.Add(garden);
        scene.Items.Add(new Item { Name = "Oak", Kind = "tree", GroupId = garden.Id, Size = new Vec3(1, 2, 1) });
        scene.Items.Add(new Item { Name = "Birch", Kind = "tree", GroupId = garden.Id, Size = new Vec3(1, 2, 1) });

        var json = SceneContext.ToJson(scene);

        json.Should().Contain("\"name\":\"Garden\"");
        json.Should().Contain("\"itemCount\":2");
        json.Should().Contain("\"group\":\"Garden\""); // detailed items carry their group name
    }

    [Fact]
    public void ToJson_CompositeItem_AppearsOnceWithBoundingSize_NotParts()
    {
        var store = new SceneStore();
        var tools = new SceneTools(store);
        tools.CreateRoom("Office", 5, 5);
        tools.CreateItem("Chair", "chair");

        var json = SceneContext.ToJson(store.Current);
        var item = store.Current.Items.Single();

        json.Should().Contain("\"name\":\"Chair\"");
        json.Should().Contain("\"size\":");
        json.Should().MatchRegex(@"""size"":\{""x"":\d+\.?\d*,""y"":0\.9,""z"":\d+\.?\d*\}");
        json.Should().NotContain("parts");
        json.Split("\"name\":\"Chair\"").Should().HaveCount(2);
    }
}
