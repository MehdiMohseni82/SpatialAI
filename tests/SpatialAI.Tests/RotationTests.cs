using FluentAssertions;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class RotationTests
{
    private static (SceneStore store, SceneTools tools) New()
    {
        var store = new SceneStore();
        return (store, new SceneTools(store));
    }

    [Fact]
    public void CreateItem_AppliesRotationY()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 5, 5);
        tools.CreateItem("Chair", "chair", rotationY: 90f);

        store.Current.Items.Single().RotationY.Should().BeApproximately(90f, 0.001f);
    }

    [Theory]
    [InlineData(0f, -1f, 0f)]     // item south of target → faces +Z (0°)
    [InlineData(0f, 1f, 180f)]    // north → faces -Z
    [InlineData(-1f, 0f, 90f)]    // west → faces +X
    [InlineData(1f, 0f, -90f)]    // east → faces -X
    public void FaceToward_PointsAtOrigin(float fromX, float fromZ, float expectedDeg)
    {
        SceneTools.FaceToward(fromX, fromZ, 0f, 0f).Should().BeApproximately(expectedDeg, 0.01f);
    }

    [Fact]
    public void ArrangeAround_PlacesItemsFacingTheTarget()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Desk", "desk", positionX: 0, positionZ: 0);

        tools.ArrangeAround("Desk", "chair", 4);

        var chairs = store.Current.Items.Where(i => i.Kind == "chair").ToList();
        chairs.Should().HaveCount(4);

        var desk = store.Current.Items.Single(i => i.Name == "Desk");
        foreach (var chair in chairs)
        {
            // each chair shares the desk's room and is rotated to face the desk center
            chair.RoomId.Should().Be(desk.RoomId);
            var expected = SceneTools.FaceToward(chair.Position.X, chair.Position.Z, desk.Position.X, desk.Position.Z);
            chair.RotationY.Should().BeApproximately(expected, 0.5f);
            // and is placed away from the desk center (on the ring)
            var dist = MathF.Sqrt(chair.Position.X * chair.Position.X + chair.Position.Z * chair.Position.Z);
            dist.Should().BeGreaterThan(0.3f);
        }
    }

    [Fact]
    public void ArrangeAround_UnknownTarget_ReturnsMessage()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office");
        var msg = tools.ArrangeAround("Nonexistent", "chair", 4);
        msg.Should().Contain("No item");
        store.Current.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1.5f, 0f)]    // chair south of desk → faces +Z (0°)
    [InlineData(1.5f, 180f)]   // chair north of desk → faces -Z (180°)
    public void CreateItem_SeatAutoFacesNearestSurface(float chairZ, float expectedDeg)
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Desk", "desk", positionX: 0, positionZ: 0);

        // no rotationY, no faceItem → a seat should auto-face the nearest desk/table
        tools.CreateItem("Chair", "chair", positionX: 0, positionZ: chairZ);

        var chair = store.Current.Items.Single(i => i.Name == "Chair");
        chair.RotationY.Should().BeApproximately(expectedDeg, 0.01f);
    }

    [Fact]
    public void CreateItem_FaceItem_TurnsToFaceTarget()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Desk", "desk", positionX: 0, positionZ: 0);

        tools.CreateItem("Chair", "chair", positionX: 2, positionZ: 0, faceItem: "Desk");

        var chair = store.Current.Items.Single(i => i.Name == "Chair");
        var expected = SceneTools.FaceToward(2, 0, 0, 0); // ≈ -90 (faces -X toward the desk)
        chair.RotationY.Should().BeApproximately(expected, 0.01f);
    }

    [Fact]
    public void CreateItem_ExplicitRotationY_WinsOverAutoFace()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Desk", "desk", positionX: 0, positionZ: 0);

        tools.CreateItem("Chair", "chair", positionX: 0, positionZ: -1.5f, rotationY: 45f);

        store.Current.Items.Single(i => i.Name == "Chair").RotationY.Should().BeApproximately(45f, 0.01f);
    }

    [Fact]
    public void CreateItem_UnknownFaceItem_DoesNotCrashAndFallsThrough()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Desk", "desk", positionX: 0, positionZ: 0);

        tools.CreateItem("Chair", "chair", positionX: 0, positionZ: -1.5f, faceItem: "Ghost");

        // unknown faceItem → falls through to seat auto-face toward the desk (0°)
        store.Current.Items.Single(i => i.Name == "Chair").RotationY.Should().BeApproximately(0f, 0.01f);
    }

    [Fact]
    public void CreateItem_NonSeat_DoesNotAutoFace()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Desk", "desk", positionX: 0, positionZ: 0);

        tools.CreateItem("Plant", "plant", positionX: 0, positionZ: -1.5f);

        store.Current.Items.Single(i => i.Name == "Plant").RotationY.Should().BeApproximately(0f, 0.01f);
    }

    [Fact]
    public void SeatCreatedBeforeSurface_IsRefacedWhenTheSurfaceIsAdded()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);

        // Chair added FIRST, with no desk yet → it locks to the default facing (0°).
        tools.CreateItem("Chair", "chair", positionX: 1.5f, positionZ: 0);
        store.Current.Items.Single(i => i.Name == "Chair").RotationY.Should().BeApproximately(0f, 0.01f);

        // Adding the desk should re-face the (auto-faced) chair toward it — the core orientation fix.
        tools.CreateItem("Desk", "desk", positionX: 0, positionZ: 0);

        var chair = store.Current.Items.Single(i => i.Name == "Chair");
        var desk = store.Current.Items.Single(i => i.Name == "Desk");
        var expected = SceneTools.FaceToward(chair.Position.X, chair.Position.Z, desk.Position.X, desk.Position.Z);
        chair.RotationY.Should().BeApproximately(expected, 0.5f);
        chair.RotationY.Should().NotBe(0f);   // proves it was actually re-faced
    }

    [Fact]
    public void ItemPlacedOnASurface_InheritsTheSurfaceRotation()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Desk", "desk", positionX: 0, positionZ: 0, rotationY: 45f);

        tools.CreateItem("Monitor", "monitor", onItem: "Desk");

        var desk = store.Current.Items.Single(i => i.Name == "Desk");
        var monitor = store.Current.Items.Single(i => i.Name == "Monitor");
        monitor.RotationY.Should().BeApproximately(desk.RotationY, 0.01f);
    }

    [Fact]
    public void ExplicitlyOrientedSeat_IsNotRefacedByALaterSurface()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 5);
        tools.CreateItem("Chair", "chair", positionX: 1.5f, positionZ: 0, rotationY: 30f);

        tools.CreateItem("Desk", "desk", positionX: 0, positionZ: 0);

        // The chair had an explicit rotationY (AutoFacing == false) → the surface must not turn it.
        store.Current.Items.Single(i => i.Name == "Chair").RotationY.Should().BeApproximately(30f, 0.01f);
    }
}
