namespace SpatialAI.Api.Blueprint;

// ── Vision extraction DTOs ───────────────────────────────────────────────────
// The vision model fills these in (all coordinates in meters, derived from the printed dimension chains).
// A C# BuildingReconstructor (Phase 2) maps a BuildingPlan onto the existing SceneTools.

/// <summary>How a single uploaded image was classified by the vision pass.</summary>
public sealed record PlanClassification(
    int Index,
    string Kind,        // "floorplan" | "elevation" | "section" | "other"
    int LevelGuess,     // for floorplans: -1 basement, 0 ground, 1 first… (else 0)
    string Label);      // short human label, e.g. "Erdgeschoss" / "Ansicht Nord"

/// <summary>A window or door read from a floor plan, on one wall of a room.</summary>
public sealed record PlanOpening(
    string Wall,        // "north" | "south" | "east" | "west"
    string Type,        // "window" | "door"
    float Offset,       // along the wall from its center (m)
    float Width,
    float Height);

/// <summary>A furniture/fixture symbol read from a floor plan, mapped to a catalog kind.</summary>
public sealed record PlanFurniture(
    string Kind,        // catalog kind, e.g. "bed", "sofa", "toilet", "kitchen_counter"
    float X,
    float Z,
    float RotationY,
    float? Width,
    float? Depth);

/// <summary>A staircase symbol (bridges storeys).</summary>
public sealed record PlanStair(float X, float Z, float RotationY);

/// <summary>One room on a floor plan, as a rectangle in meters with its openings + furniture.</summary>
public sealed record PlanRoom(
    string Name,
    float CenterX,
    float CenterZ,
    float Width,
    float Depth,
    List<PlanOpening>? Openings,
    List<PlanFurniture>? Furniture);

/// <summary>One storey: its rooms, plus the storey elevation/height in meters.</summary>
public sealed record FloorPlan(
    int Level,
    string Name,
    float Elevation,
    float Height,
    List<PlanRoom>? Rooms,
    List<PlanStair>? Stairs,
    float ExternalWidth = 0,   // building's overall external width (m), from the outer dimension chain
    float ExternalDepth = 0,   // building's overall external depth (m)
    bool InRoof = false,       // attic storey enclosed by the roof (no full masonry walls)
    string? EntranceRoom = null,  // ground floor: name of the room the FRONT DOOR opens into (Windfang/Eingang…)
    string? EntranceWall = null); // the exterior wall (north|south|east|west) that front door is on

/// <summary>Roof read from the elevation/section drawings (exact ▽ height markers in meters).</summary>
public sealed record RoofInfo(
    string Style,         // "flat" | "gable" | "hip" | "mansard"
    float Height,         // rise above the eave = ridge − eave (m)
    int Dormers = 0,      // dormers on one main slope
    float Eave = 0,       // Y where the roof starts (top of the masonry wall)
    float Ridge = 0,      // Y of the roof top
    float Break = 0);     // mansard kink Y (steep→shallow); 0 = auto

/// <summary>An exterior/site object (tree, car, terrace, garage…) placed around the building.</summary>
public sealed record SiteItem(string Kind, float X, float Z, float RotationY);

/// <summary>The full reconstructed building, merged from all the plan images.</summary>
public sealed record BuildingPlan(
    List<FloorPlan> Floors,
    RoofInfo? Roof,
    List<SiteItem>? Site,
    float Width,         // overall footprint (m)
    float Depth);
