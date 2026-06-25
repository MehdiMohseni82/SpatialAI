using FluentAssertions;
using SpatialAI.Core.Furniture;
using SpatialAI.Core.Model;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class CompositeTests
{
    private static (SceneStore store, SceneTools tools) New()
    {
        var store = new SceneStore();
        return (store, new SceneTools(store));
    }

    public static IEnumerable<object[]> Kinds =>
    [
        ["chair"], ["stool"], ["desk"], ["table"], ["coffee_table"], ["sofa"], ["bed"],
        ["nightstand"], ["wardrobe"], ["bookshelf"], ["floor_lamp"], ["table_lamp"],
        ["monitor"], ["tv"], ["rug"], ["plant"],
        // fixtures
        ["kitchen_counter"], ["kitchen_island"], ["sink"], ["stove"], ["fridge"], ["dishwasher"],
        ["toilet"], ["bathtub"], ["basin"], ["shower"], ["radiator"], ["fireplace"], ["ac_unit"],
        ["washing_machine"], ["column"], ["railing"], ["staircase"], ["mirror"], ["bench"]
    ];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Factory_KnownKind_BuildsMultiplePartsWithinBounds(string kind)
    {
        FurnitureFactory.IsKnown(kind).Should().BeTrue();
        var built = FurnitureFactory.Build(kind, null, null, null, null);

        built.Should().NotBeNull();
        built!.Parts.Count.Should().BeGreaterThanOrEqualTo(2);
        built.Size.X.Should().BeGreaterThan(0);
        built.Size.Y.Should().BeGreaterThan(0);
        built.Size.Z.Should().BeGreaterThan(0);

        const float eps = 0.001f;
        foreach (var p in built.Parts)
        {
            (MathF.Abs(p.Offset.X) + p.Size.X / 2).Should().BeLessThanOrEqualTo(built.Size.X / 2 + eps);
            (MathF.Abs(p.Offset.Y) + p.Size.Y / 2).Should().BeLessThanOrEqualTo(built.Size.Y / 2 + eps);
            (MathF.Abs(p.Offset.Z) + p.Size.Z / 2).Should().BeLessThanOrEqualTo(built.Size.Z / 2 + eps);
        }
    }

    [Fact]
    public void Factory_ResolvesAliases()
    {
        FurnitureFactory.Normalize("couch").Should().Be("sofa");
        FurnitureFactory.Normalize("bookcase").Should().Be("bookshelf");
        FurnitureFactory.IsKnown("couch").Should().BeTrue();
        FurnitureFactory.Build("couch", null, null, null, null).Should().NotBeNull();
    }

    [Fact]
    public void Factory_UnknownKind_IsNotKnown()
    {
        FurnitureFactory.IsKnown("spaceship").Should().BeFalse();
        FurnitureFactory.Build("spaceship", null, null, null, null).Should().BeNull();
    }

    [Fact]
    public void CreateItem_Chair_BuildsCompositeRestingOnFloor()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 5, 5);
        tools.CreateItem("Chair", "chair");

        var item = store.Current.Items.Should().ContainSingle().Subject;
        item.Kind.Should().Be("chair");
        item.Parts.Count.Should().BeGreaterThan(1);
        item.Position.Y.Should().BeApproximately(item.Size.Y / 2f, 0.001f);
    }

    [Fact]
    public void CreateItem_PrimitiveBox_IsSinglePart()
    {
        var (store, tools) = New();
        tools.CreateRoom("R");
        tools.CreateItem("Block", "box", 0.5f, 0.5f, 0.5f);

        var item = store.Current.Items.Single();
        item.Parts.Should().ContainSingle();
        item.Kind.Should().BeNull();
    }

    [Fact]
    public void CreateItem_UnknownKind_FallsBackToSingleBox()
    {
        var (store, tools) = New();
        tools.CreateRoom("R");
        tools.CreateItem("Thing", "spaceship");

        var item = store.Current.Items.Single();
        item.Parts.Should().ContainSingle();
        item.Parts[0].Shape.Should().Be(Shape.Box);
    }

    [Fact]
    public void ComposeItem_BuildsBoundingBoxAndRestsOnFloor()
    {
        var (store, tools) = New();
        tools.CreateRoom("R");
        var parts = new List<Part>
        {
            new() { Shape = Shape.Cylinder, Offset = new Vec3(0, 0.03f, 0), Size = new Vec3(0.3f, 0.06f, 0.3f), Color = new Rgba(0.3f, 0.3f, 0.3f) },
            new() { Shape = Shape.Box, Offset = new Vec3(0, 0.5f, 0), Size = new Vec3(0.1f, 1.0f, 0.1f), Color = new Rgba(0.6f, 0.6f, 0.6f) },
        };
        tools.ComposeItem("Pole", parts);

        var item = store.Current.Items.Single();
        item.Parts.Count.Should().Be(2);
        item.Size.Y.Should().BeApproximately(1.0f, 0.05f);     // base bottom 0 .. pole top ~1.0
        item.Position.Y.Should().BeApproximately(item.Size.Y / 2f, 0.001f);
    }

    [Fact]
    public void ScaleItem_ScalesEveryPart()
    {
        var (store, tools) = New();
        tools.CreateRoom("Office", 6, 6);
        tools.CreateItem("Chair", "chair");
        var item = store.Current.Items.Single();
        var part0Before = item.Parts[0].Size.X;
        var sizeBefore = item.Size.Y;

        tools.ScaleItem("Chair", 2f);

        item.Parts[0].Size.X.Should().BeApproximately(part0Before * 2f, 0.0001f);
        item.Size.Y.Should().BeApproximately(sizeBefore * 2f, 0.0001f);
        item.Position.Y.Should().BeApproximately(item.Size.Y / 2f, 0.001f);
    }
}
