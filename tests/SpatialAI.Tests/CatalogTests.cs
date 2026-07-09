using FluentAssertions;
using SpatialAI.Api.Catalog;
using SpatialAI.Core.Furniture;
using SpatialAI.Core.Model;
using Xunit;

namespace SpatialAI.Tests;

public class CatalogTests
{
    private static string TempDb() =>
        Path.Combine(Path.GetTempPath(), "spatialai-catalog-tests", Guid.NewGuid().ToString("N"), "catalog.db");

    [Fact]
    public void Repository_SeedsAndLoadsTheWholeCatalog()
    {
        var db = TempDb();
        try
        {
            var repo = new CatalogRepository(db);
            repo.EnsureSeeded();
            var catalog = repo.Load();

            catalog.AllKinds.Count.Should().Be(CatalogSeed.Entries.Count);
            catalog.IsKnown("forklift").Should().BeTrue();
            catalog.IsKnown("office_chair").Should().BeTrue();
            catalog.Normalize("gaming chair").Should().Be("office_chair"); // alias survived the round-trip
            catalog.TryGet("pallet_rack", out var rack).Should().BeTrue();
            rack.Category.Should().Be("Industrial — warehouse");
        }
        finally { Cleanup(db); }
    }

    [Fact]
    public void Repository_DoesNotReseedWhenAlreadyPopulated()
    {
        var db = TempDb();
        try
        {
            var repo = new CatalogRepository(db);
            repo.EnsureSeeded();
            repo.EnsureSeeded(); // second call must be a no-op (no duplicate rows / no throw)
            new CatalogRepository(db).Load().AllKinds.Count.Should().Be(CatalogSeed.Entries.Count);
        }
        finally { Cleanup(db); }
    }

    [Fact]
    public void FurnitureFactory_UseCatalog_SwapsActiveCatalog()
    {
        var original = FurnitureFactory.Current;
        try
        {
            var custom = new Catalog([
                new CatalogEntry("widget", "Test", 1f, 1f, 1f, Rgba.Gray, ["thing"], "a test kind")
            ]);
            FurnitureFactory.UseCatalog(custom);

            FurnitureFactory.IsKnown("widget").Should().BeTrue();
            FurnitureFactory.Normalize("thing").Should().Be("widget");
            FurnitureFactory.IsKnown("forklift").Should().BeFalse(); // not in the custom catalog
        }
        finally { FurnitureFactory.UseCatalog(original); } // restore the default for other tests
    }

    [Fact]
    public void DescribeKinds_IsGroupedAndCoversCategories()
    {
        var catalog = CatalogSeed.Default();

        var grouped = catalog.DescribeKinds();
        grouped.Should().Contain("Seating");
        grouped.Should().Contain("Industrial — warehouse");
        grouped.Should().Contain("forklift");

        var inline = catalog.DescribeKindsInline();
        inline.Should().Contain("office_chair");
        inline.Should().Contain("box/cylinder/sphere");
    }

    private static void Cleanup(string db)
    {
        try
        {
            var dir = Path.GetDirectoryName(db)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* best-effort temp cleanup */ }
    }
}
