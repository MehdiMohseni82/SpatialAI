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

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["couch"] = "sofa", ["settee"] = "sofa", ["loveseat"] = "sofa", ["armchair"] = "chair",
        ["bookcase"] = "bookshelf", ["shelf"] = "bookshelf", ["shelves"] = "bookshelf",
        ["coffeetable"] = "coffee_table", ["dining_table"] = "table", ["diningtable"] = "table",
        ["sidetable"] = "nightstand", ["side_table"] = "nightstand", ["bedside"] = "nightstand",
        ["bedside_table"] = "nightstand", ["nightstand"] = "nightstand",
        ["dresser"] = "wardrobe", ["closet"] = "wardrobe", ["drawers"] = "wardrobe", ["chest"] = "wardrobe",
        ["television"] = "tv", ["tv_unit"] = "tv", ["tvstand"] = "tv", ["tv_stand"] = "tv",
        ["screen"] = "monitor", ["display"] = "monitor",
        ["lamp"] = "floor_lamp", ["floorlamp"] = "floor_lamp", ["standing_lamp"] = "floor_lamp",
        ["desk_lamp"] = "table_lamp", ["tablelamp"] = "table_lamp",
        ["pottedplant"] = "plant", ["potted_plant"] = "plant", ["pot_plant"] = "plant", ["houseplant"] = "plant",
        ["carpet"] = "rug", ["bar_stool"] = "stool", ["barstool"] = "stool",
        // fixtures
        ["counter"] = "kitchen_counter", ["kitchen"] = "kitchen_counter", ["countertop"] = "kitchen_counter",
        ["island"] = "kitchen_island", ["oven"] = "stove", ["cooker"] = "stove", ["range"] = "stove",
        ["refrigerator"] = "fridge", ["washbasin"] = "basin", ["washstand"] = "basin", ["wc"] = "toilet",
        ["bath"] = "bathtub", ["tub"] = "bathtub", ["washer"] = "washing_machine",
        ["washing_machine"] = "washing_machine", ["heater"] = "radiator", ["ac"] = "ac_unit",
        ["air_conditioner"] = "ac_unit", ["stairs"] = "staircase", ["stair"] = "staircase",
        ["pillar"] = "column", ["balustrade"] = "railing", ["banister"] = "railing",
        ["dish"] = "plate", ["mug"] = "cup", ["notebook"] = "book", ["flowerpot"] = "vase",
        // outdoor / site
        ["vehicle"] = "car", ["auto"] = "car", ["automobile"] = "car",
        ["deck"] = "terrace", ["patio"] = "terrace",
        ["stoop"] = "steps", ["entrance_steps"] = "steps", ["stairs_outside"] = "steps", ["porch"] = "steps",
        ["shrub"] = "bush", ["bushes"] = "bush", ["grass"] = "lawn", ["hedgerow"] = "hedge",
    };

    // kind -> (defaultW, defaultH, defaultD, defaultPrimaryColor)
    private static readonly Dictionary<string, (float W, float H, float D, Rgba C)> Defaults = new()
    {
        ["chair"]       = (0.50f, 0.90f, 0.50f, new(0.30f, 0.45f, 0.65f)),
        ["stool"]       = (0.40f, 0.60f, 0.40f, Palette.Wood),
        ["desk"]        = (1.40f, 0.75f, 0.70f, Palette.Wood),
        ["table"]       = (1.20f, 0.75f, 0.80f, Palette.Wood),
        ["coffee_table"]= (1.00f, 0.45f, 0.55f, Palette.Wood),
        ["sofa"]        = (1.85f, 0.80f, 0.90f, new(0.42f, 0.48f, 0.55f)),
        ["bed"]         = (1.60f, 0.95f, 2.05f, new(0.55f, 0.40f, 0.30f)),
        ["nightstand"]  = (0.45f, 0.50f, 0.40f, Palette.Wood),
        ["wardrobe"]    = (1.20f, 2.00f, 0.60f, Palette.Wood),
        ["bookshelf"]   = (0.90f, 1.80f, 0.30f, Palette.Wood),
        ["floor_lamp"]  = (0.36f, 1.60f, 0.36f, new(0.95f, 0.90f, 0.70f)),
        ["table_lamp"]  = (0.30f, 0.50f, 0.30f, new(0.95f, 0.90f, 0.70f)),
        ["monitor"]     = (0.60f, 0.50f, 0.18f, Palette.MetalDark),
        ["tv"]          = (1.20f, 0.78f, 0.14f, Palette.MetalDark),
        ["rug"]         = (2.00f, 0.03f, 1.40f, new(0.72f, 0.32f, 0.30f)),
        ["plant"]       = (0.50f, 1.05f, 0.50f, Palette.Foliage),
        // kitchen
        ["kitchen_counter"] = (2.00f, 0.90f, 0.60f, Palette.Wood),
        ["kitchen_island"]  = (1.40f, 0.90f, 0.90f, Palette.Wood),
        ["sink"]            = (0.60f, 0.90f, 0.60f, Palette.Steel),
        ["stove"]           = (0.60f, 0.90f, 0.60f, Palette.SteelDark),
        ["fridge"]          = (0.70f, 1.80f, 0.70f, Palette.Steel),
        ["dishwasher"]      = (0.60f, 0.85f, 0.60f, Palette.Steel),
        // bathroom
        ["toilet"]          = (0.40f, 0.78f, 0.70f, Palette.Ceramic),
        ["bathtub"]         = (1.70f, 0.58f, 0.75f, Palette.Ceramic),
        ["basin"]           = (0.55f, 0.85f, 0.45f, Palette.Ceramic),
        ["shower"]          = (0.90f, 2.00f, 0.90f, Palette.Steel),
        // heating / appliances
        ["radiator"]        = (0.80f, 0.60f, 0.10f, Palette.White),
        ["fireplace"]       = (1.30f, 1.15f, 0.40f, Palette.Stone),
        ["ac_unit"]         = (0.40f, 0.70f, 0.40f, Palette.White),
        ["washing_machine"] = (0.60f, 0.85f, 0.60f, Palette.White),
        // structural
        ["column"]          = (0.32f, 2.50f, 0.32f, Palette.White),
        ["railing"]         = (1.60f, 1.00f, 0.10f, Palette.Wood),
        ["staircase"]       = (1.00f, 2.50f, 3.00f, Palette.Wood),
        // misc
        ["mirror"]          = (0.60f, 1.60f, 0.05f, Palette.Wood),
        ["bench"]           = (1.30f, 0.45f, 0.40f, Palette.Wood),
        // tabletop
        ["plate"]           = (0.24f, 0.04f, 0.24f, Palette.Ceramic),
        ["cup"]             = (0.08f, 0.10f, 0.08f, new(0.90f, 0.90f, 0.92f)),
        ["bowl"]            = (0.18f, 0.08f, 0.18f, Palette.Ceramic),
        ["book"]            = (0.16f, 0.04f, 0.22f, new(0.50f, 0.30f, 0.30f)),
        ["vase"]            = (0.14f, 0.30f, 0.14f, new(0.40f, 0.55f, 0.65f)),
        ["laptop"]          = (0.33f, 0.22f, 0.23f, Palette.SteelDark),
        // outdoor / site
        ["tree"]            = (2.20f, 4.50f, 2.20f, Palette.Foliage),
        ["bush"]            = (0.90f, 0.80f, 0.90f, Palette.Foliage),
        ["hedge"]           = (2.00f, 1.00f, 0.50f, Palette.FoliageDark),
        ["lawn"]            = (4.00f, 0.04f, 4.00f, new(0.34f, 0.52f, 0.30f)),
        ["fence"]           = (2.00f, 1.00f, 0.10f, Palette.WoodDark),
        ["gate"]            = (1.20f, 1.30f, 0.10f, Palette.WoodDark),
        ["car"]             = (1.90f, 1.50f, 4.40f, new(0.16f, 0.19f, 0.24f)),
        ["terrace"]         = (4.00f, 0.16f, 3.00f, Palette.Wood),
        ["garage"]          = (3.20f, 2.60f, 5.60f, Palette.Stone),
        ["steps"]           = (1.60f, 0.66f, 1.20f, Palette.Stone),
    };

    public static string Normalize(string kind)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        return Aliases.TryGetValue(k, out var mapped) ? mapped : k;
    }

    public static bool IsKnown(string kind) => Defaults.ContainsKey(Normalize(kind));

    public static Built? Build(string kind, float? width, float? height, float? depth, Rgba? color)
    {
        var k = Normalize(kind);
        if (!Defaults.TryGetValue(k, out var def)) return null;

        float w = width ?? def.W, h = height ?? def.H, d = depth ?? def.D;
        var primary = color ?? def.C;
        var b = new B();

        switch (k)
        {
            case "chair":        Chair(b, w, h, d, primary); break;
            case "stool":        Stool(b, w, h, d, primary); break;
            case "desk":
            case "table":
            case "coffee_table": Table(b, w, h, d, primary); break;
            case "sofa":         Sofa(b, w, h, d, primary); break;
            case "bed":          Bed(b, w, h, d, primary); break;
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
