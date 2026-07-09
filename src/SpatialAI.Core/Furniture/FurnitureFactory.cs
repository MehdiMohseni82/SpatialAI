using SpatialAI.Core.Model;

namespace SpatialAI.Core.Furniture;

/// <summary>
/// Parametric catalog that assembles recognizable furniture from primitive parts (boxes/cylinders/
/// spheres) with per-part colors. Builders author parts in floor-relative coords (Y up from 0); a
/// shared <see cref="Finalize"/> step computes the overall bounding size and recenters every offset to
/// be relative to the bounding-box center — the single convention the rest of the app stores.
/// </summary>
public static class FurnitureFactory
{
    public sealed record Built(List<Part> Parts, Vec3 Size, Rgba Primary);

    // The catalog (kinds, defaults, aliases, descriptions) is the single source of truth, loaded from
    // CatalogSeed by default and swapped for the database-backed catalog at app startup via UseCatalog.
    private static Catalog _catalog = CatalogSeed.Default();

    /// <summary>Swaps the active catalog (e.g. the database-loaded one wired at app startup).</summary>
    public static void UseCatalog(Catalog catalog) => _catalog = catalog ?? _catalog;

    /// <summary>The active catalog — source of truth for kinds, defaults, aliases and descriptions.</summary>
    public static Catalog Current => _catalog;

    /// <summary>Category-grouped kind list for the system prompt.</summary>
    public static string DescribeKinds() => _catalog.DescribeKinds();

    /// <summary>Single-line kind list for a tool's schema description.</summary>
    public static string DescribeKindsInline() => _catalog.DescribeKindsInline();

    public static string Normalize(string kind) => _catalog.Normalize(kind);

    public static bool IsKnown(string kind) => _catalog.IsKnown(kind);

    public static Built? Build(string kind, float? width, float? height, float? depth, Rgba? color)
    {
        var k = Normalize(kind);
        if (!_catalog.TryGet(k, out var def)) return null;

        float w = width ?? def.W, h = height ?? def.H, d = depth ?? def.D;
        var primary = color ?? def.Color;
        var b = new B();

        switch (k)
        {
            case "chair":
            case "dining_chair": Chair(b, w, h, d, primary); break;
            case "office_chair": OfficeChair(b, w, h, d, primary); break;
            case "armchair":     Armchair(b, w, h, d, primary); break;
            case "stool":        Stool(b, w, h, d, primary); break;
            case "table":        Table(b, w, h, d, primary); break;
            case "desk":         Desk(b, w, h, d, primary); break;
            case "computer_desk":ComputerDesk(b, w, h, d, primary); break;
            case "dining_table": DiningTable(b, w, h, d, primary); break;
            case "coffee_table": CoffeeTable(b, w, h, d, primary); break;
            case "sofa":
            case "loveseat":     Sofa(b, w, h, d, primary); break;
            case "sectional_sofa": Sectional(b, w, h, d, primary); break;
            case "bed":
            case "single_bed":
            case "double_bed":
            case "king_bed":     Bed(b, w, h, d, primary); break;
            case "bunk_bed":     BunkBed(b, w, h, d, primary); break;
            case "chest_of_drawers": ChestOfDrawers(b, w, h, d, primary); break;
            case "sideboard":    Sideboard(b, w, h, d, primary); break;
            case "desk_lamp":    DeskLamp(b, w, h, d, primary); break;
            case "ceiling_light":CeilingLight(b, w, h, d, primary); break;
            case "chandelier":   Chandelier(b, w, h, d, primary); break;
            case "nightstand":   Nightstand(b, w, h, d, primary); break;
            case "wardrobe":     Wardrobe(b, w, h, d, primary); break;
            case "bookshelf":    Bookshelf(b, w, h, d, primary); break;
            case "floor_lamp":
            case "table_lamp":   Lamp(b, w, h, d, primary); break;
            case "monitor":      Monitor(b, w, h, d, primary); break;
            case "tv":           Tv(b, w, h, d, primary); break;
            case "rug":          Rug(b, w, h, d, primary); break;
            case "plant":        Plant(b, w, h, d, primary); break;
            case "kitchen_counter":
            case "kitchen_island": Counter(b, w, h, d, primary); break;
            case "sink":         Sink(b, w, h, d, primary); break;
            case "stove":        Stove(b, w, h, d, primary); break;
            case "fridge":       Fridge(b, w, h, d, primary); break;
            case "dishwasher":
            case "washing_machine": Appliance(b, w, h, d, primary, k == "washing_machine"); break;
            case "toilet":       Toilet(b, w, h, d, primary); break;
            case "bathtub":      Bathtub(b, w, h, d, primary); break;
            case "basin":        Basin(b, w, h, d, primary); break;
            case "shower":       Shower(b, w, h, d, primary); break;
            case "radiator":     Radiator(b, w, h, d, primary); break;
            case "fireplace":    Fireplace(b, w, h, d, primary); break;
            case "ac_unit":      AcUnit(b, w, h, d, primary); break;
            case "column":       Column(b, w, h, d, primary); break;
            case "railing":      Railing(b, w, h, d, primary); break;
            case "staircase":    Staircase(b, w, h, d, primary); break;
            case "mirror":       Mirror(b, w, h, d, primary); break;
            case "bench":        Bench(b, w, h, d, primary); break;
            case "plate":        Plate(b, w, h, d, primary); break;
            case "cup":          Cup(b, w, h, d, primary); break;
            case "bowl":         Bowl(b, w, h, d, primary); break;
            case "book":         Book(b, w, h, d, primary); break;
            case "vase":         Vase(b, w, h, d, primary); break;
            case "laptop":       Laptop(b, w, h, d, primary); break;
            case "tree":         Tree(b, w, h, d, primary); break;
            case "bush":         Bush(b, w, h, d, primary); break;
            case "hedge":        Hedge(b, w, h, d, primary); break;
            case "lawn":         Lawn(b, w, h, d, primary); break;
            case "fence":        Fence(b, w, h, d, primary); break;
            case "gate":         Gate(b, w, h, d, primary); break;
            case "car":          Car(b, w, h, d, primary); break;
            case "terrace":      Terrace(b, w, h, d, primary); break;
            case "garage":       Garage(b, w, h, d, primary); break;
            case "steps":        Steps(b, w, h, d, primary); break;
            // industrial
            case "pallet_rack":  PalletRack(b, w, h, d, primary); break;
            case "cantilever_rack": CantileverRack(b, w, h, d, primary); break;
            case "shelving_unit": ShelvingUnit(b, w, h, d, primary); break;
            case "pallet":       Pallet(b, w, h, d, primary); break;
            case "pallet_jack":  PalletJack(b, w, h, d, primary); break;
            case "forklift":     Forklift(b, w, h, d, primary); break;
            case "stacker":      Stacker(b, w, h, d, primary); break;
            case "crate":        Crate(b, w, h, d, primary); break;
            case "drum":         Drum(b, w, h, d, primary); break;
            case "bollard":      Bollard(b, w, h, d, primary); break;
            case "safety_barrier": SafetyBarrier(b, w, h, d, primary); break;
            case "conveyor":     Conveyor(b, w, h, d, primary); break;
            case "cnc_machine":  CncMachine(b, w, h, d, primary); break;
            case "press":        Press(b, w, h, d, primary); break;
            case "robot_arm":    RobotArm(b, w, h, d, primary); break;
            case "workbench":    Workbench(b, w, h, d, primary); break;
            case "control_panel":ControlPanel(b, w, h, d, primary); break;
            case "agv":          Agv(b, w, h, d, primary); break;
            default: return null;
        }

        return Finalize(b.Parts, primary);
    }

    /// <summary>Computes bounding size and recenters part offsets to the bounding-box center.</summary>
    public static Built Finalize(List<Part> parts, Rgba primary)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in parts)
        {
            minX = MathF.Min(minX, p.Offset.X - p.Size.X / 2); maxX = MathF.Max(maxX, p.Offset.X + p.Size.X / 2);
            minY = MathF.Min(minY, p.Offset.Y - p.Size.Y / 2); maxY = MathF.Max(maxY, p.Offset.Y + p.Size.Y / 2);
            minZ = MathF.Min(minZ, p.Offset.Z - p.Size.Z / 2); maxZ = MathF.Max(maxZ, p.Offset.Z + p.Size.Z / 2);
        }
        var size = new Vec3(maxX - minX, maxY - minY, maxZ - minZ);
        float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, cz = (minZ + maxZ) / 2;
        foreach (var p in parts)
            p.Offset = new Vec3(p.Offset.X - cx, p.Offset.Y - cy, p.Offset.Z - cz);
        return new Built(parts, size, primary);
    }

    // ── Builders (floor-relative; Y is the part's vertical center above the floor) ───────────

    private static void Chair(B b, float w, float h, float d, Rgba c)
    {
        const float seatTop = 0.45f, seatT = 0.06f, legT = 0.05f;
        var legs = Palette.WoodDark;
        b.Legs(w, d, seatTop - seatT, legT, legs);
        b.Box(0, seatTop - seatT / 2, 0, w, seatT, d, c);                                  // seat
        b.Box(0, seatTop + (h - seatTop) / 2, -d / 2 + 0.05f, w, h - seatTop, 0.08f, c);   // backrest
    }

    private static void Stool(B b, float w, float h, float d, Rgba c)
    {
        const float seatT = 0.06f, legT = 0.05f;
        b.Legs(w, d, h - seatT, legT, Palette.Darken(c, 0.3f));
        b.Cyl(0, h - seatT / 2, 0, w, seatT, d, c);
    }

    private static void Table(B b, float w, float h, float d, Rgba c)
    {
        const float topT = 0.06f, legT = 0.07f;
        b.Box(0, h - topT / 2, 0, w, topT, d, c);
        b.Legs(w - 0.04f, d - 0.04f, h - topT, legT, Palette.Darken(c, 0.18f));
    }

    private static void Sofa(B b, float w, float h, float d, Rgba c)
    {
        var frame = Palette.Darken(c, 0.18f);
        b.Box(0, 0.20f, 0, w, 0.40f, d, frame);                                  // base
        b.Box(0, 0.55f, -d / 2 + 0.10f, w, 0.50f, 0.20f, c);                     // backrest
        b.Box(-w / 2 + 0.10f, 0.45f, 0, 0.20f, 0.45f, d, c);                     // left arm
        b.Box(w / 2 - 0.10f, 0.45f, 0, 0.20f, 0.45f, d, c);                      // right arm
        var cushionW = (w - 0.4f) / 2 - 0.04f;                                   // two seat cushions
        b.Box(-cushionW / 2 - 0.04f, 0.46f, 0.06f, cushionW, 0.14f, d - 0.34f, Palette.Lighten(c, 0.12f));
        b.Box(cushionW / 2 + 0.04f, 0.46f, 0.06f, cushionW, 0.14f, d - 0.34f, Palette.Lighten(c, 0.12f));
        b.Legs(w - 0.2f, d - 0.2f, 0.10f, 0.06f, Palette.WoodDark);
    }

    private static void Bed(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.22f, 0, w, 0.24f, d, Palette.WoodDark);                       // frame
        b.Box(0, 0.40f, 0.05f, w - 0.08f, 0.16f, d - 0.12f, Palette.White);      // mattress
        b.Box(0, 0.55f, -d / 2 + 0.05f, w, h - 0.45f, 0.10f, c);                 // headboard
        b.Box(-w / 4, 0.52f, -d / 2 + 0.30f, w / 2 - 0.14f, 0.12f, 0.34f, Palette.Lighten(c, 0.35f)); // pillow L
        b.Box(w / 4, 0.52f, -d / 2 + 0.30f, w / 2 - 0.14f, 0.12f, 0.34f, Palette.Lighten(c, 0.35f));  // pillow R
        b.Legs(w, d, 0.10f, 0.08f, Palette.WoodDark);
    }

    private static void Nightstand(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h - 0.16f, 0, w, h - 0.20f, d, c);                              // body
        b.Box(0, h - 0.16f, d / 2, w - 0.06f, 0.13f, 0.03f, Palette.Lighten(c, 0.12f)); // drawer front
        b.Box(0, h - 0.16f, d / 2 + 0.03f, 0.10f, 0.03f, 0.03f, Palette.MetalDark);     // handle
        b.Legs(w - 0.06f, d - 0.06f, 0.16f, 0.05f, Palette.WoodDark);
    }

    private static void Wardrobe(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.10f + (h - 0.10f) / 2, 0, w, h - 0.10f, d, c);                // body
        b.Box(-w / 4, h / 2 + 0.05f, d / 2, w / 2 - 0.04f, h - 0.30f, 0.03f, Palette.Lighten(c, 0.10f)); // door L
        b.Box(w / 4, h / 2 + 0.05f, d / 2, w / 2 - 0.04f, h - 0.30f, 0.03f, Palette.Lighten(c, 0.10f));  // door R
        b.Box(-0.04f, h / 2 + 0.05f, d / 2 + 0.03f, 0.03f, 0.30f, 0.03f, Palette.MetalDark);             // handle L
        b.Box(0.04f, h / 2 + 0.05f, d / 2 + 0.03f, 0.03f, 0.30f, 0.03f, Palette.MetalDark);              // handle R
        b.Legs(w - 0.08f, d - 0.08f, 0.10f, 0.06f, Palette.WoodDark);
    }

    private static void Bookshelf(B b, float w, float h, float d, Rgba c)
    {
        const float t = 0.04f;
        b.Box(-w / 2 + t / 2, h / 2, 0, t, h, d, c);                             // left side
        b.Box(w / 2 - t / 2, h / 2, 0, t, h, d, c);                              // right side
        b.Box(0, h / 2, -d / 2 + 0.01f, w, h, 0.02f, Palette.Darken(c, 0.12f));  // back
        int shelves = 4;
        for (int i = 0; i <= shelves; i++)
        {
            float y = 0.02f + i * (h - 0.04f) / shelves;
            b.Box(0, y, 0, w - 2 * t, 0.03f, d - 0.04f, c);
        }
    }

    private static void Lamp(B b, float w, float h, float d, Rgba c)
    {
        float baseD = MathF.Min(w, d) * 0.85f;
        b.Cyl(0, 0.03f, 0, baseD, 0.06f, baseD, Palette.MetalDark);             // base
        b.Cyl(0, h / 2, 0, 0.05f, h - 0.3f, 0.05f, Palette.Metal);              // pole
        b.Cyl(0, h - 0.16f, 0, w, 0.30f, d, c);                                 // shade
    }

    private static void Monitor(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.01f, 0, 0.25f, 0.02f, d, Palette.MetalDark);                 // base
        b.Box(0, 0.13f, -0.02f, 0.04f, 0.24f, 0.04f, Palette.MetalDark);        // neck
        b.Box(0, h - 0.17f, 0.02f, w, h - 0.18f, 0.03f, Palette.Screen);        // screen
    }

    private static void Tv(B b, float w, float h, float d, Rgba c)
    {
        b.Box(-w / 5, 0.015f, 0, 0.30f, 0.03f, d * 1.4f, Palette.MetalDark);    // foot L
        b.Box(w / 5, 0.015f, 0, 0.30f, 0.03f, d * 1.4f, Palette.MetalDark);     // foot R
        b.Box(0, 0.06f, 0, 0.06f, 0.10f, 0.04f, Palette.MetalDark);             // neck
        b.Box(0, 0.12f + (h - 0.12f) / 2, 0, w, h - 0.12f, 0.05f, Palette.Screen); // screen
    }

    private static void Rug(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.01f, 0, w, 0.02f, d, Palette.Darken(c, 0.25f));              // border
        b.Box(0, 0.022f, 0, w - 0.16f, 0.02f, d - 0.16f, c);                    // inner
    }

    private static void Plant(B b, float w, float h, float d, Rgba c)
    {
        float potD = MathF.Min(w, d) * 0.7f;
        b.Cyl(0, 0.16f, 0, potD, 0.32f, potD, Palette.Clay);                    // pot
        b.Cyl(0, h * 0.45f, 0, 0.05f, h * 0.5f, 0.05f, Palette.FoliageDark);    // stem
        b.Sph(0, h - 0.30f, 0, w * 0.9f, 0.55f, d * 0.9f, c);                   // foliage
        b.Sph(-w * 0.2f, h - 0.45f, 0.05f, w * 0.5f, 0.35f, d * 0.5f, Palette.Darken(c, 0.12f));
        b.Sph(w * 0.2f, h - 0.12f, -0.05f, w * 0.5f, 0.35f, d * 0.5f, Palette.Lighten(c, 0.10f));
    }

    // ── Furniture variants (distinct from the generic chair/table/sofa/bed) ───

    private static void Desk(B b, float w, float h, float d, Rgba c)
    {
        const float topT = 0.05f, legT = 0.06f;
        b.Box(0, h - topT / 2, 0, w, topT, d, c);                                       // top
        // left side stands on two legs; the right side is a drawer pedestal
        b.Box(-w / 2 + legT / 2, (h - topT) / 2, -d / 2 + legT / 2, legT, h - topT, legT, Palette.Darken(c, 0.20f));
        b.Box(-w / 2 + legT / 2, (h - topT) / 2,  d / 2 - legT / 2, legT, h - topT, legT, Palette.Darken(c, 0.20f));
        float pedW = w * 0.28f, px = w / 2 - pedW / 2;
        b.Box(px, (h - topT) / 2, 0, pedW, h - topT, d - 0.04f, Palette.Darken(c, 0.06f)); // pedestal
        b.Box(px, h - 0.20f, d / 2 - 0.01f, pedW - 0.06f, 0.11f, 0.02f, Palette.Lighten(c, 0.10f)); // drawer 1
        b.Box(px, h - 0.36f, d / 2 - 0.01f, pedW - 0.06f, 0.11f, 0.02f, Palette.Lighten(c, 0.10f)); // drawer 2
        b.Box(px, h - 0.20f, d / 2 + 0.02f, 0.10f, 0.02f, 0.02f, Palette.MetalDark);
        b.Box(px, h - 0.36f, d / 2 + 0.02f, 0.10f, 0.02f, 0.02f, Palette.MetalDark);
        b.Box(-pedW / 2, h * 0.55f, -d / 2 + 0.04f, w - pedW - 0.12f, h * 0.42f, 0.03f, Palette.Darken(c, 0.05f)); // modesty panel
    }

    private static void ComputerDesk(B b, float w, float h, float d, Rgba c)
    {
        const float topT = 0.05f;
        b.Box(0, h - topT / 2, 0, w, topT, d, c);                                       // top
        b.Legs(w - 0.06f, d - 0.06f, h - topT, 0.05f, Palette.MetalDark);               // metal legs
        b.Box(0, h * 0.60f, -d / 2 + 0.03f, w - 0.10f, h * 0.50f, 0.02f, Palette.Darken(c, 0.06f)); // back/cable panel
        const float riserH = 0.22f;                                                     // raised monitor shelf
        b.Box(-w * 0.28f, h + riserH / 2, -d * 0.15f, 0.05f, riserH, d * 0.40f, Palette.Darken(c, 0.12f));
        b.Box( w * 0.28f, h + riserH / 2, -d * 0.15f, 0.05f, riserH, d * 0.40f, Palette.Darken(c, 0.12f));
        b.Box(0, h + riserH, -d * 0.15f, w * 0.62f, 0.04f, d * 0.40f, c);
    }

    private static void DiningTable(B b, float w, float h, float d, Rgba c)
    {
        const float topT = 0.07f, legT = 0.10f;
        b.Box(0, h - topT / 2, 0, w, topT, d, c);                                       // thick top
        b.Box(0, h - topT - 0.05f, -d / 2 + 0.07f, w - 0.20f, 0.08f, 0.04f, Palette.Darken(c, 0.12f)); // apron
        b.Box(0, h - topT - 0.05f,  d / 2 - 0.07f, w - 0.20f, 0.08f, 0.04f, Palette.Darken(c, 0.12f));
        b.Legs(w - 0.16f, d - 0.16f, h - topT, legT, Palette.Darken(c, 0.16f));         // chunky legs
    }

    private static void CoffeeTable(B b, float w, float h, float d, Rgba c)
    {
        const float topT = 0.05f, legT = 0.06f;
        b.Box(0, h - topT / 2, 0, w, topT, d, c);                                       // top
        b.Box(0, 0.12f, 0, w - 0.12f, 0.03f, d - 0.12f, Palette.Darken(c, 0.10f));      // lower shelf
        b.Legs(w - 0.06f, d - 0.06f, h - topT, legT, Palette.Darken(c, 0.18f));
    }

    private static void OfficeChair(B b, float w, float h, float d, Rgba c)
    {
        var dark = Palette.MetalDark;
        const float seatTop = 0.48f, seatT = 0.10f;
        b.Cyl(0, 0.05f, 0, w * 0.55f, 0.04f, d * 0.55f, dark);                          // star base (disc)
        for (var i = 0; i < 5; i++)
        {
            var a = MathF.Tau * i / 5;
            b.Cyl(MathF.Sin(a) * w * 0.40f, 0.03f, MathF.Cos(a) * d * 0.40f, 0.07f, 0.06f, 0.07f, Palette.Screen); // casters
        }
        b.Cyl(0, 0.27f, 0, 0.06f, 0.40f, 0.06f, Palette.Metal);                         // gas cylinder
        b.Box(0, seatTop, 0, w * 0.70f, seatT, d * 0.70f, c);                           // seat
        b.Box(0, seatTop + 0.32f, -d * 0.30f, w * 0.70f, 0.55f, 0.08f, c);              // backrest
        b.Box(-w * 0.36f, seatTop + 0.12f, 0, 0.06f, 0.06f, d * 0.5f, dark);            // armrests
        b.Box( w * 0.36f, seatTop + 0.12f, 0, 0.06f, 0.06f, d * 0.5f, dark);
    }

    private static void Armchair(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.22f, 0, w, 0.34f, d, Palette.Darken(c, 0.16f));                      // base block
        b.Box(0, 0.55f, -d / 2 + 0.10f, w, 0.50f, 0.18f, c);                            // backrest
        b.Box(-w / 2 + 0.10f, 0.42f, 0, 0.18f, 0.40f, d, c);                            // arms
        b.Box( w / 2 - 0.10f, 0.42f, 0, 0.18f, 0.40f, d, c);
        b.Box(0, 0.44f, 0.04f, w - 0.34f, 0.14f, d - 0.30f, Palette.Lighten(c, 0.12f)); // seat cushion
        b.Legs(w - 0.20f, d - 0.20f, 0.10f, 0.05f, Palette.WoodDark);                   // feet
    }

    private static void Sectional(B b, float w, float h, float d, Rgba c)
    {
        var frame = Palette.Darken(c, 0.18f);
        float mainD = d * 0.5f, mz = -d / 2 + mainD / 2;
        b.Box(0, 0.20f, mz, w, 0.40f, mainD, frame);                                    // main base (along X, at front)
        b.Box(0, 0.55f, -d / 2 + 0.10f, w, 0.50f, 0.20f, c);                            // back of the main run
        b.Box(-w / 2 + 0.10f, 0.45f, mz, 0.20f, 0.45f, mainD, c);                       // left arm
        b.Box(0, 0.46f, mz, w - 0.40f, 0.14f, mainD - 0.20f, Palette.Lighten(c, 0.12f)); // main cushion
        float chW = w * 0.34f, cx = w / 2 - chW / 2;                                    // chaise extension (along +Z, right end)
        b.Box(cx, 0.20f, mainD / 2, chW, 0.40f, d - mainD, frame);
        b.Box(w / 2 - 0.10f, 0.45f, 0, 0.20f, 0.45f, d, c);                             // right arm (full depth)
        b.Box(cx, 0.46f, mainD / 2, chW - 0.20f, 0.14f, d - mainD - 0.10f, Palette.Lighten(c, 0.12f)); // chaise cushion
        b.Legs(w - 0.20f, d - 0.20f, 0.10f, 0.06f, Palette.WoodDark);
    }

    private static void BunkBed(B b, float w, float h, float d, Rgba c)
    {
        var wood = Palette.WoodDark;
        float lowerY = 0.45f, upperY = h - 0.30f;
        void Bunk(float y)
        {
            b.Box(0, y, 0, w, 0.16f, d, wood);                                          // frame
            b.Box(0, y + 0.12f, 0.03f, w - 0.08f, 0.12f, d - 0.10f, Palette.White);     // mattress
            b.Box(0, y + 0.22f, -d / 2 + 0.05f, w, 0.32f, 0.06f, c);                    // headboard
        }
        Bunk(lowerY); Bunk(upperY);
        foreach (var sx in new[] { -w / 2 + 0.05f, w / 2 - 0.05f })
            foreach (var sz in new[] { -d / 2 + 0.05f, d / 2 - 0.05f })
                b.Box(sx, h / 2, sz, 0.07f, h, 0.07f, wood);                            // corner posts
        b.Box(0, upperY + 0.22f, d / 2 - 0.05f, w, 0.10f, 0.05f, wood);                 // upper guard rail
        float lx = w / 2 - 0.14f;
        b.Box(lx, h * 0.5f, d / 2 - 0.02f, 0.05f, h, 0.05f, wood);                      // ladder rail
        for (var i = 0; i < 4; i++)
            b.Box(lx, 0.30f + i * 0.35f, d / 2 - 0.02f, 0.34f, 0.03f, 0.05f, wood);     // rungs
    }

    private static void ChestOfDrawers(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.08f + (h - 0.08f) / 2, 0, w, h - 0.08f, d, c);                       // body
        const int drawers = 4;
        float top = h - 0.04f, bottom = 0.12f, dh = (top - bottom) / drawers;
        for (var i = 0; i < drawers; i++)
        {
            float cy = bottom + dh * (i + 0.5f);
            b.Box(0, cy, d / 2 - 0.01f, w - 0.08f, dh - 0.03f, 0.02f, Palette.Lighten(c, 0.08f)); // front
            b.Box(0, cy, d / 2 + 0.02f, 0.16f, 0.03f, 0.03f, Palette.MetalDark);                  // handle
        }
        b.Legs(w - 0.08f, d - 0.08f, 0.12f, 0.05f, Palette.WoodDark);
    }

    private static void Sideboard(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.12f + (h - 0.12f) / 2, 0, w, h - 0.12f, d, c);                       // body
        const int doors = 3;
        float dw = (w - 0.08f) / doors;
        for (var i = 0; i < doors; i++)
        {
            float cx = -w / 2 + 0.04f + dw * (i + 0.5f);
            b.Box(cx, h * 0.55f, d / 2 - 0.01f, dw - 0.04f, h * 0.55f, 0.02f, Palette.Lighten(c, 0.08f)); // door
            b.Box(cx + dw * 0.30f, h * 0.55f, d / 2 + 0.02f, 0.03f, 0.12f, 0.03f, Palette.MetalDark);     // handle
        }
        b.Legs(w - 0.10f, d - 0.08f, 0.12f, 0.05f, Palette.WoodDark);
    }

    private static void DeskLamp(B b, float w, float h, float d, Rgba c)
    {
        b.Cyl(0, 0.02f, 0, w * 0.6f, 0.04f, d * 0.9f, Palette.MetalDark);               // weighted base
        b.Cyl(0, h * 0.35f, -d * 0.20f, 0.03f, h * 0.60f, 0.03f, Palette.Metal);        // lower arm
        b.Box(0, h - 0.08f, 0.02f, 0.03f, 0.03f, d * 0.70f, Palette.Metal);             // upper arm (reaches forward)
        b.Cyl(0, h - 0.10f, d * 0.32f, 0.12f, 0.10f, 0.12f, new Rgba(0.95f, 0.90f, 0.70f)); // head
    }

    private static void CeilingLight(B b, float w, float h, float d, Rgba c)
    {
        b.Cyl(0, h - 0.02f, 0, w * 0.5f, 0.04f, d * 0.5f, Palette.MetalDark);           // ceiling mount
        b.Cyl(0, h * 0.60f, 0, 0.02f, h * 0.50f, 0.02f, Palette.MetalDark);             // cord
        b.Cyl(0, h * 0.22f, 0, w, h * 0.40f, d, c);                                     // shade
    }

    private static void Chandelier(B b, float w, float h, float d, Rgba c)
    {
        b.Cyl(0, h - 0.02f, 0, 0.10f, 0.04f, 0.10f, Palette.Metal);                     // ceiling mount
        b.Cyl(0, h * 0.60f, 0, 0.02f, h * 0.50f, 0.02f, Palette.Metal);                 // chain
        b.Sph(0, h * 0.35f, 0, 0.14f, 0.14f, 0.14f, Palette.Metal);                     // central body
        for (var i = 0; i < 6; i++)
        {
            var a = MathF.Tau * i / 6;
            b.Sph(MathF.Sin(a) * w * 0.42f, h * 0.32f, MathF.Cos(a) * d * 0.42f, 0.07f, 0.10f, 0.07f, c); // bulbs
        }
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static void Counter(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.05f, 0.04f, w, 0.10f, d - 0.08f, Palette.Darken(c, 0.35f));   // toe kick
        b.Box(0, (h - 0.04f) / 2 + 0.05f, 0, w, h - 0.10f - 0.04f, d, c);        // body
        b.Box(0, h - 0.02f, 0, w + 0.04f, 0.05f, d + 0.04f, Palette.Stone);      // countertop
        b.Box(-w / 4, h * 0.5f, d / 2 + 0.01f, 0.12f, 0.03f, 0.02f, Palette.SteelDark); // handle
        b.Box(w / 4, h * 0.5f, d / 2 + 0.01f, 0.12f, 0.03f, 0.02f, Palette.SteelDark);
    }

    private static void Sink(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, (h - 0.06f) / 2, 0, w, h - 0.06f, d, Palette.Wood);             // cabinet
        b.Box(0, h - 0.03f, 0, w + 0.02f, 0.06f, d + 0.02f, Palette.Steel);      // steel top
        b.Box(0, h - 0.06f, 0.02f, w * 0.6f, 0.10f, d * 0.55f, Palette.SteelDark); // basin recess
        b.Cyl(0, h + 0.12f, -d * 0.25f, 0.04f, 0.24f, 0.04f, Palette.Steel);     // faucet stem
        b.Box(0, h + 0.22f, -d * 0.12f, 0.04f, 0.04f, 0.22f, Palette.Steel);     // spout
    }

    private static void Stove(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, (h - 0.04f) / 2, 0, w, h - 0.04f, d, c);                        // body
        b.Box(0, h - 0.02f, 0, w, 0.04f, d, Palette.Screen);                     // cooktop
        foreach (var bx in new[] { -w * 0.22f, w * 0.22f })
            foreach (var bz in new[] { -d * 0.18f, d * 0.18f })
                b.Cyl(bx, h + 0.005f, bz, 0.16f, 0.02f, 0.16f, Palette.SteelDark); // burners
        b.Box(0, h * 0.38f, d / 2, w - 0.06f, h * 0.5f, 0.03f, Palette.Lighten(c, 0.08f)); // oven door
        b.Box(0, h - 0.12f, d / 2 + 0.03f, w - 0.12f, 0.04f, 0.04f, Palette.Steel);        // handle
    }

    private static void Fridge(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h / 2, 0, w, h, d, c);
        b.Box(0, h * 0.72f, d / 2, w - 0.06f, h * 0.5f, 0.03f, Palette.Lighten(c, 0.05f)); // upper door
        b.Box(0, h * 0.24f, d / 2, w - 0.06f, h * 0.42f, 0.03f, Palette.Lighten(c, 0.05f)); // lower door
        b.Box(w * 0.3f, h * 0.72f, d / 2 + 0.03f, 0.04f, 0.30f, 0.04f, Palette.SteelDark);
        b.Box(w * 0.3f, h * 0.24f, d / 2 + 0.03f, 0.04f, 0.30f, 0.04f, Palette.SteelDark);
    }

    private static void Appliance(B b, float w, float h, float d, Rgba c, bool washer)
    {
        b.Box(0, h / 2, 0, w, h, d, c);
        b.Box(0, h * 0.45f, d / 2, w - 0.06f, h * 0.7f, 0.03f, Palette.Lighten(c, 0.06f)); // door
        b.Box(0, h * 0.86f, d / 2 + 0.02f, w * 0.5f, 0.05f, 0.02f, Palette.SteelDark);     // control panel
        if (washer)
            b.Sph(0, h * 0.45f, d / 2 + 0.02f, w * 0.5f, w * 0.5f, 0.06f, Palette.Screen); // round window
        else
            b.Box(0, h - 0.06f, d / 2 + 0.03f, w * 0.7f, 0.04f, 0.04f, Palette.SteelDark); // handle
    }

    private static void Toilet(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h - 0.18f, -d / 2 + 0.08f, w, 0.36f, 0.16f, c);                 // tank
        b.Box(0, 0.20f, 0.06f, w * 0.85f, 0.40f, d * 0.62f, c);                  // bowl base
        b.Cyl(0, 0.43f, 0.10f, w * 0.85f, 0.08f, d * 0.62f, Palette.White);      // seat
        b.Box(0, 0.06f, 0.06f, w * 0.4f, 0.12f, d * 0.3f, Palette.Darken(c, 0.05f)); // pedestal
    }

    private static void Bathtub(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h / 2, 0, w, h, d, c);                                          // outer shell
        b.Box(0, h - 0.06f, 0, w - 0.18f, 0.14f, d - 0.18f, Palette.Lighten(c, 0.06f)); // inner basin
    }

    private static void Basin(B b, float w, float h, float d, Rgba c)
    {
        b.Cyl(0, (h - 0.12f) / 2, 0, 0.20f, h - 0.12f, 0.20f, c);                // pedestal
        b.Box(0, h - 0.06f, 0, w, 0.12f, d, c);                                  // basin slab
        b.Box(0, h - 0.05f, 0.02f, w * 0.7f, 0.07f, d * 0.6f, Palette.Darken(c, 0.06f)); // bowl
        b.Cyl(0, h + 0.08f, -d * 0.3f, 0.03f, 0.16f, 0.03f, Palette.Steel);      // faucet
    }

    private static void Shower(B b, float w, float h, float d, Rgba c)
    {
        var glass = new Rgba(0.80f, 0.88f, 0.95f);
        b.Box(0, 0.04f, 0, w, 0.08f, d, Palette.Stone);                          // tray
        b.Box(0, h / 2, -d / 2 + 0.02f, w, h, 0.04f, glass);                     // back panel
        b.Box(-w / 2 + 0.02f, h / 2, 0, 0.04f, h, d, glass);                     // left panel
        b.Box(w / 2 - 0.02f, h / 2, 0, 0.04f, h, d, glass);                      // right panel
        b.Cyl(0, h * 0.6f, -d / 2 + 0.06f, 0.03f, h * 0.7f, 0.03f, Palette.Steel); // pipe
        b.Cyl(0, h - 0.06f, -d / 2 + 0.14f, 0.14f, 0.04f, 0.14f, Palette.Steel); // head
    }

    private static void Radiator(B b, float w, float h, float d, Rgba c)
    {
        var fins = Math.Max(6, (int)(w / 0.08f));
        b.Box(0, h / 2, 0, w, h, d * 0.4f, c);                                   // back
        for (var i = 0; i < fins; i++)
        {
            var x = -w / 2 + (i + 0.5f) * w / fins;
            b.Box(x, h / 2, d * 0.2f, w / fins * 0.6f, h - 0.04f, d * 0.6f, c);  // fin
        }
        b.Cyl(-w / 2 + 0.04f, 0.05f, 0, 0.04f, 0.10f, 0.04f, Palette.SteelDark); // valve
    }

    private static void Fireplace(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.05f, 0.05f, w + 0.1f, 0.10f, d + 0.1f, Palette.Darken(c, 0.12f)); // hearth
        b.Box(0, h / 2, 0, w, h, d, c);                                          // surround
        b.Box(0, h * 0.38f, d / 2 - 0.06f, w * 0.55f, h * 0.5f, 0.16f, Palette.Screen); // firebox
        b.Box(0, h - 0.04f, 0, w + 0.12f, 0.08f, d + 0.10f, Palette.Wood);       // mantel
    }

    private static void AcUnit(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h / 2, 0, w, h, d, c);
        for (var i = 0; i < 4; i++)
            b.Box(0, h - 0.10f - i * 0.06f, d / 2, w * 0.7f, 0.02f, 0.02f, Palette.SteelDark); // vents
        b.Box(0, h * 0.4f, d / 2, w * 0.5f, 0.18f, 0.02f, Palette.Screen);       // display
    }

    private static void Column(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.08f, 0, w, 0.16f, d, Palette.Darken(c, 0.10f));               // base
        b.Cyl(0, h / 2, 0, w * 0.7f, h - 0.32f, d * 0.7f, c);                    // shaft
        b.Box(0, h - 0.08f, 0, w, 0.16f, d, Palette.Darken(c, 0.10f));           // capital
    }

    private static void Railing(B b, float w, float h, float d, Rgba c)
    {
        b.Box(-w / 2 + 0.04f, h / 2, 0, 0.08f, h, d, c);                         // posts
        b.Box(w / 2 - 0.04f, h / 2, 0, 0.08f, h, d, c);
        b.Box(0, h - 0.04f, 0, w, 0.08f, d, c);                                  // top rail
        var n = Math.Max(4, (int)(w / 0.2f));
        for (var i = 1; i < n; i++)
            b.Box(-w / 2 + i * w / n, (h - 0.08f) / 2, 0, 0.03f, h - 0.08f, 0.03f, c); // balusters
    }

    private static void Staircase(B b, float w, float h, float d, Rgba c)
    {
        var steps = Math.Max(5, (int)(h / 0.18f));
        float rise = h / steps, run = d / steps;
        for (var i = 0; i < steps; i++)
        {
            var y = (i + 1) * rise;
            var z = -d / 2 + (i + 0.5f) * run;
            b.Box(0, y - rise / 2, z, w, rise, run, i % 2 == 0 ? c : Palette.Darken(c, 0.07f));
        }
    }

    private static void Mirror(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h / 2, 0, w, h, d, c);                                          // frame
        b.Box(0, h / 2, d / 2, w - 0.08f, h - 0.08f, 0.01f, new Rgba(0.82f, 0.88f, 0.92f)); // glass
    }

    private static void Bench(B b, float w, float h, float d, Rgba c)
    {
        b.Legs(w, d, h - 0.05f, 0.06f, Palette.Darken(c, 0.20f));
        b.Box(0, h - 0.025f, 0, w, 0.05f, d, c);                                 // seat
    }

    private static void Plate(B b, float w, float h, float d, Rgba c)
    {
        b.Cyl(0, h * 0.30f, 0, w, h * 0.60f, d, c);                              // dish
        b.Cyl(0, h * 0.80f, 0, w * 0.82f, h * 0.40f, d * 0.82f, Palette.Darken(c, 0.05f)); // well
    }

    private static void Cup(B b, float w, float h, float d, Rgba c)
    {
        b.Cyl(0, h / 2, 0, w, h, d, c);                                          // body
        b.Box(w * 0.55f, h * 0.5f, 0, w * 0.18f, h * 0.5f, 0.025f, c);           // handle
    }

    private static void Bowl(B b, float w, float h, float d, Rgba c)
    {
        b.Cyl(0, h / 2, 0, w, h, d, c);                                          // outer
        b.Cyl(0, h * 0.62f, 0, w * 0.8f, h * 0.65f, d * 0.8f, Palette.Darken(c, 0.10f)); // inside
    }

    private static void Book(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h / 2, 0, w, h, d, c);                                          // cover
        b.Box(0.01f, h / 2, 0, w - 0.04f, h - 0.02f, d - 0.02f, Palette.White);  // pages
    }

    private static void Vase(B b, float w, float h, float d, Rgba c)
    {
        b.Cyl(0, h * 0.28f, 0, w, h * 0.56f, d, c);                              // bulb
        b.Cyl(0, h * 0.74f, 0, w * 0.55f, h * 0.5f, d * 0.55f, c);               // neck
    }

    private static void Laptop(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.012f, d * 0.08f, w, 0.024f, d * 0.92f, c);                    // base / keyboard
        b.Box(0, h * 0.5f, -d * 0.46f, w, h, 0.02f, Palette.Screen);             // screen (upright at back)
    }

    // ── Outdoor / site ───────────────────────────────────────────────────────
    private static void Tree(B b, float w, float h, float d, Rgba c)
    {
        var trunk = Palette.WoodDark;
        b.Cyl(0, h * 0.28f, 0, w * 0.16f, h * 0.56f, d * 0.16f, trunk);            // trunk
        b.Sph(0, h * 0.66f, 0, w, h * 0.55f, d, c);                                // main canopy
        b.Sph(-w * 0.22f, h * 0.58f, d * 0.12f, w * 0.6f, h * 0.4f, d * 0.6f, Palette.Darken(c, 0.06f));
        b.Sph(w * 0.20f, h * 0.78f, -d * 0.10f, w * 0.55f, h * 0.38f, d * 0.55f, Palette.Lighten(c, 0.05f));
    }

    private static void Bush(B b, float w, float h, float d, Rgba c)
    {
        b.Sph(0, h * 0.5f, 0, w, h, d, c);
        b.Sph(-w * 0.28f, h * 0.42f, d * 0.1f, w * 0.6f, h * 0.7f, d * 0.6f, Palette.Darken(c, 0.05f));
        b.Sph(w * 0.26f, h * 0.46f, -d * 0.12f, w * 0.55f, h * 0.65f, d * 0.55f, Palette.Lighten(c, 0.05f));
    }

    private static void Hedge(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h / 2, 0, w, h, d, c);                                            // clipped block
        b.Box(0, h - 0.04f, 0, w - 0.06f, 0.08f, d - 0.06f, Palette.Lighten(c, 0.06f)); // top sheen
    }

    private static void Lawn(B b, float w, float h, float d, Rgba c)
        => b.Box(0, h / 2, 0, w, h, d, c);                                         // flat green patch

    private static void Fence(B b, float w, float h, float d, Rgba c)
    {
        var posts = Math.Max(2, (int)(w / 0.6f) + 1);
        for (var i = 0; i < posts; i++)
        {
            var x = -w / 2 + i * (w / (posts - 1));
            b.Box(x, h / 2, 0, 0.08f, h, d, c);                                    // post
        }
        b.Box(0, h * 0.78f, 0, w, 0.06f, d * 0.6f, Palette.Lighten(c, 0.05f));     // top rail
        b.Box(0, h * 0.30f, 0, w, 0.06f, d * 0.6f, Palette.Lighten(c, 0.05f));     // bottom rail
    }

    private static void Gate(B b, float w, float h, float d, Rgba c)
    {
        b.Box(-w / 2 + 0.05f, h / 2, 0, 0.10f, h, d, c);                           // left post
        b.Box(w / 2 - 0.05f, h / 2, 0, 0.10f, h, d, c);                            // right post
        b.Box(0, h * 0.45f, 0, w - 0.2f, h * 0.7f, d * 0.5f, Palette.Lighten(c, 0.06f)); // panel
    }

    private static void Car(B b, float w, float h, float d, Rgba c)
    {
        var glass = new Rgba(0.45f, 0.55f, 0.62f);
        var tyre = Palette.Screen;
        b.Box(0, h * 0.34f, 0, w, h * 0.42f, d, c);                                // lower body
        b.Box(0, h * 0.66f, d * 0.04f, w * 0.86f, h * 0.34f, d * 0.5f, Palette.Lighten(c, 0.04f)); // cabin
        b.Box(0, h * 0.66f, d * 0.04f, w * 0.80f, h * 0.26f, d * 0.46f, glass);    // windows
        foreach (var sx in new[] { -w / 2 + 0.05f, w / 2 - 0.05f })
            foreach (var sz in new[] { -d * 0.32f, d * 0.32f })
                b.Cyl(sx, h * 0.18f, sz, 0.32f, 0.18f, 0.32f, tyre);              // wheels (axis along X)
    }

    private static void Terrace(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h / 2, 0, w, h, d, c);                                            // deck slab
        var planks = Math.Max(3, (int)(w / 0.4f));
        for (var i = 0; i < planks; i++)
        {
            var x = -w / 2 + (i + 0.5f) * (w / planks);
            b.Box(x, h + 0.005f, 0, 0.02f, 0.01f, d, Palette.Darken(c, 0.08f));    // plank seams
        }
    }

    private static void Garage(B b, float w, float h, float d, Rgba c)
    {
        var t = 0.12f;
        b.Box(-w / 2 + t / 2, h / 2, 0, t, h, d, c);                              // left wall
        b.Box(w / 2 - t / 2, h / 2, 0, t, h, d, c);                               // right wall
        b.Box(0, h / 2, -d / 2 + t / 2, w, h, t, c);                              // back wall
        b.Box(0, h + 0.05f, 0, w + 0.2f, 0.10f, d + 0.2f, Palette.Darken(c, 0.12f)); // flat roof
        b.Box(0, h * 0.42f, d / 2 - 0.04f, w * 0.82f, h * 0.78f, 0.06f, Palette.Lighten(c, 0.05f)); // door
    }

    // A solid exterior stoop — 3 treads rising toward the door (the +Z / back side).
    private static void Steps(B b, float w, float h, float d, Rgba c)
    {
        const int n = 3;
        float sh = h / n, sd = d / n;
        for (var i = 0; i < n; i++)
        {
            float top = sh * (i + 1);                 // each tread is a solid block up to its level
            float frontZ = -d / 2 + sd * i;           // shrinks from the back as it rises
            float depth = d - sd * i;
            b.Box(0, top / 2f, frontZ + depth / 2f, w, top, depth, i == n - 1 ? c : Palette.Lighten(c, 0.04f));
        }
    }

    // ── Industrial — warehouse / logistics ───────────────────────────────────

    private static void PalletRack(B b, float w, float h, float d, Rgba c)
    {
        const float postT = 0.08f;
        foreach (var sx in new[] { -w / 2 + postT / 2, w / 2 - postT / 2 })
            foreach (var sz in new[] { -d / 2 + postT / 2, d / 2 - postT / 2 })
                b.Box(sx, h / 2, sz, postT, h, postT, c);                            // 4 uprights
        var levels = Math.Max(2, (int)(h / 1.0f));
        var beam = Palette.SafetyOrange;
        for (var i = 1; i <= levels; i++)
        {
            float y = i * (h / (levels + 1));
            b.Box(0, y, -d / 2 + postT / 2, w, 0.10f, postT, beam);                  // front/back beams
            b.Box(0, y,  d / 2 - postT / 2, w, 0.10f, postT, beam);
        }
        foreach (var sx in new[] { -w / 2 + postT / 2, w / 2 - postT / 2 })          // side cross-members
        {
            b.Box(sx, h * 0.33f, 0, postT * 0.6f, 0.04f, d * 0.9f, c);
            b.Box(sx, h * 0.66f, 0, postT * 0.6f, 0.04f, d * 0.9f, c);
        }
    }

    private static void CantileverRack(B b, float w, float h, float d, Rgba c)
    {
        const float postT = 0.10f;
        foreach (var sx in new[] { -w / 2 + 0.3f, w / 2 - 0.3f })
        {
            b.Box(sx, h / 2, 0, postT, h, postT, c);                                 // column
            b.Box(sx, 0.06f, 0, postT * 1.4f, 0.12f, d, c);                          // base foot
            for (var i = 1; i <= 3; i++)
            {
                float y = i * (h / 4f);
                b.Box(sx, y,  d * 0.25f, postT * 0.8f, 0.08f, d * 0.5f, Palette.SafetyOrange); // arms
                b.Box(sx, y, -d * 0.25f, postT * 0.8f, 0.08f, d * 0.5f, Palette.SafetyOrange);
            }
        }
    }

    private static void ShelvingUnit(B b, float w, float h, float d, Rgba c)
    {
        const float postT = 0.05f;
        foreach (var sx in new[] { -w / 2 + postT / 2, w / 2 - postT / 2 })
            foreach (var sz in new[] { -d / 2 + postT / 2, d / 2 - postT / 2 })
                b.Box(sx, h / 2, sz, postT, h, postT, c);                            // posts
        const int shelves = 4;
        for (var i = 0; i <= shelves; i++)
        {
            float y = 0.04f + i * (h - 0.06f) / shelves;
            b.Box(0, y, 0, w - 2 * postT, 0.03f, d - 2 * postT, Palette.Lighten(c, 0.06f));
        }
    }

    private static void Pallet(B b, float w, float h, float d, Rgba c)
    {
        const int boards = 5;
        for (var i = 0; i < boards; i++)
            b.Box(0, h - 0.02f, -d / 2 + (i + 0.5f) * (d / boards), w, 0.025f, d / boards * 0.7f, c); // deck
        foreach (var sx in new[] { -w / 2 + 0.08f, 0f, w / 2 - 0.08f })
            b.Box(sx, 0.05f, 0, 0.12f, 0.10f, d, Palette.Darken(c, 0.12f));          // stringers
    }

    private static void PalletJack(B b, float w, float h, float d, Rgba c)
    {
        foreach (var sx in new[] { -w * 0.3f, w * 0.3f })
            b.Box(sx, 0.08f, 0.10f, w * 0.22f, 0.10f, d * 0.8f, c);                  // forks
        b.Box(0, 0.10f, -d / 2 + 0.12f, w, 0.14f, 0.18f, c);                         // rear cross
        b.Box(0, h * 0.5f, -d / 2 + 0.10f, 0.06f, h, 0.06f, Palette.MetalDark);      // tiller
        b.Box(0, h - 0.05f, -d / 2 + 0.02f, w * 0.5f, 0.06f, 0.12f, Palette.MetalDark); // grip
        foreach (var sx in new[] { -w * 0.3f, w * 0.3f })
            b.Cyl(sx, 0.04f, d / 2 - 0.08f, 0.10f, 0.08f, 0.10f, Palette.Screen);    // load wheels
        b.Cyl(0, 0.05f, -d / 2 + 0.16f, 0.12f, 0.10f, 0.12f, Palette.Screen);        // steer wheel
    }

    private static void Forklift(B b, float w, float h, float d, Rgba c)
    {
        var dark = Palette.MetalDark;
        b.Box(0, 0.55f, -d * 0.20f, w, 0.80f, d * 0.55f, c);                         // counterweight body
        b.Box(0, 1.00f, -d * 0.32f, w * 0.8f, 0.50f, d * 0.30f, Palette.Darken(c, 0.10f)); // seat back
        b.Box(0, 0.95f, -d * 0.16f, w * 0.7f, 0.12f, 0.40f, Palette.Screen);         // seat
        foreach (var sx in new[] { -w / 2 + 0.06f, w / 2 - 0.06f })                  // overhead guard posts
            foreach (var sz in new[] { -d * 0.05f, -d * 0.40f })
                b.Box(sx, 1.50f, sz, 0.06f, 1.00f, 0.06f, dark);
        b.Box(0, h - 0.05f, -d * 0.22f, w, 0.06f, d * 0.45f, dark);                  // guard roof
        foreach (var sx in new[] { -w * 0.28f, w * 0.28f })                          // mast rails
            b.Box(sx, h * 0.5f, d * 0.42f, 0.08f, h * 0.85f, 0.10f, dark);
        b.Box(0, 0.30f, d * 0.46f, w * 0.6f, 0.40f, 0.06f, dark);                    // carriage
        foreach (var sx in new[] { -w * 0.2f, w * 0.2f })
            b.Box(sx, 0.06f, d * 0.70f, 0.12f, 0.05f, d * 0.5f, Palette.SteelDark);  // forks
        foreach (var sx in new[] { -w / 2 + 0.06f, w / 2 - 0.06f })
            foreach (var sz in new[] { d * 0.1f, -d * 0.35f })
                b.Cyl(sx, 0.25f, sz, 0.50f, 0.25f, 0.50f, Palette.Screen);           // wheels
    }

    private static void Stacker(B b, float w, float h, float d, Rgba c)
    {
        var dark = Palette.MetalDark;
        b.Box(0, 0.50f, -d * 0.30f, w, 1.00f, d * 0.40f, c);                         // body
        b.Box(0, h * 0.5f, -d * 0.28f, 0.08f, h, 0.08f, dark);                       // handle column
        foreach (var sx in new[] { -w * 0.3f, w * 0.3f })
            b.Box(sx, h * 0.5f, d * 0.4f, 0.07f, h * 0.95f, 0.09f, dark);            // mast rails
        foreach (var sx in new[] { -w * 0.22f, w * 0.22f })
            b.Box(sx, 0.06f, d * 0.6f, 0.10f, 0.05f, d * 0.6f, Palette.SteelDark);   // forks
        foreach (var sx in new[] { -w / 2 + 0.05f, w / 2 - 0.05f })
            b.Cyl(sx, 0.08f, -d * 0.4f, 0.16f, 0.16f, 0.16f, Palette.Screen);        // wheels
    }

    private static void Crate(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h / 2, 0, w - 0.06f, h - 0.06f, d - 0.06f, c);                      // body
        var frame = Palette.Darken(c, 0.20f);
        foreach (var sx in new[] { -w / 2 + 0.03f, w / 2 - 0.03f })
            foreach (var sz in new[] { -d / 2 + 0.03f, d / 2 - 0.03f })
                b.Box(sx, h / 2, sz, 0.06f, h, 0.06f, frame);                        // corner battens
        foreach (var sy in new[] { 0.04f, h - 0.04f })
        {
            b.Box(0, sy, -d / 2 + 0.03f, w, 0.06f, 0.06f, frame);
            b.Box(0, sy,  d / 2 - 0.03f, w, 0.06f, 0.06f, frame);
        }
    }

    private static void Drum(B b, float w, float h, float d, Rgba c)
    {
        b.Cyl(0, h / 2, 0, w, h, d, c);                                              // body
        b.Cyl(0, h * 0.3f, 0, w * 1.04f, 0.06f, d * 1.04f, Palette.Darken(c, 0.15f)); // rings
        b.Cyl(0, h * 0.7f, 0, w * 1.04f, 0.06f, d * 1.04f, Palette.Darken(c, 0.15f));
        b.Cyl(0, h - 0.02f, 0, w * 0.9f, 0.04f, d * 0.9f, Palette.MetalDark);        // lid
    }

    private static void Bollard(B b, float w, float h, float d, Rgba c)
    {
        b.Cyl(0, 0.03f, 0, w * 1.3f, 0.06f, d * 1.3f, Palette.MetalDark);            // base plate
        b.Cyl(0, h / 2, 0, w, h, d, c);                                              // post
        b.Box(0, h * 0.5f, 0, w * 1.02f, 0.12f, d * 1.02f, Palette.MetalDark);       // hazard band
        b.Sph(0, h - 0.04f, 0, w, w, d, c);                                          // dome top
    }

    private static void SafetyBarrier(B b, float w, float h, float d, Rgba c)
    {
        foreach (var sx in new[] { -w / 2 + 0.05f, w / 2 - 0.05f })
        {
            b.Box(sx, h / 2, 0, 0.08f, h, d, c);                                     // posts
            b.Cyl(sx, 0.04f, 0, 0.16f, 0.08f, 0.16f, Palette.MetalDark);             // feet
        }
        b.Box(0, h - 0.08f, 0, w, 0.10f, d * 0.7f, c);                               // top rail
        b.Box(0, h * 0.45f, 0, w, 0.10f, d * 0.7f, c);                               // mid rail
    }

    // ── Industrial — manufacturing ───────────────────────────────────────────

    private static void Conveyor(B b, float w, float h, float d, Rgba c)
    {
        float deckY = h - 0.10f;
        b.Box(0, deckY, 0, w, 0.06f, d * 0.8f, Palette.Screen);                      // belt
        b.Box(0, deckY + 0.06f, -d / 2 + 0.04f, w, 0.10f, 0.04f, c);                 // side rails
        b.Box(0, deckY + 0.06f,  d / 2 - 0.04f, w, 0.10f, 0.04f, c);
        foreach (var sx in new[] { -w / 2 + 0.1f, w / 2 - 0.1f })
            foreach (var sz in new[] { -d / 2 + 0.08f, d / 2 - 0.08f })
                b.Box(sx, deckY / 2, sz, 0.06f, deckY, 0.06f, c);                    // legs
        foreach (var sx in new[] { -w / 2 + 0.04f, w / 2 - 0.04f })
            b.Box(sx, deckY, 0, 0.10f, 0.10f, d * 0.78f, Palette.MetalDark);         // end rollers
    }

    private static void CncMachine(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h * 0.5f, 0, w, h, d, c);                                           // enclosure
        b.Box(0, h * 0.55f, d / 2 + 0.005f, w * 0.6f, h * 0.4f, 0.02f, Palette.Screen); // window
        b.Box(w * 0.5f + 0.12f, h * 0.5f, d * 0.2f, 0.24f, h * 0.5f, 0.30f, Palette.SteelDark); // control box
        b.Box(w * 0.5f + 0.12f, h * 0.55f, d * 0.2f + 0.16f, 0.18f, 0.18f, 0.02f, Palette.Screen); // HMI
        b.Cyl(w * 0.3f, h + 0.08f, 0, 0.05f, 0.16f, 0.05f, new Rgba(0.90f, 0.20f, 0.15f)); // stack light
        b.Cyl(w * 0.3f, h + 0.22f, 0, 0.05f, 0.12f, 0.05f, new Rgba(0.95f, 0.80f, 0.15f));
    }

    private static void Press(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, 0.20f, 0, w, 0.40f, d, Palette.Darken(c, 0.10f));                   // base
        b.Box(0, h * 0.5f, -d / 2 + 0.18f, w, h, 0.36f, c);                          // back frame
        b.Box(0, h - 0.15f, 0.05f, w * 0.9f, 0.30f, d * 0.7f, c);                    // crown
        b.Box(0, h * 0.62f, 0.10f, w * 0.5f, 0.50f, d * 0.4f, Palette.SteelDark);    // ram
        b.Box(0, h * 0.34f, 0.10f, w * 0.7f, 0.12f, d * 0.5f, Palette.MetalDark);    // bolster
    }

    private static void RobotArm(B b, float w, float h, float d, Rgba c)
    {
        var joint = Palette.MetalDark;
        b.Cyl(0, 0.06f, 0, w * 0.9f, 0.12f, d * 0.9f, joint);                        // base
        b.Cyl(0, 0.28f, 0, w * 0.5f, 0.40f, d * 0.5f, c);                            // rotating column
        b.Box(0, h * 0.45f, 0.05f, w * 0.25f, h * 0.40f, d * 0.25f, c);              // lower arm
        b.Sph(0, h * 0.62f, 0.05f, 0.18f, 0.18f, 0.18f, joint);                      // elbow
        b.Box(0, h * 0.70f, d * 0.20f, w * 0.22f, 0.16f, d * 0.5f, c);               // upper arm
        b.Box(0, h * 0.70f, d * 0.42f, 0.12f, 0.20f, 0.12f, joint);                  // wrist
        b.Box(0, h * 0.62f, d * 0.50f, 0.16f, 0.08f, 0.08f, Palette.SteelDark);      // gripper
    }

    private static void Workbench(B b, float w, float h, float d, Rgba c)
    {
        const float topH = 0.90f;
        b.Box(0, topH - 0.03f, 0, w, 0.06f, d, Palette.Wood);                        // worktop
        b.Legs(w - 0.08f, d - 0.08f, topH - 0.06f, 0.07f, c);                        // legs
        b.Box(0, 0.15f, 0, w - 0.12f, 0.03f, d - 0.10f, Palette.Darken(c, 0.10f));   // lower shelf
        b.Box(0, topH + (h - topH) / 2, -d / 2 + 0.03f, w, h - topH, 0.03f, Palette.SteelDark); // pegboard
    }

    private static void ControlPanel(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h * 0.5f, 0, w, h, d, c);                                           // cabinet
        b.Box(0, h * 0.55f, d / 2 + 0.005f, w * 0.85f, h * 0.7f, 0.02f, Palette.Darken(c, 0.10f)); // door
        b.Box(0, h * 0.7f, d / 2 + 0.02f, w * 0.5f, 0.28f, 0.02f, Palette.Screen);   // HMI screen
        foreach (var bx in new[] { -w * 0.2f, 0f, w * 0.2f })
            b.Cyl(bx, h * 0.45f, d / 2 + 0.02f, 0.05f, 0.03f, 0.05f, new Rgba(0.20f, 0.70f, 0.30f)); // buttons
        b.Box(w * 0.3f, h * 0.30f, d / 2 + 0.02f, 0.06f, 0.12f, 0.03f, new Rgba(0.90f, 0.20f, 0.15f)); // e-stop
    }

    private static void Agv(B b, float w, float h, float d, Rgba c)
    {
        b.Box(0, h * 0.5f, 0, w, h * 0.8f, d, c);                                    // deck body
        b.Box(0, h * 0.9f, 0, w * 0.8f, h * 0.2f, d * 0.8f, Palette.Darken(c, 0.10f)); // top plate
        b.Box(0, h * 0.3f,  d / 2 + 0.02f, w, 0.08f, 0.04f, Palette.MetalDark);      // bumpers
        b.Box(0, h * 0.3f, -d / 2 - 0.02f, w, 0.08f, 0.04f, Palette.MetalDark);
        foreach (var sx in new[] { -w / 2 + 0.08f, w / 2 - 0.08f })
            foreach (var sz in new[] { -d * 0.3f, d * 0.3f })
                b.Cyl(sx, 0.06f, sz, 0.12f, 0.12f, 0.12f, Palette.Screen);           // wheels
    }

    /// <summary>Small mutable part collector with primitive + 4-leg helpers.</summary>
    private sealed class B
    {
        public readonly List<Part> Parts = [];

        public void Add(Shape s, float ox, float oy, float oz, float sx, float sy, float sz, Rgba c)
            => Parts.Add(new Part { Shape = s, Offset = new Vec3(ox, oy, oz), Size = new Vec3(sx, sy, sz), Color = c });

        public void Box(float ox, float oy, float oz, float sx, float sy, float sz, Rgba c)
            => Add(Shape.Box, ox, oy, oz, sx, sy, sz, c);

        public void Cyl(float ox, float oy, float oz, float sx, float sy, float sz, Rgba c)
            => Add(Shape.Cylinder, ox, oy, oz, sx, sy, sz, c);

        public void Sph(float ox, float oy, float oz, float sx, float sy, float sz, Rgba c)
            => Add(Shape.Sphere, ox, oy, oz, sx, sy, sz, c);

        public void Legs(float spanX, float spanZ, float legH, float t, Rgba c)
        {
            float x = spanX / 2 - t / 2, z = spanZ / 2 - t / 2;
            foreach (var sx in new[] { -x, x })
                foreach (var sz in new[] { -z, z })
                    Box(sx, legH / 2, sz, t, legH, t, c);
        }
    }
}
