using SpatialAI.Core.Model;

namespace SpatialAI.Core.Furniture;

/// <summary>
/// The built-in catalog dataset. This is the ONE in-code definition of every kind's metadata (size,
/// color, aliases, category, description); it seeds the catalog database on first run and is the default
/// catalog when no database is wired (unit tests, offline). Geometry for each kind lives in
/// <see cref="FurnitureFactory"/>, keyed by the same kind name.
/// </summary>
public static class CatalogSeed
{
    public static Catalog Default() => new(Entries);

    private static CatalogEntry E(string kind, string category, float w, float h, float d, Rgba color,
        string[]? aliases = null, string description = "") =>
        new(kind, category, w, h, d, color, aliases ?? [], description);

    public static readonly IReadOnlyList<CatalogEntry> Entries =
    [
        // ── Seating ──────────────────────────────────────────────────────────
        E("chair",        "Seating", 0.50f, 0.90f, 0.50f, new(0.30f, 0.45f, 0.65f)),
        E("dining_chair", "Seating", 0.48f, 0.92f, 0.50f, new(0.55f, 0.40f, 0.30f), description: "4-leg"),
        E("office_chair", "Seating", 0.62f, 1.10f, 0.62f, new(0.18f, 0.20f, 0.24f),
            ["gaming_chair", "task_chair", "swivel_chair", "desk_chair"], "wheeled swivel"),
        E("armchair",     "Seating", 0.85f, 0.85f, 0.88f, new(0.40f, 0.46f, 0.54f),
            ["recliner", "lounge_chair", "easy_chair"], "upholstered lounge"),
        E("stool",        "Seating", 0.40f, 0.60f, 0.40f, Palette.Wood, ["bar_stool", "barstool"]),
        E("bench",        "Seating", 1.30f, 0.45f, 0.40f, Palette.Wood),

        // ── Surfaces (tables / desks) ─────────────────────────────────────────
        E("desk",          "Surfaces", 1.40f, 0.75f, 0.70f, Palette.Wood, ["writing_desk"], "writing, drawers"),
        E("computer_desk", "Surfaces", 1.40f, 0.75f, 0.70f, Palette.Wood,
            ["pc_desk", "gaming_desk", "computerdesk"], "monitor shelf"),
        E("table",         "Surfaces", 1.20f, 0.75f, 0.80f, Palette.Wood),
        E("dining_table",  "Surfaces", 1.80f, 0.75f, 0.95f, Palette.Wood, ["diningtable", "dinner_table"]),
        E("coffee_table",  "Surfaces", 1.00f, 0.45f, 0.55f, Palette.Wood, ["coffeetable"]),
        E("nightstand",    "Surfaces", 0.45f, 0.50f, 0.40f, Palette.Wood,
            ["sidetable", "side_table", "bedside", "bedside_table"]),

        // ── Sleeping ──────────────────────────────────────────────────────────
        E("bed",         "Sleeping", 1.60f, 0.95f, 2.05f, new(0.55f, 0.40f, 0.30f)),
        E("single_bed",  "Sleeping", 0.95f, 0.95f, 2.05f, new(0.55f, 0.40f, 0.30f), ["twin_bed"]),
        E("double_bed",  "Sleeping", 1.60f, 0.95f, 2.05f, new(0.55f, 0.40f, 0.30f), ["queen_bed"]),
        E("king_bed",    "Sleeping", 1.95f, 0.95f, 2.10f, new(0.55f, 0.40f, 0.30f), ["king"]),
        E("bunk_bed",    "Sleeping", 1.00f, 1.70f, 2.05f, new(0.55f, 0.40f, 0.30f), ["bunk", "bunkbed"], "stacked"),

        // ── Sofas ───────────────────────────────────────────────────────────
        E("sofa",           "Sofas", 1.85f, 0.80f, 0.90f, new(0.42f, 0.48f, 0.55f), ["couch", "settee"]),
        E("loveseat",       "Sofas", 1.35f, 0.80f, 0.90f, new(0.42f, 0.48f, 0.55f), ["two_seater"], "2-seat"),
        E("sectional_sofa", "Sofas", 2.60f, 0.80f, 1.70f, new(0.42f, 0.48f, 0.55f),
            ["sectional", "l_sofa", "corner_sofa"], "L-shaped"),

        // ── Storage ─────────────────────────────────────────────────────────
        E("wardrobe",         "Storage", 1.20f, 2.00f, 0.60f, Palette.Wood, ["closet"]),
        E("chest_of_drawers", "Storage", 0.90f, 1.10f, 0.50f, Palette.Wood, ["dresser", "drawers", "chest", "tallboy"]),
        E("sideboard",        "Storage", 1.60f, 0.80f, 0.45f, Palette.Wood, ["credenza", "buffet", "cabinet"]),
        E("bookshelf",        "Storage", 0.90f, 1.80f, 0.30f, Palette.Wood, ["bookcase", "shelf", "shelves"]),

        // ── Lighting ────────────────────────────────────────────────────────
        E("floor_lamp",    "Lighting", 0.36f, 1.60f, 0.36f, new(0.95f, 0.90f, 0.70f), ["lamp", "floorlamp", "standing_lamp"]),
        E("table_lamp",    "Lighting", 0.30f, 0.50f, 0.30f, new(0.95f, 0.90f, 0.70f), ["tablelamp"]),
        E("desk_lamp",     "Lighting", 0.40f, 0.55f, 0.20f, Palette.MetalDark, description: "articulated"),
        E("ceiling_light", "Lighting", 0.40f, 0.55f, 0.40f, new(0.95f, 0.90f, 0.70f),
            ["pendant", "pendant_light", "ceiling_lamp", "hanging_light"], "pendant"),
        E("chandelier",    "Lighting", 0.70f, 0.70f, 0.70f, new(0.92f, 0.86f, 0.55f)),

        // ── Display & decor ───────────────────────────────────────────────────
        E("monitor", "Display & decor", 0.60f, 0.50f, 0.18f, Palette.MetalDark, ["screen", "display"]),
        E("tv",      "Display & decor", 1.20f, 0.78f, 0.14f, Palette.MetalDark, ["television", "tv_unit", "tvstand", "tv_stand"]),
        E("rug",     "Display & decor", 2.00f, 0.03f, 1.40f, new(0.72f, 0.32f, 0.30f), ["carpet"]),
        E("plant",   "Display & decor", 0.50f, 1.05f, 0.50f, Palette.Foliage, ["pottedplant", "potted_plant", "pot_plant", "houseplant"]),
        E("mirror",  "Display & decor", 0.60f, 1.60f, 0.05f, Palette.Wood),

        // ── Kitchen ───────────────────────────────────────────────────────────
        E("kitchen_counter", "Kitchen", 2.00f, 0.90f, 0.60f, Palette.Wood, ["counter", "kitchen", "countertop"]),
        E("kitchen_island",  "Kitchen", 1.40f, 0.90f, 0.90f, Palette.Wood, ["island"]),
        E("sink",            "Kitchen", 0.60f, 0.90f, 0.60f, Palette.Steel),
        E("stove",           "Kitchen", 0.60f, 0.90f, 0.60f, Palette.SteelDark, ["oven", "cooker", "range"]),
        E("fridge",          "Kitchen", 0.70f, 1.80f, 0.70f, Palette.Steel, ["refrigerator"]),
        E("dishwasher",      "Kitchen", 0.60f, 0.85f, 0.60f, Palette.Steel),

        // ── Bathroom ──────────────────────────────────────────────────────────
        E("toilet",  "Bathroom", 0.40f, 0.78f, 0.70f, Palette.Ceramic, ["wc"]),
        E("bathtub", "Bathroom", 1.70f, 0.58f, 0.75f, Palette.Ceramic, ["bath", "tub"]),
        E("basin",   "Bathroom", 0.55f, 0.85f, 0.45f, Palette.Ceramic, ["washbasin", "washstand"]),
        E("shower",  "Bathroom", 0.90f, 2.00f, 0.90f, Palette.Steel),

        // ── Appliances & heating ──────────────────────────────────────────────
        E("radiator",        "Appliances & heating", 0.80f, 0.60f, 0.10f, Palette.White, ["heater"]),
        E("fireplace",       "Appliances & heating", 1.30f, 1.15f, 0.40f, Palette.Stone),
        E("ac_unit",         "Appliances & heating", 0.40f, 0.70f, 0.40f, Palette.White, ["ac", "air_conditioner"]),
        E("washing_machine", "Appliances & heating", 0.60f, 0.85f, 0.60f, Palette.White, ["washer"]),

        // ── Structural ──────────────────────────────────────────────────────
        E("column",    "Structural", 0.32f, 2.50f, 0.32f, Palette.White, ["pillar"]),
        E("railing",   "Structural", 1.60f, 1.00f, 0.10f, Palette.Wood, ["balustrade", "banister"]),
        E("staircase", "Structural", 1.00f, 2.50f, 3.00f, Palette.Wood, ["stairs", "stair"]),

        // ── Tableware ─────────────────────────────────────────────────────────
        E("plate",  "Tableware", 0.24f, 0.04f, 0.24f, Palette.Ceramic, ["dish"]),
        E("cup",    "Tableware", 0.08f, 0.10f, 0.08f, new(0.90f, 0.90f, 0.92f), ["mug"]),
        E("bowl",   "Tableware", 0.18f, 0.08f, 0.18f, Palette.Ceramic),
        E("book",   "Tableware", 0.16f, 0.04f, 0.22f, new(0.50f, 0.30f, 0.30f), ["notebook"]),
        E("vase",   "Tableware", 0.14f, 0.30f, 0.14f, new(0.40f, 0.55f, 0.65f), ["flowerpot"]),
        E("laptop", "Tableware", 0.33f, 0.22f, 0.23f, Palette.SteelDark),

        // ── Outdoor & site ─────────────────────────────────────────────────────
        E("tree",    "Outdoor & site", 2.20f, 4.50f, 2.20f, Palette.Foliage),
        E("bush",    "Outdoor & site", 0.90f, 0.80f, 0.90f, Palette.Foliage, ["shrub", "bushes"]),
        E("hedge",   "Outdoor & site", 2.00f, 1.00f, 0.50f, Palette.FoliageDark, ["hedgerow"]),
        E("lawn",    "Outdoor & site", 4.00f, 0.04f, 4.00f, new(0.34f, 0.52f, 0.30f), ["grass"]),
        E("fence",   "Outdoor & site", 2.00f, 1.00f, 0.10f, Palette.WoodDark),
        E("gate",    "Outdoor & site", 1.20f, 1.30f, 0.10f, Palette.WoodDark),
        E("car",     "Outdoor & site", 1.90f, 1.50f, 4.40f, new(0.16f, 0.19f, 0.24f), ["vehicle", "auto", "automobile"]),
        E("terrace", "Outdoor & site", 4.00f, 0.16f, 3.00f, Palette.Wood, ["deck", "patio"]),
        E("garage",  "Outdoor & site", 3.20f, 2.60f, 5.60f, Palette.Stone),
        E("steps",   "Outdoor & site", 1.60f, 0.66f, 1.20f, Palette.Stone, ["stoop", "entrance_steps", "stairs_outside", "porch"]),

        // ── Industrial — warehouse / logistics ─────────────────────────────────
        E("pallet_rack",     "Industrial — warehouse", 2.70f, 3.00f, 1.10f, new(0.20f, 0.32f, 0.55f),
            ["rack", "racking", "warehouse_rack", "storage_rack"]),
        E("cantilever_rack", "Industrial — warehouse", 2.40f, 2.50f, 1.00f, new(0.55f, 0.20f, 0.20f)),
        E("shelving_unit",   "Industrial — warehouse", 1.00f, 2.00f, 0.50f, Palette.SteelDark,
            ["industrial_shelf", "steel_shelf", "shelving"]),
        E("pallet",          "Industrial — warehouse", 1.20f, 0.14f, 1.00f, Palette.Wood),
        E("pallet_jack",     "Industrial — warehouse", 0.55f, 1.20f, 1.60f, Palette.SafetyOrange,
            ["hand_pallet_truck", "pump_truck"]),
        E("forklift",        "Industrial — warehouse", 1.10f, 2.10f, 2.40f, Palette.SafetyYellow,
            ["lift_truck", "fork_truck", "fork_lift"]),
        E("stacker",         "Industrial — warehouse", 0.90f, 2.60f, 1.80f, Palette.SafetyYellow, ["reach_truck"]),
        E("crate",           "Industrial — warehouse", 0.90f, 0.80f, 0.90f, Palette.Wood),
        E("drum",            "Industrial — warehouse", 0.58f, 0.88f, 0.58f, Palette.SafetyOrange, ["barrel"]),
        E("bollard",         "Industrial — warehouse", 0.22f, 0.95f, 0.22f, Palette.SafetyYellow),
        E("safety_barrier",  "Industrial — warehouse", 1.50f, 1.10f, 0.12f, Palette.SafetyYellow, ["guard_rail", "safety_rail"]),

        // ── Industrial — manufacturing ─────────────────────────────────────────
        E("conveyor",      "Industrial — manufacturing", 2.40f, 0.85f, 0.70f, Palette.SteelDark, ["conveyor_belt", "belt_conveyor"]),
        E("cnc_machine",   "Industrial — manufacturing", 1.80f, 2.00f, 1.40f, Palette.Steel, ["machine", "cnc", "milling_machine"]),
        E("press",         "Industrial — manufacturing", 1.30f, 2.40f, 1.20f, Palette.SteelDark, ["stamping_press", "hydraulic_press"]),
        E("robot_arm",     "Industrial — manufacturing", 0.90f, 1.70f, 0.90f, Palette.SafetyOrange, ["industrial_robot", "robot", "robotic_arm"]),
        E("workbench",     "Industrial — manufacturing", 1.80f, 1.50f, 0.70f, Palette.SteelDark),
        E("control_panel", "Industrial — manufacturing", 0.90f, 2.00f, 0.50f, new(0.62f, 0.64f, 0.40f),
            ["electrical_cabinet", "control_cabinet", "control_box"]),
        E("agv",           "Industrial — manufacturing", 0.90f, 0.40f, 1.30f, Palette.SafetyYellow, ["automated_guided_vehicle", "agv_cart"]),
    ];
}
