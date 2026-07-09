using FluentAssertions;
using SpatialAI.Core.Furniture;
using Xunit;

namespace SpatialAI.Tests;

public class VariantsTests
{
    public static IEnumerable<object[]> NewKinds =>
    [
        ["dining_chair"], ["office_chair"], ["armchair"],
        ["desk"], ["computer_desk"], ["dining_table"], ["coffee_table"],
        ["loveseat"], ["sectional_sofa"],
        ["single_bed"], ["double_bed"], ["king_bed"], ["bunk_bed"],
        ["chest_of_drawers"], ["sideboard"],
        ["desk_lamp"], ["ceiling_light"], ["chandelier"],
    ];

    [Theory]
    [MemberData(nameof(NewKinds))]
    public void NewKind_BuildsMultiPartModelWithinBounds(string kind)
    {
        FurnitureFactory.IsKnown(kind).Should().BeTrue($"'{kind}' should be a catalog kind, not a box fallback");
        var built = FurnitureFactory.Build(kind, null, null, null, null);

        built.Should().NotBeNull();
        built!.Parts.Count.Should().BeGreaterThanOrEqualTo(2);

        const float eps = 0.001f;
        foreach (var p in built.Parts)
        {
            (MathF.Abs(p.Offset.X) + p.Size.X / 2).Should().BeLessThanOrEqualTo(built.Size.X / 2 + eps);
            (MathF.Abs(p.Offset.Y) + p.Size.Y / 2).Should().BeLessThanOrEqualTo(built.Size.Y / 2 + eps);
            (MathF.Abs(p.Offset.Z) + p.Size.Z / 2).Should().BeLessThanOrEqualTo(built.Size.Z / 2 + eps);
        }
    }

    // Each variant must differ from the generic it used to collapse onto (part count and/or size).
    [Theory]
    [InlineData("dining_table", "table")]
    [InlineData("computer_desk", "desk")]
    [InlineData("desk", "table")]
    [InlineData("coffee_table", "table")]
    [InlineData("office_chair", "chair")]
    [InlineData("armchair", "chair")]
    [InlineData("desk_lamp", "table_lamp")]
    [InlineData("sectional_sofa", "sofa")]
    [InlineData("chest_of_drawers", "wardrobe")]
    public void Variant_IsDistinctFromGeneric(string variant, string generic)
    {
        var a = FurnitureFactory.Build(variant, null, null, null, null)!;
        var b = FurnitureFactory.Build(generic, null, null, null, null)!;

        var sameShape = a.Parts.Count == b.Parts.Count
                        && MathF.Abs(a.Size.X - b.Size.X) < 0.001f
                        && MathF.Abs(a.Size.Y - b.Size.Y) < 0.001f
                        && MathF.Abs(a.Size.Z - b.Size.Z) < 0.001f;
        sameShape.Should().BeFalse($"'{variant}' should look different from '{generic}'");
    }

    [Theory]
    [InlineData("gaming chair", "office_chair")]
    [InlineData("swivel chair", "office_chair")]
    [InlineData("recliner", "armchair")]
    [InlineData("dresser", "chest_of_drawers")]
    [InlineData("credenza", "sideboard")]
    [InlineData("pendant", "ceiling_light")]
    [InlineData("computer desk", "computer_desk")]
    [InlineData("dining table", "dining_table")]
    [InlineData("bunk", "bunk_bed")]
    [InlineData("two seater", "loveseat")]
    public void Alias_ResolvesToVariant(string phrase, string expected)
    {
        FurnitureFactory.Normalize(phrase).Should().Be(expected);
        FurnitureFactory.IsKnown(phrase).Should().BeTrue();
    }

    [Fact]
    public void OldCollapsingAliases_NoLongerCollapse()
    {
        FurnitureFactory.Normalize("dining_table").Should().NotBe("table");
        FurnitureFactory.Normalize("armchair").Should().NotBe("chair");
        FurnitureFactory.Normalize("desk_lamp").Should().NotBe("table_lamp");
        FurnitureFactory.Normalize("loveseat").Should().NotBe("sofa");
    }
}
