using FluentAssertions;
using SpatialAI.Api.Blueprint;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class BuildingReconstructorTests
{
    private static (SceneStore store, BuildingReconstructor recon) New()
    {
        var store = new SceneStore();
        return (store, new BuildingReconstructor(new SceneTools(store)));
    }

    private static BuildingPlan SampleHouse() => new(
        Floors:
        [
            new FloorPlan(Level: -1, Name: "Basement", Elevation: -2.5f, Height: 2.5f,
                Rooms: [ new PlanRoom("Garage", 0, 0, 6, 5, null,
                            [ new PlanFurniture("car", 0, 0, 0, null, null) ]) ],
                Stairs: null),
            new FloorPlan(Level: 0, Name: "Ground", Elevation: 0f, Height: 2.6f,
                Rooms: [ new PlanRoom("Living", 0, 0, 8, 6,
                            [ new PlanOpening("south", "door", 0, 1.0f, 2.1f),
                              new PlanOpening("north", "window", 1.5f, 1.4f, 1.2f) ],
                            [ new PlanFurniture("sofa", -2, -1, 0, null, null) ]) ],
                Stairs: null),
            new FloorPlan(Level: 1, Name: "Upper", Elevation: 2.6f, Height: 2.6f,
                Rooms: [ new PlanRoom("Bedroom", 0, 0, 8, 6, null,
                            [ new PlanFurniture("bed", 2, 1, 0, null, null) ]) ],
                Stairs: null),
        ],
        Roof: new RoofInfo("mansard", 2.4f),
        Site: [ new SiteItem("tree", 7, 4, 0), new SiteItem("car", -7, 4, 90) ],
        Width: 8, Depth: 6);

    [Fact]
    public void Reconstruct_BuildsStackedStoreysAtIncreasingElevation()
    {
        var (store, recon) = New();
        recon.Reconstruct(SampleHouse());

        var rooms = store.Current.Rooms;
        rooms.Should().HaveCount(3);
        rooms.Single(r => r.Level == -1).Elevation.Should().BeApproximately(-2.5f, 0.01f);
        rooms.Single(r => r.Level == 0).Elevation.Should().BeApproximately(0f, 0.01f);
        rooms.Single(r => r.Level == 1).Elevation.Should().BeApproximately(2.6f, 0.01f);
        // names are tagged per storey so duplicates across floors stay distinct
        rooms.Select(r => r.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Reconstruct_AddsOpeningsToTheGroundRoom()
    {
        var (store, recon) = New();
        recon.Reconstruct(SampleHouse());

        var ground = store.Current.Rooms.Single(r => r.Level == 0);
        ground.Openings.Should().Contain(o => o.Type == "door");
        ground.Openings.Should().Contain(o => o.Type == "window");
    }

    [Fact]
    public void Reconstruct_PlacesFurnitureOnItsStorey()
    {
        var (store, recon) = New();
        recon.Reconstruct(SampleHouse());

        var bed = store.Current.Items.Single(i => i.Kind == "bed");
        bed.Level.Should().Be(1);
        (bed.Position.Y - bed.Size.Y / 2f).Should().BeApproximately(2.6f, 0.02f); // rests on the upper floor

        var sofa = store.Current.Items.Single(i => i.Kind == "sofa");
        sofa.Level.Should().Be(0);
    }

    [Fact]
    public void Reconstruct_PlacesSiteItemsOnTheGround()
    {
        var (store, recon) = New();
        recon.Reconstruct(SampleHouse());

        // the outdoor tree is outside every room footprint → ground level, no room
        var tree = store.Current.Items.Single(i => i.Kind == "tree");
        tree.Level.Should().Be(0);
        tree.RoomId.Should().BeNull();
        (tree.Position.Y - tree.Size.Y / 2f).Should().BeApproximately(0f, 0.02f);
    }

    [Fact]
    public void Reconstruct_SetsTheBuildingRoof()
    {
        var (store, recon) = New();
        recon.Reconstruct(SampleHouse());

        store.Current.Roof.Should().NotBeNull();
        store.Current.Roof!.Style.Should().Be("mansard");
        store.Current.Roof.BaseY.Should().BeApproximately(5.2f, 0.05f); // top of the upper storey (2.6 + 2.6)
    }

    [Fact]
    public void Reconstruct_CreatesStairsOncePerFloor_NotPerRoom()
    {
        var (store, recon) = New();
        var plan = new BuildingPlan(
            Floors:
            [
                new FloorPlan(0, "Ground", 0f, 2.6f,
                    Rooms:
                    [
                        new PlanRoom("Hall", -3, 0, 4, 6, null, null),
                        new PlanRoom("Living", 2, 0, 4, 6, null, null),
                        new PlanRoom("Kitchen", 6, 0, 4, 6, null, null),
                    ],
                    Stairs: [ new PlanStair(-3, 0, 0) ]),
            ],
            Roof: null, Site: null, Width: 12, Depth: 6);

        recon.Reconstruct(plan);

        // 3 rooms, but the single stair must yield exactly ONE staircase (was duplicated per room before).
        store.Current.Items.Count(i => i.Kind == "staircase").Should().Be(1);
    }

    [Fact]
    public void Reconstruct_ClampsFurnitureOutsideItsRoom_BackInside()
    {
        var (store, recon) = New();
        var plan = new BuildingPlan(
            Floors:
            [
                new FloorPlan(0, "Ground", 0f, 2.6f,
                    Rooms: [ new PlanRoom("Bedroom", 0, 0, 4, 4, null,
                                [ new PlanFurniture("bed", 9, 9, 0, null, null) ]) ], // way outside
                    Stairs: null),
            ],
            Roof: null, Site: null, Width: 4, Depth: 4);

        recon.Reconstruct(plan);

        var bed = store.Current.Items.Single(i => i.Kind == "bed");
        bed.RoomId.Should().NotBeNull();              // landed in a room, not orphaned
        bed.Level.Should().Be(0);
        MathF.Abs(bed.Position.X).Should().BeLessThan(2f); // clamped back within the 4×4 room
        MathF.Abs(bed.Position.Z).Should().BeLessThan(2f);
    }

    [Fact]
    public void Reconstruct_BuildsAClearFrontEntrance()
    {
        var (store, recon) = New();
        var plan = new BuildingPlan(
            Floors:
            [
                new FloorPlan(0, "Ground", 0f, 4.1f,
                    Rooms: [ new PlanRoom("Windfang", 0, -4, 3, 2, null, null),   // entrance hall on the front
                             new PlanRoom("Wohnen", 0, 2, 8, 6, null, null) ],
                    Stairs: null, EntranceRoom: "Windfang", EntranceWall: "south"),
            ],
            Roof: null, Site: null, Width: 8, Depth: 10);

        recon.Reconstruct(plan);

        var windfang = store.Current.Rooms.Single(r => r.Name.Contains("Windfang"));
        windfang.Openings.Should().Contain(o => o.Type == "door" && o.Wall == "south"); // clear front door
        store.Current.Items.Should().Contain(i => i.Kind == "steps");                    // a stoop with steps
    }

    [Fact]
    public void Reconstruct_DoesNotAddASecondEntranceDoorWhenThePlanAlreadyHasOne()
    {
        var (store, recon) = New();
        var plan = new BuildingPlan(
            Floors:
            [
                new FloorPlan(0, "Ground", 0f, 4.1f,
                    Rooms: [ new PlanRoom("Windfang", 0, -4, 3, 2,
                                Openings: [ new PlanOpening("south", "door", 0, 1.0f, 2.1f) ], Furniture: null),
                             new PlanRoom("Wohnen", 0, 2, 8, 6, null, null) ],
                    Stairs: null, EntranceRoom: "Windfang", EntranceWall: "south"),
            ],
            Roof: null, Site: null, Width: 8, Depth: 10);

        recon.Reconstruct(plan);

        var windfang = store.Current.Rooms.Single(r => r.Name.Contains("Windfang"));
        windfang.Openings.Count(o => o.Type == "door" && o.Wall == "south").Should().Be(1); // exactly ONE, not two
        store.Current.Items.Should().Contain(i => i.Kind == "steps");
    }

    [Fact]
    public void Reconstruct_AtticFloorIsFlaggedInRoof()
    {
        var (store, recon) = New();
        var plan = new BuildingPlan(
            Floors:
            [
                new FloorPlan(0, "Ground", 0f, 4.1f, Rooms: [ new PlanRoom("Living", 0, 0, 8, 6, null, null) ], Stairs: null),
                new FloorPlan(1, "Attic", 4.1f, 2.8f, Rooms: [ new PlanRoom("Bedroom", 0, 0, 8, 6, null, null) ], Stairs: null,
                    InRoof: true),
            ],
            Roof: new RoofInfo("mansard", 5.75f, 2, Eave: 4.1f, Ridge: 9.85f, Break: 7.15f),
            Site: null, Width: 8, Depth: 6);

        recon.Reconstruct(plan);

        store.Current.Rooms.Single(r => r.Level == 1).InRoof.Should().BeTrue();
        store.Current.Rooms.Single(r => r.Level == 0).InRoof.Should().BeFalse();
        store.Current.Rooms.Single(r => r.Level == 1).Elevation.Should().BeApproximately(4.1f, 0.01f);
    }

    [Fact]
    public void Reconstruct_RoofUsesEaveRidgeBreakFromTheElevation()
    {
        var (store, recon) = New();
        var plan = new BuildingPlan(
            Floors: [ new FloorPlan(0, "Ground", 0f, 4.1f, Rooms: [ new PlanRoom("Living", 0, 0, 8, 6, null, null) ], Stairs: null) ],
            Roof: new RoofInfo("mansard", 5.75f, 2, Eave: 4.1f, Ridge: 9.85f, Break: 7.15f),
            Site: null, Width: 8, Depth: 6);

        recon.Reconstruct(plan);

        var roof = store.Current.Roof!;
        roof.BaseY.Should().BeApproximately(4.1f, 0.01f);          // eave
        roof.Height.Should().BeApproximately(5.75f, 0.05f);        // ridge − eave
        roof.BreakY.Should().BeApproximately(7.15f, 0.01f);
        roof.Dormers.Should().Be(2);
    }
}
