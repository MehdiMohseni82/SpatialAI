using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SpatialAI.Mcp;

/// <summary>One primitive piece for <c>compose_item</c> (offset relative to footprint centre, Y up from floor).</summary>
public sealed record ComposePart(
    [property: Description("box | cylinder | sphere")] string Shape,
    float OffsetX, float OffsetY, float OffsetZ,
    float SizeX, float SizeY, float SizeZ,
    float ColorR, float ColorG, float ColorB,
    [property: Description("Optional Y rotation (deg)")] float? RotationY = null);

/// <summary>
/// MCP tools for building and editing the 3D scene. Each forwards to the SpatialAI API, so calls from
/// any MCP client appear live in the viewer. The same operations are also exposed to the in-app LLM
/// as function calls — one implementation, three surfaces.
/// </summary>
[McpServerToolType]
public sealed class SceneTools(SpatialApiClient api)
{
    [McpServerTool(Name = "create_room"), Description("Create a rectangular room with a floor and walls; optionally windows, doors, a ceiling and a roof.")]
    public Task<string> CreateRoom(
        [Description("Room name")] string name,
        [Description("Width along X (m)")] float width = 4f,
        [Description("Depth along Z (m)")] float depth = 4f,
        [Description("Center X (m)")] float centerX = 0f,
        [Description("Center Z (m)")] float centerZ = 0f,
        [Description("Wall height (m)")] float height = 2.5f,
        [Description("Number of windows to auto-place")] int windows = 0,
        [Description("Number of doors to auto-place")] int doors = 0,
        [Description("Close the top with a ceiling")] bool ceiling = false,
        [Description("Roof style: none | flat | gable")] string roof = "none",
        [Description("Base Y of this storey's floor (m); 0 = ground, e.g. 3 for the first floor")] float elevation = 0f,
        [Description("Storey index: 0 = ground, 1 = first floor, -1 = basement")] int level = 0)
        => api.InvokeAsync("create_room", new { name, width, depth, centerX, centerZ, height, windows, doors, ceiling, roof, elevation, level });

    [McpServerTool(Name = "add_window"), Description("Add a window to a specific wall (north/south/east/west) of a room.")]
    public Task<string> AddWindow(
        [Description("Wall: north | south | east | west")] string wall,
        [Description("Optional target room")] string? roomName = null,
        [Description("Offset along the wall from center (m)")] float? offset = null,
        [Description("Window width (m)")] float? width = null,
        [Description("Window height (m)")] float? height = null,
        [Description("Sill height above floor (m)")] float? sill = null)
        => api.InvokeAsync("add_window", new { roomName, wall, offset, width, height, sill });

    [McpServerTool(Name = "add_door"), Description("Add a door (open doorway) to a specific wall of a room.")]
    public Task<string> AddDoor(
        [Description("Wall: north | south | east | west")] string wall,
        [Description("Optional target room")] string? roomName = null,
        [Description("Offset along the wall from center (m)")] float? offset = null,
        [Description("Door width (m)")] float? width = null,
        [Description("Door height (m)")] float? height = null)
        => api.InvokeAsync("add_door", new { roomName, wall, offset, width, height });

    [McpServerTool(Name = "add_partition"), Description("Add an interior partition wall that divides a room, with an optional doorway.")]
    public Task<string> AddPartition(
        [Description("'x' = wall runs along X at a fixed Z; 'z' = runs along Z at a fixed X")] string axis,
        [Description("The fixed coordinate of the wall")] float position,
        [Description("Optional target room")] string? roomName = null,
        [Description("Width of a centered doorway (0 = solid)")] float? doorWidth = null)
        => api.InvokeAsync("add_partition", new { roomName, axis, position, doorWidth });

    [McpServerTool(Name = "set_ceiling"), Description("Add or remove a room's ceiling.")]
    public Task<string> SetCeiling(
        [Description("true to add, false to remove")] bool on = true,
        [Description("Optional target room")] string? roomName = null)
        => api.InvokeAsync("set_ceiling", new { roomName, on });

    [McpServerTool(Name = "set_roof"), Description("Set a room's exterior roof style: none | flat | gable.")]
    public Task<string> SetRoof(
        [Description("none | flat | gable")] string style,
        [Description("Optional target room")] string? roomName = null)
        => api.InvokeAsync("set_roof", new { roomName, style });

    [McpServerTool(Name = "set_building_roof"), Description("Put one roof over the WHOLE building (all rooms), spanning the full footprint. Style: none | flat | gable | hip | mansard.")]
    public Task<string> SetBuildingRoof(
        [Description("none | flat | gable | hip | mansard")] string style,
        [Description("Optional roof rise above the walls (m)")] float? height = null,
        [Description("Optional number of dormers on the roof")] int dormers = 0)
        => api.InvokeAsync("set_building_roof", new { style, height, dormers });

    [McpServerTool(Name = "create_item"), Description("Create a furniture item assembled from a detailed multi-part model. Omit size for realistic defaults; omit position to auto-place in free space.")]
    public Task<string> CreateItem(
        [Description("Item name, e.g. 'Chair'")] string name,
        [Description("Kind. Furniture: chair, stool, desk, table, coffee_table, sofa, bed, nightstand, wardrobe, bookshelf, floor_lamp, table_lamp, monitor, tv, rug, plant, bench, mirror. Kitchen: kitchen_counter, kitchen_island, sink, stove, fridge, dishwasher. Bathroom: toilet, bathtub, basin, shower. Appliances: radiator, fireplace, ac_unit, washing_machine. Structural: column, railing, staircase. Outdoor/site: tree, bush, hedge, lawn, fence, gate, car, terrace, garage, steps. Or box/cylinder/sphere.")] string kind = "box",
        [Description("Optional overall width X (m)")] float? width = null,
        [Description("Optional overall height Y (m)")] float? height = null,
        [Description("Optional overall depth Z (m)")] float? depth = null,
        [Description("Red 0..1 (tints the main material)")] float? colorR = null,
        [Description("Green 0..1")] float? colorG = null,
        [Description("Blue 0..1")] float? colorB = null,
        [Description("Optional X (m)")] float? positionX = null,
        [Description("Optional Z (m)")] float? positionZ = null,
        [Description("Optional target room name")] string? roomName = null,
        [Description("Facing in degrees: 0=+Z, 90=+X, 180=-Z, -90=-X. Prefer faceItem over computing this by hand.")] float? rotationY = null,
        [Description("Place ON TOP of this named item's surface (height auto-computed)")] string? onItem = null,
        [Description("Turn this item to FACE the named item (e.g. a chair facing a desk). Preferred over rotationY.")] string? faceItem = null)
        => api.InvokeAsync("create_item", new { name, kind, width, height, depth, colorR, colorG, colorB, positionX, positionZ, roomName, rotationY, onItem, faceItem });

    [McpServerTool(Name = "arrange_on"), Description("Place several items on top of a target's surface (e.g. plates on a table).")]
    public Task<string> ArrangeOn(
        [Description("Name of the surface item, e.g. 'Table'")] string targetName,
        [Description("What to place (default 'plate')")] string kind = "plate",
        [Description("How many to place")] int count = 1,
        [Description("Optional spread radius on the surface (m)")] float? radius = null,
        [Description("Optional target room name")] string? roomName = null)
        => api.InvokeAsync("arrange_on", new { targetName, kind, count, radius, roomName });

    [McpServerTool(Name = "arrange_around"), Description("Place several items evenly around a target item (e.g. chairs around a table), each rotated to face it.")]
    public Task<string> ArrangeAround(
        [Description("Name of the item to surround, e.g. 'Desk'")] string targetName,
        [Description("What to place (default 'chair')")] string kind = "chair",
        [Description("How many to place")] int count = 4,
        [Description("Optional distance from the target center (m)")] float? radius = null,
        [Description("Optional target room name")] string? roomName = null)
        => api.InvokeAsync("arrange_around", new { targetName, kind, count, radius, roomName });

    [McpServerTool(Name = "compose_item"), Description("Build a custom object from explicit primitive parts (offsets relative to the footprint centre, Y up from the floor). Use for objects not covered by create_item kinds.")]
    public Task<string> ComposeItem(
        [Description("Item name")] string name,
        [Description("The primitive pieces that make up the object")] ComposePart[] parts,
        [Description("Optional X (m)")] float? positionX = null,
        [Description("Optional Z (m)")] float? positionZ = null,
        [Description("Optional target room name")] string? roomName = null,
        [Description("Place ON TOP of this named item's surface (height auto-computed)")] string? onItem = null,
        [Description("Turn this item to FACE the named item")] string? faceItem = null)
        => api.InvokeAsync("compose_item", new { name, parts, positionX, positionZ, roomName, onItem, faceItem });

    [McpServerTool(Name = "move_item"), Description("Move an item to a new floor position.")]
    public Task<string> MoveItem(string itemName, float positionX, float positionZ)
        => api.InvokeAsync("move_item", new { itemName, positionX, positionZ });

    [McpServerTool(Name = "rotate_item"), Description("Rotate an item around the vertical axis by N degrees.")]
    public Task<string> RotateItem(string itemName, float degrees)
        => api.InvokeAsync("rotate_item", new { itemName, degrees });

    [McpServerTool(Name = "scale_item"), Description("Scale an item uniformly by a factor.")]
    public Task<string> ScaleItem(string itemName, float factor)
        => api.InvokeAsync("scale_item", new { itemName, factor });

    [McpServerTool(Name = "recolor_item"), Description("Change an item's color (RGB 0..1).")]
    public Task<string> RecolorItem(string itemName, float colorR, float colorG, float colorB)
        => api.InvokeAsync("recolor_item", new { itemName, colorR, colorG, colorB });

    [McpServerTool(Name = "delete_item"), Description("Remove an item from the scene.")]
    public Task<string> DeleteItem(string itemName)
        => api.InvokeAsync("delete_item", new { itemName });

    [McpServerTool(Name = "list_scene"), Description("List the rooms and items currently in the scene.")]
    public Task<string> ListScene()
        => api.InvokeAsync("list_scene", new { });

    [McpServerTool(Name = "find_unused_areas"), Description("Find the largest unused floor areas in a room and highlight them.")]
    public Task<string> FindUnusedAreas([Description("Optional room name")] string? roomName = null)
        => api.InvokeAsync("find_unused_areas", new { roomName });

    [McpServerTool(Name = "analyze_ergonomics"), Description("Review the workspace for ergonomic issues relative to the user.")]
    public Task<string> AnalyzeErgonomics(
        [Description("Optional room name")] string? roomName = null,
        [Description("Optional user X")] float? userX = null,
        [Description("Optional user Z")] float? userZ = null)
        => api.InvokeAsync("analyze_ergonomics", new { roomName, userX, userZ });

    [McpServerTool(Name = "save_space"), Description("Save the current scene as a named space. Pass a name to save a new copy (Save As); omit it to save over the current space.")]
    public Task<string> SaveSpace([Description("Optional name to save as a new space")] string? name = null)
        => api.InvokeAsync("save_space", new { name });

    [McpServerTool(Name = "new_space"), Description("Start a fresh, empty space (clears the scene). Save it later to persist.")]
    public Task<string> NewSpace([Description("Name for the new space")] string name)
        => api.InvokeAsync("new_space", new { name });

    [McpServerTool(Name = "open_space"), Description("Open a previously saved space by name, replacing the current scene.")]
    public Task<string> OpenSpace([Description("Name of the saved space to open")] string name)
        => api.InvokeAsync("open_space", new { name });

    [McpServerTool(Name = "list_spaces"), Description("List the saved spaces by name.")]
    public Task<string> ListSpaces()
        => api.InvokeAsync("list_spaces", new { });
}
