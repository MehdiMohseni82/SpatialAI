using FluentAssertions;
using SpatialAI.Api.Blueprint;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class BlueprintFidelityTests
{
    private static PlanRoom Room(string name, float cx, float cz, float w, float d)
        => new(name, cx, cz, w, d, null, null);

    private static float Overlap(PlanRoom a, PlanRoom b)
    {
        float ox = MathF.Min(a.CenterX + a.Width / 2, b.CenterX + b.Width / 2) - MathF.Max(a.CenterX - a.Width / 2, b.CenterX - b.Width / 2);
        float oz = MathF.Min(a.CenterZ + a.Depth / 2, b.CenterZ + b.Depth / 2) - MathF.Max(a.CenterZ - a.Depth / 2, b.CenterZ - b.Depth / 2);
        return (ox > 0 && oz > 0) ? ox * oz : 0f;
    }

    // ── De-overlap ────────────────────────────────────────────────────────────
    [Fact]
    public void DeOverlap_RemovesOverlapsBetweenRooms()
    {
        // two 4×4 rooms overlapping by 2m along X
        var rooms = new List<PlanRoom> { Room("A", 0, 0, 4, 4), Room("B", 2, 0, 4, 4) };
        var result = BlueprintService.DeOverlap(rooms);

        for (var i = 0; i < result.Count; i++)
            for (var j = i + 1; j < result.Count; j++)
                Overlap(result[i], result[j]).Should().BeLessThan(0.02f);
        result.Should().HaveCount(2);
        result.All(r => r.Width >= 0.6f && r.Depth >= 0.6f).Should().BeTrue();
    }

    [Fact]
    public void DeOverlap_LeavesNonOverlappingRoomsUntouched()
    {
        var rooms = new List<PlanRoom> { Room("A", -3, 0, 4, 4), Room("B", 3, 0, 4, 4) }; // already tiling
        var result = BlueprintService.DeOverlap(rooms);
        result[0].Width.Should().BeApproximately(4f, 0.001f);
        result[1].Width.Should().BeApproximately(4f, 0.001f);
    }

    [Fact]
    public void DeOverlap_ResolvesAGridOfOverlaps()
    {
        // four rooms all overlapping near the origin
        var rooms = new List<PlanRoom>
        {
            Room("A", -1, -1, 4, 4), Room("B", 1, -1, 4, 4),
            Room("C", -1, 1, 4, 4),  Room("D", 1, 1, 4, 4),
        };
        var result = BlueprintService.DeOverlap(rooms);
        var total = 0f;
        for (var i = 0; i < result.Count; i++)
            for (var j = i + 1; j < result.Count; j++)
                total += Overlap(result[i], result[j]);
        total.Should().BeLessThan(0.5f); // essentially tiled
    }

    private static (float w, float d, float cx, float cz) FloorBox(FloorPlan f)
    {
        var rs = f.Rooms!;
        float minX = rs.Min(r => r.CenterX - r.Width / 2), maxX = rs.Max(r => r.CenterX + r.Width / 2);
        float minZ = rs.Min(r => r.CenterZ - r.Depth / 2), maxZ = rs.Max(r => r.CenterZ + r.Depth / 2);
        return (maxX - minX, maxZ - minZ, (minX + maxX) / 2, (minZ + maxZ) / 2);
    }

    // ── Lock floors to one shared footprint (fixes "ground smaller than first") ──
    [Fact]
    public void LockFloorToFootprint_MakesEveryFloorTheSameSizeAndCentered()
    {
        // three floors with wildly different extracted footprints (like the real bug)
        var ground = new FloorPlan(0, "Ground", 0, 2.6f,
            Rooms: [Room("A", -3, 0, 6, 5.7f), Room("B", 3, 0, 6, 5.7f)], Stairs: null);
        var first = new FloorPlan(1, "Upper", 0, 2.6f,
            Rooms: [Room("C", 0, -4, 5.4f, 8), Room("D", 0, 4, 5.4f, 8)], Stairs: null); // narrow & deep
        var basement = new FloorPlan(-1, "Basement", 0, 2.5f,
            Rooms: [Room("E", 1, 1, 7.1f, 6.3f)], Stairs: null);

        var locked = new[] { ground, first, basement }
            .Select(f => BlueprintService.LockFloorToFootprint(f, 15.6f, 12.7f)).ToList();

        foreach (var f in locked)
        {
            var (w, d, cx, cz) = FloorBox(f);
            w.Should().BeApproximately(15.6f, 0.2f);
            d.Should().BeApproximately(12.7f, 0.2f);
            cx.Should().BeApproximately(0f, 0.2f);
            cz.Should().BeApproximately(0f, 0.2f);
        }
    }

    // ── Tiling: sparse rooms fill the footprint (no missing building under the roof) ──
    [Fact]
    public void TileFloor_FillsGapsToCoverTheFootprint()
    {
        // three small rooms covering ~30% of a 12×10 footprint, with big gaps
        var rooms = new List<PlanRoom>
        {
            Room("A", -4, -3, 3, 3),
            Room("B", 4, -3, 3, 3),
            Room("C", 0, 3, 3, 3),
        };
        var tiled = BlueprintService.TileFloor(rooms, 12f, 10f);

        // covered area ≈ footprint (rooms now tile it)
        float covered = tiled.Sum(r => r.Width * r.Depth);
        covered.Should().BeGreaterThan(12f * 10f * 0.9f);

        // still within the footprint, and not overlapping
        foreach (var r in tiled)
        {
            (r.CenterX - r.Width / 2).Should().BeGreaterThanOrEqualTo(-6.01f);
            (r.CenterX + r.Width / 2).Should().BeLessThanOrEqualTo(6.01f);
        }
        float overlap = 0f;
        for (var i = 0; i < tiled.Count; i++)
            for (var j = i + 1; j < tiled.Count; j++)
                overlap += Overlap(tiled[i], tiled[j]);
        overlap.Should().BeLessThan(0.5f);
    }

    // ── Site items pushed FULLY outside (clearing their own size) ────────────────
    [Fact]
    public void PushSiteOutside_ClearsTheItemsOwnFootprint()
    {
        var tree = new SiteItem("tree", 2, 1, 0); // inside a 15.6×12.7 footprint
        var result = BlueprintService.PushSiteOutside([tree], 15.6f, 12.7f).Single();

        // the tree's canopy (catalog size) must fully clear the wall, not just its center
        var sz = SpatialAI.Core.Furniture.FurnitureFactory.Build("tree", null, null, null, null)!.Size;
        float hw = 15.6f / 2, hd = 12.7f / 2;
        bool clears = MathF.Abs(result.X) - sz.X / 2 >= hw - 0.01f || MathF.Abs(result.Z) - sz.Z / 2 >= hd - 0.01f;
        clears.Should().BeTrue();
    }

    // ── Furniture separated using REAL catalog sizes ─────────────────────────────
    [Fact]
    public void SpreadFurniture_SeparatesBigItemsByTheirCatalogSize()
    {
        // a bed (no explicit size → catalog 1.6×2.05) and a bookshelf piled at the same spot
        var room = new PlanRoom("Bedroom", 0, 0, 8, 8, null,
            [ new PlanFurniture("bed", 0, 0, 0, null, null), new PlanFurniture("bookshelf", 0.2f, 0.1f, 0, null, null) ]);
        var spread = BlueprintService.SpreadFurniture(room)!;

        var (aw, ad) = BlueprintService.FurnitureFootprint(spread[0]);
        var (bw, bd) = BlueprintService.FurnitureFootprint(spread[1]);
        float ox = (aw / 2 + bw / 2) - MathF.Abs(spread[0].X - spread[1].X);
        float oz = (ad / 2 + bd / 2) - MathF.Abs(spread[0].Z - spread[1].Z);
        (ox <= 0.05f || oz <= 0.05f).Should().BeTrue(); // no longer overlapping
    }

    // ── Furniture spread within a room ──────────────────────────────────────────
    [Fact]
    public void SpreadFurniture_SeparatesPiledItems()
    {
        var room = new PlanRoom("Bedroom", 0, 0, 6, 6, null,
            [ new("bed", 0, 0, 0, 1.6f, 2.0f), new("nightstand", 0.1f, 0.1f, 0, 0.5f, 0.4f) ]);
        var spread = BlueprintService.SpreadFurniture(room)!;

        var a = spread[0]; var b = spread[1];
        float ox = (1.6f / 2 + 0.5f / 2) - MathF.Abs(a.X - b.X);
        float oz = (2.0f / 2 + 0.4f / 2) - MathF.Abs(a.Z - b.Z);
        (ox <= 0.05f || oz <= 0.05f).Should().BeTrue(); // no longer overlapping on at least one axis
    }

    // ── Dormers ───────────────────────────────────────────────────────────────
    [Fact]
    public void Reconstruct_CarriesDormerCountOntoTheRoof()
    {
        var store = new SceneStore();
        var recon = new BuildingReconstructor(new SceneTools(store));
        var plan = new BuildingPlan(
            Floors: [ new FloorPlan(0, "Ground", 0f, 2.6f,
                        Rooms: [ Room("Living", 0, 0, 8, 6) ], Stairs: null) ],
            Roof: new RoofInfo("mansard", 2.4f, Dormers: 3),
            Site: null, Width: 8, Depth: 6);

        recon.Reconstruct(plan);

        store.Current.Roof.Should().NotBeNull();
        store.Current.Roof!.Dormers.Should().Be(3);
    }

    [Fact]
    public void SetBuildingRoof_StoresDormerCount()
    {
        var store = new SceneStore();
        var tools = new SceneTools(store);
        tools.CreateRoom("Room", 8, 6);
        tools.SetBuildingRoof("mansard", null, 4);
        store.Current.Roof!.Dormers.Should().Be(4);
    }
}
