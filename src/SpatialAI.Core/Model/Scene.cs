using System.Text.Json.Serialization;

namespace SpatialAI.Core.Model;

/// <summary>Primitive shapes an item can be rendered as.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Shape
{
    Box,
    Cylinder,
    Sphere
}

/// <summary>A point or size in 3D space, meters. Y is up; ground is Y = 0.</summary>
public readonly record struct Vec3(float X, float Y, float Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
}

/// <summary>An RGBA color, components in 0..1.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1f)
{
    public static Rgba Gray => new(0.8f, 0.8f, 0.8f);
}

/// <summary>
/// One primitive piece of a composite item (e.g. a chair leg). Offset is relative to the item's
/// bounding-box center; size/rotation/color are the piece's own.
/// </summary>
public sealed class Part
{
    public Shape Shape { get; set; } = Shape.Box;
    public Vec3 Offset { get; set; }
    public Vec3 Size { get; set; } = new(0.5f, 0.5f, 0.5f);
    public float RotationY { get; set; }
    public Rgba Color { get; set; } = Rgba.Gray;
}

/// <summary>A free-standing object placed in the scene (chair, table, box, ...).</summary>
public sealed class Item
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Item";
    public Shape Shape { get; set; } = Shape.Box;

    /// <summary>Center position. Y is the vertical center of the object.</summary>
    public Vec3 Position { get; set; }

    /// <summary>Overall bounding dimensions [width(X), height(Y), depth(Z)] in meters (used by analyzers).</summary>
    public Vec3 Size { get; set; } = new(0.5f, 0.5f, 0.5f);

    /// <summary>Rotation around the vertical (Y) axis, in degrees.</summary>
    public float RotationY { get; set; }

    /// <summary>Primary color (per-part colors live on <see cref="Parts"/>).</summary>
    public Rgba Color { get; set; } = Rgba.Gray;

    /// <summary>
    /// The primitive parts this item is rendered from (center-relative offsets). Always populated —
    /// a plain box/cylinder/sphere is a single part.
    /// </summary>
    public List<Part> Parts { get; set; } = [];

    /// <summary>Catalog kind (e.g. "chair", "desk") if built from the furniture catalog; else null.</summary>
    public string? Kind { get; set; }

    /// <summary>Optional id of the room this item belongs to.</summary>
    public Guid? RoomId { get; set; }

    /// <summary>Storey index this item belongs to (0 = ground). Used for per-floor viewing/filtering.</summary>
    public int Level { get; set; }

    /// <summary>Optional id of the <see cref="Group"/> this item belongs to (scene-graph hierarchy).</summary>
    public Guid? GroupId { get; set; }

    /// <summary>
    /// Instancing tag. Items that share an instance key are geometrically identical repeats (a fence
    /// post, a roof tile, a tree from one batch) and can be drawn with a single GPU instanced mesh.
    /// </summary>
    public string? InstanceKey { get; set; }
}

/// <summary>
/// A named node in the scene graph used to organize items into regions (House / Garden / Boundary…).
/// Groups can nest via <see cref="ParentId"/>, enabling whole-region operations and scoped context.
/// </summary>
public sealed class Group
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Group";

    /// <summary>Optional parent group id for nesting; null for a top-level group.</summary>
    public Guid? ParentId { get; set; }
}

/// <summary>A window or door cut into one of a room's walls.</summary>
public sealed class Opening
{
    /// <summary>Which wall: "north" (+Z), "south" (-Z), "east" (+X), "west" (-X).</summary>
    public string Wall { get; set; } = "north";
    /// <summary>"window" or "door".</summary>
    public string Type { get; set; } = "window";
    /// <summary>Offset along the wall from its center, meters (0 = centered).</summary>
    public float Offset { get; set; }
    public float Width { get; set; } = 1.2f;
    public float Height { get; set; } = 1.2f;
    /// <summary>Height of the opening's bottom above the floor (0 for doors).</summary>
    public float Sill { get; set; } = 0.9f;
}

/// <summary>An interior partition wall spanning the room along an axis, with an optional centered doorway.</summary>
public sealed class Partition
{
    /// <summary>"x" = wall runs along X at a fixed Z; "z" = runs along Z at a fixed X.</summary>
    public string Axis { get; set; } = "x";
    /// <summary>The fixed coordinate of the wall (Z for an "x" partition, X for a "z" partition).</summary>
    public float Position { get; set; }
    /// <summary>Width of a centered doorway through the partition (0 = solid wall).</summary>
    public float DoorWidth { get; set; }
}

/// <summary>A rectangular room with a floor and four walls (rendered from its bounds).</summary>
public sealed class Room
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Room";

    /// <summary>Floor-plane center [X, 0, Z]. The storey's vertical position is <see cref="Elevation"/>.</summary>
    public Vec3 Center { get; set; }
    public float Width { get; set; } = 4f;   // along X
    public float Depth { get; set; } = 4f;   // along Z
    public float Height { get; set; } = 2.5f;

    /// <summary>Base Y of this room's floor slab (the storey elevation). 0 = ground floor.</summary>
    public float Elevation { get; set; }

    /// <summary>Storey index (0 = ground, 1 = first, -1 = basement). Groups rooms into floors.</summary>
    public int Level { get; set; }

    /// <summary>Attic storey enclosed by the building roof — rendered without full masonry walls.</summary>
    public bool InRoof { get; set; }

    public Rgba FloorColor { get; set; } = new(0.5f, 0.5f, 0.5f);

    /// <summary>Windows and doors cut into the walls.</summary>
    public List<Opening> Openings { get; set; } = [];

    /// <summary>Close the top of the room with a ceiling slab.</summary>
    public bool Ceiling { get; set; }

    /// <summary>Exterior roof style: "none", "flat", or "gable".</summary>
    public string Roof { get; set; } = "none";

    /// <summary>Interior partition walls.</summary>
    public List<Partition> Partitions { get; set; } = [];
}

/// <summary>
/// A building-wide roof spanning the whole footprint (independent of per-room <see cref="Room.Roof"/>).
/// Style: "flat" | "gable" | "hip" | "mansard". Rendered parametrically by the viewer.
/// </summary>
public sealed class BuildingRoof
{
    public string Style { get; set; } = "gable";
    /// <summary>Footprint bounds in meters.</summary>
    public float MinX { get; set; }
    public float MinZ { get; set; }
    public float MaxX { get; set; }
    public float MaxZ { get; set; }
    /// <summary>Y where the roof starts (the eave — top of the masonry wall).</summary>
    public float BaseY { get; set; }
    /// <summary>Total roof rise above <see cref="BaseY"/> (ridge − eave).</summary>
    public float Height { get; set; } = 2.5f;
    /// <summary>For a mansard: Y of the kink between the steep lower and shallow upper slopes (0 = auto).</summary>
    public float BreakY { get; set; }
    /// <summary>Number of dormers to render on the roof (0 = viewer picks a default for mansards).</summary>
    public int Dormers { get; set; }
}

/// <summary>A transient visual marker (e.g. an unused-area suggestion) the viewer can render.</summary>
public sealed class Highlight
{
    public Vec3 Position { get; set; }
    public Vec3 Size { get; set; }
    public Rgba Color { get; set; } = new(0.37f, 0.92f, 0.83f, 0.35f);
    public string? Label { get; set; }
}

/// <summary>The full scene: rooms, items, groups, and transient highlights.</summary>
public sealed class Scene
{
    public List<Room> Rooms { get; init; } = [];
    public List<Item> Items { get; init; } = [];

    /// <summary>Scene-graph groups (House / Garden / Boundary…) that items reference via <see cref="Item.GroupId"/>.</summary>
    public List<Group> Groups { get; init; } = [];

    /// <summary>Optional building-wide roof spanning the whole footprint (null = none).</summary>
    public BuildingRoof? Roof { get; set; }

    public List<Highlight> Highlights { get; init; } = [];
}
