using FluentAssertions;
using SpatialAI.Core.Furniture;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class SurfaceTests
{
    private static (SceneStore store, SceneTools tools) New()
    {
        var store = new SceneStore();
        return (store, new SceneTools(store));
    }

    [Fact]
    public void CreateItem_OnItem_RestsOnTheSurface()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 5, 5);
        tools.CreateItem("Table", "table", positionX: 0, positionZ: 0);
        var table = store.Current.Items.Single(i => i.Name == "Table");
        var tableTop = table.Position.Y + table.Size.Y / 2f;

        tools.CreateItem("Plate", "plate", onItem: "Table");

        var plate = store.Current.Items.Single(i => i.Name == "Plate");
        plate.Position.Y.Should().BeApproximately(tableTop + plate.Size.Y / 2f, 0.001f);
        plate.Position.Y.Should().BeGreaterThan(0.3f);            // well above the floor
        plate.Position.X.Should().BeApproximately(table.Position.X, 0.001f); // centered on table
    }

    [Fact]
    public void ArrangeOn_PutsItemsOnTheSurfaceWithinFootprint()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Table", "table", positionX: 0, positionZ: 0);
        var table = store.Current.Items.Single(i => i.Name == "Table");
        var tableTop = table.Position.Y + table.Size.Y / 2f;

        tools.ArrangeOn("Table", "plate", 4);

        var plates = store.Current.Items.Where(i => i.Kind == "plate").ToList();
        plates.Should().HaveCount(4);
        foreach (var p in plates)
        {
            p.Position.Y.Should().BeApproximately(tableTop + p.Size.Y / 2f, 0.001f);
            MathF.Abs(p.Position.X - table.Position.X).Should().BeLessThan(table.Size.X / 2f);
            MathF.Abs(p.Position.Z - table.Position.Z).Should().BeLessThan(table.Size.Z / 2f);
            p.RoomId.Should().Be(table.RoomId);
        }
    }

    [Fact]
    public void CreateItem_OnUnknownItem_FallsBackToFloor()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office");
        tools.CreateItem("Plate", "plate", onItem: "Nonexistent");

        var plate = store.Current.Items.Single();
        plate.Position.Y.Should().BeApproximately(plate.Size.Y / 2f, 0.001f); // on the floor
    }

    [Theory]
    [InlineData("plate")]
    [InlineData("cup")]
    [InlineData("bowl")]
    [InlineData("book")]
    [InlineData("vase")]
    [InlineData("laptop")]
    public void TabletopKinds_Build(string kind)
    {
        FurnitureFactory.IsKnown(kind).Should().BeTrue();
        var built = FurnitureFactory.Build(kind, null, null, null, null);
        built.Should().NotBeNull();
        built!.Parts.Count.Should().BeGreaterThanOrEqualTo(1);
        built.Size.Y.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Aliases_Resolve()
    {
        FurnitureFactory.Normalize("dish").Should().Be("plate");
        FurnitureFactory.Normalize("mug").Should().Be("cup");
    }
}
