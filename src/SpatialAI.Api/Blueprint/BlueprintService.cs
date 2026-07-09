using System.Text.Json;
using SpatialAI.Core.Furniture;

namespace SpatialAI.Api.Blueprint;

/// <summary>
/// Turns a batch of architectural drawing images into a structured <see cref="BuildingPlan"/> using the
/// vision model: (1) classify each image, (2) extract each floor plan to rooms/openings/furniture in meters,
/// (3) read the elevations/section for storey heights, roof style and site items, then (4) merge and stack
/// the storeys at their cumulative elevations. The plan is later turned into a 3D scene by
/// <c>BuildingReconstructor</c> (Phase 2). Extraction is best-effort — the result is meant to be refined.
/// </summary>
public sealed class BlueprintService(VisionClient vision)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    public bool IsConfigured => vision.IsConfigured;

    // ── Orchestration ────────────────────────────────────────────────────────
    public async Task<BuildingPlan> BuildAsync(IReadOnlyList<VisionClient.Image> images, bool atticHint, CancellationToken ct)
    {
        var classes = await ClassifyAsync(images, ct);

        var floorPlanImages = classes
            .Where(c => c.Kind == "floorplan" && c.Index >= 0 && c.Index < images.Count)
            .Select(c => images[c.Index]).ToList();

        // Extract every floor plan (in parallel), pairing each with its image + classification.
        var floorTasks = classes
            .Where(c => c.Kind == "floorplan" && c.Index >= 0 && c.Index < images.Count)
            .Select(c => ExtractFloorAsync(images[c.Index], c, ct))
            .ToList();

        var elevationImages = classes
            .Where(c => c.Kind is "elevation" or "section" && c.Index >= 0 && c.Index < images.Count)
            .Select(c => images[c.Index]).ToList();

        var elevationTask = elevationImages.Count > 0
            ? ExtractElevationsAsync(elevationImages, ct)
            : Task.FromResult<ElevationResult?>(null);

        // Dedicated, focused read of the building's exact external footprint (the biggest, clearest numbers).
        var footprintTask = floorPlanImages.Count > 0
            ? ExtractFootprintAsync(floorPlanImages, ct)
            : Task.FromResult<(float W, float D)>((0, 0));

        await Task.WhenAll(floorTasks.Cast<Task>().Append(elevationTask).Append(footprintTask));

        var floors = floorTasks.Select(t => t.Result).Where(f => f is not null).Cast<FloorPlan>().ToList();
        var elev = elevationTask.Result;
        var (footW, footD) = footprintTask.Result;

        return Merge(floors, elev, footW, footD, atticHint);
    }

    // ── Dedicated footprint read ─────────────────────────────────────────────
    private async Task<(float W, float D)> ExtractFootprintAsync(IReadOnlyList<VisionClient.Image> floorPlans, CancellationToken ct)
    {
        const string sys = """
            You read ONLY the overall EXTERNAL dimensions of a building from its architectural floor plans.
            Every floor plan of the same building shows the SAME overall size. Find the single LARGEST dimension
            number printed along the TOP edge (the total external width) and the single LARGEST printed along the
            SIDE edge (the total external depth) — these are the big overall totals (e.g. 15.62 m and 12.72 m),
            NOT a room or a wall segment, and NOT including terraces/balconies.
            Reply as JSON: {"width":number,"depth":number} in meters. Output ONLY the JSON object.
            """;
        var user = $"Read the building's overall external width and depth from these {floorPlans.Count} floor plan(s).";
        var raw = await vision.CompleteJsonAsync(sys, user, floorPlans, ct);
        var fp = Deserialize<Footprint>(raw);
        return fp is null ? (0, 0) : (fp.Width, fp.Depth);
    }

    // ── 1. Classify ──────────────────────────────────────────────────────────
    private async Task<List<PlanClassification>> ClassifyAsync(IReadOnlyList<VisionClient.Image> images, CancellationToken ct)
    {
        const string sys = """
            You are an architecture-drawing classifier. You will be shown several images in order (index 0,1,2…).
            Classify EACH image. Reply as JSON: {"items":[{"index":int,"kind":"floorplan|elevation|section|other",
            "levelGuess":int,"label":string}, ...]} with one entry per image, in index order.
            - "floorplan": a top-down plan of one storey (rooms seen from above). Set levelGuess: -1 basement
              (Kellergeschoss/Untergeschoss), 0 ground (Erdgeschoss), 1 first upper (Obergeschoss/Dachgeschoss),
              2 second, etc. Read German labels in the title block to decide.
            - "elevation": an exterior side view (Ansicht). levelGuess 0.
            - "section": a vertical cut showing stacked floors (Schnitt). levelGuess 0.
            - "other": anything else. Output ONLY the JSON object.
            """;
        var user = $"Classify these {images.Count} images, indices 0..{images.Count - 1}.";
        var raw = await vision.CompleteJsonAsync(sys, user, images, ct);
        var parsed = Deserialize<ClassifyResult>(raw);
        return parsed?.Items ?? [];
    }

    // ── 2. Extract one floor plan ────────────────────────────────────────────
    private async Task<FloorPlan?> ExtractFloorAsync(VisionClient.Image image, PlanClassification cls, CancellationToken ct)
    {
        var sys = """
            You extract a single architectural FLOOR PLAN into structured JSON, in METERS. Use the printed
            dimension chains (numbers along the edges, e.g. 5.40, 3.00, 1.20) to scale everything. Place the
            origin (0,0) at the CENTER of the building footprint; X increases to the right (east), Z increases
            toward the top of the page (north). EVERY floor of the same building must use this SAME origin so
            storeys stack.

            Reply as JSON:
            {"level":int,"name":string,"height":number,"externalWidth":number,"externalDepth":number,
             "underRoof":bool,"entranceRoom":string,"entranceWall":"north|south|east|west",
             "rooms":[{"name":string,"centerX":number,"centerZ":number,"width":number,"depth":number,
               "openings":[{"wall":"north|south|east|west","type":"window|door","offset":number,"width":number,"height":number}],
               "furniture":[{"kind":string,"x":number,"z":number,"rotationY":number,"width":number,"depth":number}]}],
             "stairs":[{"x":number,"z":number,"rotationY":number}]}

            Rules:
            - USE THE PRINTED NUMBERS, DO NOT EYEBALL: this plan is fully dimensioned. Read each room's printed
              AREA (the "NN.NN m²" label inside it, e.g. 42.77 m², 19.78 m², 17.04 m²) and its width/depth from
              the dimension numbers next to its walls (5.40, 3.00, 1.20, 4.31, 4.94 …). A room's width×depth must
              be consistent with its printed m² area. Read OPENING widths from their printed numbers (1.20, 1.00,
              2.45 …). Do NOT estimate sizes from visual proportions — the numbers are exact.
            - EXTERNAL DIMS: report `externalWidth` and `externalDepth` = the building's overall external width
              and depth in meters, read from the single LARGEST (outermost) dimension-chain totals printed along
              the top edge and the side edge (e.g. 15.62 m wide × 12.72 m deep). These are the most reliable
              numbers on the sheet — read them carefully.
            - SCALE: scale every room so the union of room rectangles fills that external footprint — do not
              undershoot.
            - TILE, DON'T OVERLAP: rooms partition the floor plate edge-to-edge — adjacent rooms share a wall and
              their rectangles must NOT overlap. The sum of room areas should ≈ the external footprint area. Snap
              shared walls so there are no gaps or overlaps between neighboring rooms.
            - Read the German room labels (Wohnen/Essen/Kochen=living, Schlafen=bedroom, Bad=bath, Küche=kitchen,
              Master, Flur/Diele=hall, Loggia, Garage, Keller, Hobby) and the m² areas to size rooms.
            - ENTRANCE (ground floor only): find the MAIN front-door entry — the door from outside into the
              entrance hall (Windfang / Eingang / Diele / Vorzimmer). Report `entranceRoom` = that room's name
              and `entranceWall` = the exterior wall it sits on. Leave both empty on other floors.
            - ATTIC / UNDER-ROOF: set `underRoof` = true when this floor sits under a SLOPED roof (a
              Dachgeschoss/DG or top storey). Tell-tale signs: diagonal dashed lines crossing the rooms
              (the roof slope), printed height-contour marks like "1.0 m Linie / 1.5 m / 2.0 m / 2.3 m"
              (where the sloping ceiling reaches that height), knee-wall hatching along the outer edges, or
              a "Dachgeschoss/DG/Spitzboden" label. Such a floor is OPEN TO THE ROOF — it has NO flat
              ceiling. Set false for a normal full-height storey (Erdgeschoss/Obergeschoss with straight walls).
            - "height" = storey wall height in meters (typical 2.5–2.8; use 2.5 if unreadable).
            - furniture.kind MUST be one of: bed, sofa, chair, table, desk, wardrobe, bookshelf, nightstand,
              kitchen_counter, kitchen_island, sink, stove, fridge, dishwasher, toilet, bathtub, basin, shower,
              tv, plant, staircase, car. Map symbols to the nearest kind; rotationY 0 faces +Z (north),
              90 faces +X (east), 180 south, -90 west. Omit furniture you can't identify.
            - Coordinates are room/furniture CENTERS in meters from the origin. Output ONLY the JSON object.
            """;
        var user = $"Extract this floor plan. It was classified as '{cls.Label}' (level {cls.LevelGuess}). Use level {cls.LevelGuess}.";
        var raw = await vision.CompleteJsonAsync(sys, user, [image], ct);
        var floor = Deserialize<FloorPlan>(raw);
        if (floor is null) return null;
        // Trust the classifier's level if the model didn't echo it sensibly.
        return floor with { Level = floor.Level == 0 && cls.LevelGuess != 0 ? cls.LevelGuess : floor.Level };
    }

    // ── 3. Extract elevations + section ──────────────────────────────────────
    private async Task<ElevationResult?> ExtractElevationsAsync(IReadOnlyList<VisionClient.Image> images, CancellationToken ct)
    {
        const string sys = """
            You read architectural ELEVATION and SECTION drawings (exterior side views + a vertical cut) and
            report the building's vertical info + exterior site. Reply as JSON:
            {"roofStyle":"flat|gable|hip|mansard","roofHeight":number,"dormers":int,
             "grade":number,"eave":number,"ridge":number,"mansardBreak":number,"basementFloor":number,
             "groundHeight":number,"upperHeight":number,"basementHeight":number,
             "site":[{"kind":string,"x":number,"z":number,"rotationY":number}]}
            - HEIGHT MARKERS (read the printed ▽ level numbers like ±0.00, +4.10, +7.15, +9.85, −2.05 — exact):
              `grade` = the ±0.00 ground line; `eave` = the height where the main masonry wall ends and the roof
              starts (the lower roof edge, e.g. +4.10); `mansardBreak` = the kink height where the steep lower
              roof slope meets the shallow upper slope (e.g. +7.15); `ridge` = the very top of the roof (e.g.
              +9.85); `basementFloor` = the lowest marker (e.g. −2.05). Use 0 for any you cannot read.
            - roofStyle: pick the closest. A two-slope-per-side roof (steep lower, shallow upper) is "mansard";
              a single ridge with two slopes is "gable"; sloped on all four sides is "hip". This house's tall
              roof with a steep lower slope + dormers + a shallow top is "mansard".
            - roofHeight = roof rise above the eave (m) = ridge − eave. *Height fields = storey heights (m).
            - dormers = how many dormers (Gauben — windowed boxes projecting from the roof slope) are on ONE
              main slope (front); 0 if none.
            - site.kind one of: tree, bush, hedge, car, terrace, garage, fence, gate, lawn. x/z in meters from
              the building center (X right/east, Z toward top/north); place trees/cars where shown. Output ONLY JSON.
            """;
        var user = $"Read these {images.Count} elevation/section images for roof, storey heights and site items.";
        var raw = await vision.CompleteJsonAsync(sys, user, images, ct);
        return Deserialize<ElevationResult>(raw);
    }

    // ── 4. Merge + stack storeys ─────────────────────────────────────────────
    private static BuildingPlan Merge(List<FloorPlan> floors, ElevationResult? elev, float footW, float footD, bool atticHint = false)
    {
        var ordered = floors.OrderBy(f => f.Level).ToList();

        // Footprint target. The elevation's facade is the building's WIDTH (the longer side, e.g. 15.62), so
        // orient landscape: width = larger dim, depth = smaller. Floors whose layout is portrait get rotated 90°
        // (not stretched) so they fill it without distortion.
        var floorWidths = ordered.Select(f => { var (a, _, c, _) = Bounds(f.Rooms ?? []); return c - a; }).Where(v => v > 0.5f).ToList();
        var floorDepths = ordered.Select(f => { var (_, a, _, c) = Bounds(f.Rooms ?? []); return c - a; }).Where(v => v > 0.5f).ToList();
        float extW = footW is >= 4f and <= 40f ? footW : 0;
        float extD = footD is >= 4f and <= 40f ? footD : 0;
        float a0 = extW > 0 ? extW : (Median(floorWidths) is var mw and > 0 ? mw : 10f);
        float b0 = extD > 0 ? extD : (Median(floorDepths) is var md and > 0 ? md : 10f);
        float targetW = MathF.Max(a0, b0), targetD = MathF.Min(a0, b0); // landscape
        // Decide the 90° rotation ONCE for the whole building (so floors AND site stay aligned): rotate when the
        // floors' layout is portrait but the facade/footprint is landscape.
        bool rotate90 = Median(floorDepths) > Median(floorWidths) * 1.05f && targetW >= targetD;
        var oriented = rotate90 ? ordered.Select(RotateFloor90).ToList() : ordered;
        var locked = oriented.Select(f => LockFloorToFootprint(f, targetW, targetD)).ToList();

        var (minX, minZ, maxX, maxZ) = Bounds(locked.SelectMany(f => f.Rooms ?? []));
        float span = MathF.Min(maxX - minX, maxZ - minZ);

        // ── Vertical profile from the elevation's ▽ markers (with sane fallbacks) ──
        bool Sane(float v, float lo, float hi) => v >= lo && v <= hi;
        var aboveGrade = locked.Select(f => f.Level).Where(l => l >= 0).Distinct().OrderBy(l => l).ToList();
        var basements = locked.Select(f => f.Level).Where(l => l < 0).Distinct().OrderByDescending(l => l).ToList();
        string roofStyle = elev is null || string.IsNullOrWhiteSpace(elev.RoofStyle) ? "gable" : elev.RoofStyle.ToLowerInvariant();
        bool pitched = roofStyle is "mansard" or "gable" or "hip";

        // The top above-grade storey under a pitched roof is the ATTIC. Normally that needs ≥2 storeys, but the
        // extractor can also flag a floor as under a sloped roof (a lone Dachgeschoss plan) — honour that too.
        // Attic when: the user ticked the "attic" box, the extractor flagged a roofed floor, or (top of a
        // multi-storey stack) the classic ≥2-storey rule below.
        bool extractedAttic = atticHint || locked.Any(f => f.UnderRoof && f.Level >= 0);
        int? atticLevel = pitched && aboveGrade.Count > 0 && (aboveGrade.Count >= 2 || extractedAttic)
            ? aboveGrade[^1] : null;
        var masonryAbove = aboveGrade.Where(l => l != atticLevel).ToList();
        int nMason = Math.Max(1, masonryAbove.Count);
        // A lone attic (no full storey below it) sits ON THE GROUND with a low knee wall — the roof springs
        // just above the floor rather than atop a full storey.
        bool atticOnGround = atticLevel is not null && masonryAbove.Count == 0;

        float eave = atticOnGround ? 0.8f
            : elev is not null && Sane(elev.Eave, 2f, 12f) ? elev.Eave : 4.0f;
        float ridge = elev is not null && Sane(elev.Ridge, eave + 0.5f, 20f) ? elev.Ridge : eave + span * 0.45f;
        float brk = elev is not null && Sane(elev.MansardBreak, eave + 0.3f, ridge - 0.3f) ? elev.MansardBreak : eave + (ridge - eave) * 0.6f;
        float basementFloor = elev is not null && Sane(elev.BasementFloor, -8f, -0.5f) ? elev.BasementFloor : -2.4f;

        (float elevation, float height, bool inRoof) Profile(int level)
        {
            if (level == atticLevel)
            {
                float floorE = atticOnGround ? 0f : eave;   // lone attic on the ground vs. attic atop masonry
                return (floorE, MathF.Max(0.6f, ridge - floorE), true);
            }
            if (level >= 0)
            {
                float h = eave / nMason;
                int idx = masonryAbove.IndexOf(level); if (idx < 0) idx = 0;
                return (idx * h, h, false);
            }
            // basements: stack down from grade; deepest reaches basementFloor
            int bidx = basements.IndexOf(level); if (bidx < 0) bidx = 0;
            float bh = -basementFloor / MathF.Max(1, basements.Count);
            return (-(bidx + 1) * bh, bh, false);
        }

        var stacked = locked.Select(f =>
        {
            var (e, h, inRoof) = Profile(f.Level);
            return f with { Elevation = e, Height = h, InRoof = inRoof };
        }).ToList();

        RoofInfo? roof = elev is null && !pitched ? null
            : new RoofInfo(roofStyle, MathF.Max(0.8f, ridge - eave), Math.Clamp(elev?.Dormers ?? 0, 0, 8), eave, ridge, brk);

        // Keep site objects (trees, car…) OUTSIDE the building footprint — rotated with the building.
        var siteRaw = elev?.Site ?? [];
        var siteRot = rotate90 ? siteRaw.Select(s => s with { X = s.Z, Z = s.X }).ToList() : siteRaw;
        var site = PushSiteOutside(siteRot, maxX - minX, maxZ - minZ);

        return new BuildingPlan(stacked, roof, site, maxX - minX, maxZ - minZ);
    }

    /// <summary>Rotates a whole floor 90° (swap X↔Z of rooms/openings/furniture/stairs), preserving room
    /// proportions — used to align a portrait-extracted layout with the landscape facade.</summary>
    private static FloorPlan RotateFloor90(FloorPlan f)
    {
        var rooms = f.Rooms ?? [];
        if (rooms.Count == 0) return f;
        static string SwapWall(string? w) => (w?.ToLowerInvariant()) switch { "north" => "east", "east" => "north", "south" => "west", "west" => "south", _ => w ?? "north" };
        return f with
        {
            Rooms = rooms.Select(r => r with
            {
                CenterX = r.CenterZ, CenterZ = r.CenterX, Width = r.Depth, Depth = r.Width,
                Openings = r.Openings?.Select(o => o with { Wall = SwapWall(o.Wall) }).ToList(),
                Furniture = r.Furniture?.Select(fn => fn with { X = fn.Z, Z = fn.X }).ToList()
            }).ToList(),
            Stairs = f.Stairs?.Select(st => st with { X = st.Z, Z = st.X }).ToList(),
            EntranceWall = SwapWall(f.EntranceWall)
        };
    }

    /// <summary>Moves any site item that would overlap the building out past the nearest edge — far enough that
    /// the item's own footprint (e.g. a tree's canopy) fully clears the wall.</summary>
    public static List<SiteItem> PushSiteOutside(List<SiteItem> site, float footW, float footD)
    {
        if (site.Count == 0 || footW < 0.5f || footD < 0.5f) return site;
        float hw = footW / 2f, hd = footD / 2f, gap = 0.8f;
        return site.Select(s =>
        {
            var sz = FurnitureFactory.Build(s.Kind ?? "box", null, null, null, null)?.Size;
            float halfX = (sz?.X ?? 1.0f) / 2f, halfZ = (sz?.Z ?? 1.0f) / 2f;
            // overlaps the building if the item's box intersects the footprint box
            bool overlaps = MathF.Abs(s.X) < hw + halfX && MathF.Abs(s.Z) < hd + halfZ;
            if (!overlaps) return s;
            float dx = (hw + halfX) - MathF.Abs(s.X), dz = (hd + halfZ) - MathF.Abs(s.Z);
            if (dx <= dz)
            {
                float sign = s.X >= 0 ? 1f : -1f;
                return s with { X = sign * (hw + halfX + gap) };
            }
            float signz = s.Z >= 0 ? 1f : -1f;
            return s with { Z = signz * (hd + halfZ + gap) };
        }).ToList();
    }

    // ── Footprint scaling + de-overlap (deterministic, unit-tested) ──────────
    private static (float minX, float minZ, float maxX, float maxZ) Bounds(IEnumerable<PlanRoom> rooms)
    {
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var r in rooms)
        {
            minX = MathF.Min(minX, r.CenterX - r.Width / 2); maxX = MathF.Max(maxX, r.CenterX + r.Width / 2);
            minZ = MathF.Min(minZ, r.CenterZ - r.Depth / 2); maxZ = MathF.Max(maxZ, r.CenterZ + r.Depth / 2);
        }
        return minX > maxX ? (0, 0, 0, 0) : (minX, minZ, maxX, maxZ);
    }

    /// <summary>Median of positive readings (0 if none).</summary>
    public static float Median(IReadOnlyList<float> values)
    {
        var xs = values.Where(v => v > 0.01f).OrderBy(v => v).ToList();
        if (xs.Count == 0) return 0f;
        return xs.Count % 2 == 1 ? xs[xs.Count / 2] : (xs[xs.Count / 2 - 1] + xs[xs.Count / 2]) / 2f;
    }

    /// <summary>
    /// Maps a whole floor (rooms + their openings/furniture + stairs) so the room bounding box becomes exactly
    /// <paramref name="footW"/>×<paramref name="footD"/> centered at the origin (recenter + per-axis scale),
    /// then de-overlaps rooms and spreads furniture. Applied to every storey with the same footprint → all
    /// floors are consistent and identically sized.
    /// </summary>
    public static FloorPlan LockFloorToFootprint(FloorPlan f, float footW, float footD)
    {
        var rooms = (f.Rooms ?? []).ToList();
        if (rooms.Count == 0 || footW < 0.5f || footD < 0.5f)
            return f with { Rooms = DeOverlap(rooms) };

        var (minX, minZ, maxX, maxZ) = Bounds(rooms);
        float uw = MathF.Max(0.5f, maxX - minX), ud = MathF.Max(0.5f, maxZ - minZ);
        float ucx = (minX + maxX) / 2f, ucz = (minZ + maxZ) / 2f;
        float sx = footW / uw, sz = footD / ud;
        float TX(float x) => (x - ucx) * sx;
        float TZ(float z) => (z - ucz) * sz;
        static bool AlongX(string? wall) => wall is "north" or "south";

        var mapped = rooms.Select(r => r with
        {
            CenterX = TX(r.CenterX), Width = r.Width * sx,
            CenterZ = TZ(r.CenterZ), Depth = r.Depth * sz,
            Openings = r.Openings?.Select(o => o with
            {
                Offset = o.Offset * (AlongX(o.Wall?.ToLowerInvariant()) ? sx : sz),
                Width = o.Width * (AlongX(o.Wall?.ToLowerInvariant()) ? sx : sz)
            }).ToList(),
            Furniture = r.Furniture?.Select(fn => fn with
            {
                X = TX(fn.X), Z = TZ(fn.Z),
                Width = fn.Width.HasValue ? fn.Width * sx : fn.Width,
                Depth = fn.Depth.HasValue ? fn.Depth * sz : fn.Depth
            }).ToList()
        }).ToList();

        // Tile the floor: grow rooms to share walls and reach the footprint edges so there are no GAPS
        // (extraction leaves rooms covering only ~half the plate, which made the roof float over emptiness).
        var tiled = TileFloor(mapped, footW, footD);
        var spread = tiled.Select(r => r with { Furniture = SpreadFurniture(r) }).ToList();
        var stairs = f.Stairs?.Select(st => st with { X = TX(st.X), Z = TZ(st.Z) }).ToList();
        return f with { Rooms = spread, Stairs = stairs };
    }

    /// <summary>
    /// Turns a set of (possibly sparse / overlapping) room rectangles into a clean PARTITION of the
    /// footprint: adjacent rooms snap to a shared wall (closing gaps AND removing overlaps), and rooms on the
    /// perimeter extend to the footprint edge. A few relaxation passes converge. Result: rooms tile the plate
    /// with ~no gaps and ~no overlaps — so the building has no missing parts under the roof.
    /// </summary>
    public static List<PlanRoom> TileFloor(List<PlanRoom> rooms, float footW, float footD)
    {
        if (rooms.Count == 0) return rooms;
        const float band = 0.4f;
        float hw = footW / 2f, hd = footD / 2f;
        int n = rooms.Count;
        var x0 = new float[n]; var x1 = new float[n]; var z0 = new float[n]; var z1 = new float[n];
        for (var k = 0; k < n; k++)
        {
            x0[k] = Math.Clamp(rooms[k].CenterX - rooms[k].Width / 2, -hw, hw);
            x1[k] = Math.Clamp(rooms[k].CenterX + rooms[k].Width / 2, -hw, hw);
            z0[k] = Math.Clamp(rooms[k].CenterZ - rooms[k].Depth / 2, -hd, hd);
            z1[k] = Math.Clamp(rooms[k].CenterZ + rooms[k].Depth / 2, -hd, hd);
        }

        // GROW-ONLY relaxation: each pass, every room grows each edge to the midline of the gap with its
        // nearest facing neighbor (in the same band), or to the footprint boundary if nothing faces it. Never
        // shrinks → stable. Computed from a snapshot and applied together so it's order-independent.
        for (var pass = 0; pass < 12; pass++)
        {
            var nx0 = (float[])x0.Clone(); var nx1 = (float[])x1.Clone();
            var nz0 = (float[])z0.Clone(); var nz1 = (float[])z1.Clone();
            for (var i = 0; i < n; i++)
            {
                float cxi = (x0[i] + x1[i]) / 2, czi = (z0[i] + z1[i]) / 2;
                float right = hw, left = -hw, top = hd, bottom = -hd;
                for (var j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    float cxj = (x0[j] + x1[j]) / 2, czj = (z0[j] + z1[j]) / 2;
                    float zob = MathF.Min(z1[i], z1[j]) - MathF.Max(z0[i], z0[j]);
                    float xob = MathF.Min(x1[i], x1[j]) - MathF.Max(x0[i], x0[j]);
                    if (zob > band) // shares a horizontal band → may block left/right growth
                    {
                        if (cxj > cxi) right = MathF.Min(right, (x1[i] + x0[j]) / 2f);
                        else left = MathF.Max(left, (x0[i] + x1[j]) / 2f);
                    }
                    if (xob > band) // shares a vertical band → may block top/bottom growth
                    {
                        if (czj > czi) top = MathF.Min(top, (z1[i] + z0[j]) / 2f);
                        else bottom = MathF.Max(bottom, (z0[i] + z1[j]) / 2f);
                    }
                }
                nx1[i] = MathF.Max(x1[i], right); nx0[i] = MathF.Min(x0[i], left);
                nz1[i] = MathF.Max(z1[i], top); nz0[i] = MathF.Min(z0[i], bottom);
            }
            x0 = nx0; x1 = nx1; z0 = nz0; z1 = nz1;
        }

        var grown = rooms.Select((r, k) => r with
        {
            CenterX = (x0[k] + x1[k]) / 2f, Width = MathF.Max(0.8f, x1[k] - x0[k]),
            CenterZ = (z0[k] + z1[k]) / 2f, Depth = MathF.Max(0.8f, z1[k] - z0[k])
        }).ToList();
        return DeOverlap(grown); // trim any residual overlaps to shared walls
    }

    /// <summary>The footprint (X,Z) a furniture piece occupies, from its given size or the real catalog default,
    /// accounting for a 90° rotation (which swaps width↔depth).</summary>
    public static (float w, float d) FurnitureFootprint(PlanFurniture fn)
    {
        float w = fn.Width is > 0.05f ? fn.Width!.Value : 0f;
        float d = fn.Depth is > 0.05f ? fn.Depth!.Value : 0f;
        if (w <= 0f || d <= 0f)
        {
            var sz = FurnitureFactory.Build(fn.Kind ?? "box", null, null, null, null)?.Size;
            if (w <= 0f) w = sz?.X ?? 0.7f;
            if (d <= 0f) d = sz?.Z ?? 0.7f;
        }
        var rot = ((fn.RotationY % 180f) + 180f) % 180f; // 0..180
        return rot is > 45f and < 135f ? (d, w) : (w, d); // ~90° → swap
    }

    /// <summary>Nudges a room's furniture apart so pieces don't pile up — using REAL item sizes so big pieces
    /// (bed, wardrobe, kitchen_counter) actually separate. Kept inside the room.</summary>
    public static List<PlanFurniture>? SpreadFurniture(PlanRoom room)
    {
        var fs = room.Furniture;
        if (fs is null || fs.Count < 2) return fs;
        var list = fs.ToList();
        var fp = list.Select(FurnitureFootprint).ToArray();
        float rhw = MathF.Max(0.1f, room.Width / 2f - 0.2f), rhd = MathF.Max(0.1f, room.Depth / 2f - 0.2f);

        for (var pass = 0; pass < 6; pass++)
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                {
                    var a = list[i]; var b = list[j];
                    float aw = fp[i].w / 2 + fp[j].w / 2, ad = fp[i].d / 2 + fp[j].d / 2;
                    float ox = aw - MathF.Abs(a.X - b.X), oz = ad - MathF.Abs(a.Z - b.Z);
                    if (ox <= 0 || oz <= 0) continue; // not overlapping
                    if (ox <= oz)
                    {
                        float push = ox / 2 + 0.02f, dir = a.X <= b.X ? 1f : -1f;
                        list[i] = a with { X = Math.Clamp(a.X - dir * push, room.CenterX - rhw, room.CenterX + rhw) };
                        list[j] = b with { X = Math.Clamp(b.X + dir * push, room.CenterX - rhw, room.CenterX + rhw) };
                    }
                    else
                    {
                        float push = oz / 2 + 0.02f, dir = a.Z <= b.Z ? 1f : -1f;
                        list[i] = a with { Z = Math.Clamp(a.Z - dir * push, room.CenterZ - rhd, room.CenterZ + rhd) };
                        list[j] = b with { Z = Math.Clamp(b.Z + dir * push, room.CenterZ - rhd, room.CenterZ + rhd) };
                    }
                }
        return list;
    }

    /// <summary>
    /// Removes overlaps between a floor's room rectangles by trimming each overlapping pair along the axis of
    /// least penetration to the overlap midline (both rooms shrink to share that wall). A few passes handle
    /// chains; rooms never shrink below a minimum size.
    /// </summary>
    public static List<PlanRoom> DeOverlap(List<PlanRoom> rooms)
    {
        const float minSize = 0.6f;
        var r = rooms.ToList();
        // Bigger rooms stay put; trim against them first.
        var order = Enumerable.Range(0, r.Count).OrderByDescending(i => r[i].Width * r[i].Depth).ToList();

        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;
            for (var ai = 0; ai < order.Count; ai++)
                for (var bi = ai + 1; bi < order.Count; bi++)
                {
                    int i = order[ai], j = order[bi];
                    var a = r[i]; var b = r[j];
                    float ax0 = a.CenterX - a.Width / 2, ax1 = a.CenterX + a.Width / 2;
                    float az0 = a.CenterZ - a.Depth / 2, az1 = a.CenterZ + a.Depth / 2;
                    float bx0 = b.CenterX - b.Width / 2, bx1 = b.CenterX + b.Width / 2;
                    float bz0 = b.CenterZ - b.Depth / 2, bz1 = b.CenterZ + b.Depth / 2;

                    float ox = MathF.Min(ax1, bx1) - MathF.Max(ax0, bx0);
                    float oz = MathF.Min(az1, bz1) - MathF.Max(az0, bz0);
                    if (ox <= 0.01f || oz <= 0.01f) continue; // no real overlap

                    if (ox <= oz) // resolve along X — trim both to the overlap midline
                    {
                        float mid = (MathF.Max(ax0, bx0) + MathF.Min(ax1, bx1)) / 2f;
                        if (a.CenterX <= b.CenterX) { ax1 = mid; bx0 = mid; } else { bx1 = mid; ax0 = mid; }
                        if (ax1 - ax0 >= minSize && bx1 - bx0 >= minSize)
                        {
                            r[i] = a with { CenterX = (ax0 + ax1) / 2, Width = ax1 - ax0 };
                            r[j] = b with { CenterX = (bx0 + bx1) / 2, Width = bx1 - bx0 };
                            changed = true;
                        }
                    }
                    else // resolve along Z
                    {
                        float mid = (MathF.Max(az0, bz0) + MathF.Min(az1, bz1)) / 2f;
                        if (a.CenterZ <= b.CenterZ) { az1 = mid; bz0 = mid; } else { bz1 = mid; az0 = mid; }
                        if (az1 - az0 >= minSize && bz1 - bz0 >= minSize)
                        {
                            r[i] = a with { CenterZ = (az0 + az1) / 2, Depth = az1 - az0 };
                            r[j] = b with { CenterZ = (bz0 + bz1) / 2, Depth = bz1 - bz0 };
                            changed = true;
                        }
                    }
                }
            if (!changed) break;
        }
        return r;
    }

    private static T? Deserialize<T>(string raw) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(ExtractJson(raw), Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Strips ```json fences / prose the model may wrap around the object.</summary>
    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        int start = raw.IndexOf('{'), end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }

    private sealed record ClassifyResult(List<PlanClassification> Items);
    private sealed record Footprint(float Width, float Depth);
    private sealed record ElevationResult(
        string RoofStyle, float RoofHeight, float GroundHeight, float UpperHeight, float BasementHeight,
        List<SiteItem> Site, int Dormers = 0,
        float Grade = 0, float Eave = 0, float Ridge = 0, float MansardBreak = 0, float BasementFloor = 0);
}
