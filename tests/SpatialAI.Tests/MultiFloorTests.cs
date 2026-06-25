using FluentAssertions;
using SpatialAI.Core.Furniture;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class MultiFloorTests
{
    private static (SceneStore store, SceneTools tools) New()
    {
        var store = new SceneStore();
        return (store, new SceneTools(store));
    }

    [Fact]
    public void CreateRoom_StoresElevationAndLevel()
    {
        var (store, tools) = New();
        tools.CreateRoom("Upstairs", 6, 5, elevation: 3f, level: 1);

        var room = store.Current.Rooms.Single();
        room.Elevation.Should().BeApproximately(3f, 0.001f);
        room.Level.Should().Be(1);
    }

    [Fact]
    public void ItemInElevatedRoom_RestsOnThatStoreyAndInheritsLevel()
    {
        var (store, tools) = New();
        tools.CreateRoom("Upstairs", 6, 5, elevation: 3f, level: 1);
        tools.CreateItem("Bed", "bed", roomName: "Upstairs");

        var bed = store.Current.Items.Single();
        bed.Level.Should().Be(1);
        // bottom of the bed sits on the storey floor (elevation 3)
        (bed.Position.Y - bed.Size.Y / 2f).Should().BeApproximately(3f, 0.01f);
    }

    [Fact]
    public void GroundRoom_KeepsItemsOnTheFloor()
    {
        var (store, tools) = New();
        tools.CreateRoom("Ground", 6, 5);   // elevation 0, level 0
        tools.CreateItem("Sofa", "sofa", roomName: "Ground");

        var sofa = store.Current.Items.Single();
        sofa.Level.Should().Be(0);
        (sofa.Position.Y - sofa.Size.Y / 2f).Should().BeApproximately(0f, 0.01f);
    }

    [Fact]
    public void SetBuildingRoof_SpansFootprintAboveTallestStorey()
    {
        var (store, tools) = New();
        tools.CreateRoom("Ground", 8, 6, centerX: 0, centerZ: 0, height: 2.5f, level: 0, elevation: 0);
        tools.CreateRoom("Upstairs", 8, 6, centerX: 0, centerZ: 0, height: 2.5f, level: 1, elevation: 2.5f);

        tools.SetBuildingRoof("mansard");

        var roof = store.Current.Roof;
        roof.Should().NotBeNull();
        roof!.Style.Should().Be("mansard");
        (roof.MaxX - roof.MinX).Should().BeApproximately(8f, 0.01f);
        (roof.MaxZ - roof.MinZ).Should().BeApproximately(6f, 0.01f);
        roof.BaseY.Should().BeApproximately(5f, 0.01f); // top of the upper storey (2.5 + 2.5)
    }

    [Fact]
    public void SetBuildingRoof_None_RemovesIt()
    {
        var (store, tools) = New();
        tools.CreateRoom("Ground", 6, 5);
        tools.SetBuildingRoof("gable");
        store.Current.Roof.Should().NotBeNull();

        tools.SetBuildingRoof("none");
        store.Current.Roof.Should().BeNull();
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("bush")]
    [InlineData("hedge")]
    [InlineData("lawn")]
    [InlineData("fence")]
    [InlineData("gate")]
    [InlineData("car")]
    [InlineData("terrace")]
    [InlineData("garage")]
    public void OutdoorKinds_Build(string kind)
    {
        FurnitureFactory.IsKnown(kind).Should().BeTrue();
        var built = FurnitureFactory.Build(kind, null, null, null, null);
        built.Should().NotBeNull();
        built!.Parts.Count.Should().BeGreaterThanOrEqualTo(1);
        built.Size.X.Should().BeGreaterThan(0);
        built.Size.Y.Should().BeGreaterThan(0);
        built.Size.Z.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("vehicle", "car")]
    [InlineData("deck", "terrace")]
    [InlineData("shrub", "bush")]
    [InlineData("grass", "lawn")]
    public void OutdoorAliases_Resolve(string alias, string expected)
        => FurnitureFactory.Normalize(alias).Should().Be(expected);
}
