using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using SpatialAI.Api.Spaces;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;

namespace SpatialAI.Api;

public sealed record ChatTurn(string Role, string Content);
public sealed record ChatRequest(string Message, List<ChatTurn>? History);
public sealed record ChatReply(string Assistant, List<string> Actions);

/// <summary>
/// Runs the Azure OpenAI function-calling loop. Every tool the model can call maps to a method on
/// <see cref="SceneTools"/>, which mutates the shared scene. The same SceneTools also backs the MCP server.
/// </summary>
public sealed class ChatEngine
{
    private readonly ChatClient? _chat;
    private readonly SceneTools _tools;
    private readonly SceneStore _store;
    private readonly SpaceManager _spaces;

    private const string SystemPrompt = """
        You are a spatial design assistant that builds and edits a simple 3D scene by calling tools.

        The current scene is provided as JSON on every turn — consult it before creating anything.
        Reference existing rooms, items, windows and doors by name; do not recreate what already
        exists; edit or extend existing elements rather than duplicating them.

        Coordinate system: meters. X = left/right, Z = forward/back, Y = up (floor at Y = 0).
        Always create a room before placing items; if none exists, create a sensible one first.

        Rooms can have building parts. Pass windows/doors counts (and ceiling/roof) to create_room for a
        one-shot build, or use add_window / add_door (per wall: north/south/east/west), add_partition
        (interior dividing wall, optional doorway), set_ceiling and set_roof (flat/gable) for control.

        To add a real-world object, call create_item with a `kind`. The system assembles a detailed,
        multi-part 3D model for that kind automatically — you only choose the kind and optionally
        size/colour. Supported kinds:
          chair, stool, desk, table, coffee_table, sofa, bed, nightstand, wardrobe, bookshelf,
          floor_lamp, table_lamp, monitor, tv, rug, plant.
        Omit width/height/depth to use realistic defaults; pass them only to override. Pick a fitting
        colour (RGB 0..1) — for furniture it tints the main material (e.g. sofa fabric, lamp shade).
        For a plain block use kind = box | cylinder | sphere with explicit width/height/depth.

        For an object NOT in the kind list above, call compose_item with an explicit list of primitive
        `parts` (boxes/cylinders/spheres). Each part's offset is relative to the object's footprint
        centre, with Y measured UP FROM THE FLOOR (a 0.4 m-tall leg sits at offsetY = 0.2). Keep it to
        a handful of parts.

        Surfaces: items rest on the floor by default. To put something ON another item (a lamp on a desk,
        a plate on a table), pass create_item's `onItem=<surface name>`; for several items on a surface
        (place settings on a table), use arrange_on(targetName, kind, count). Don't drop them on the floor.

        Orientation: to make one item face another (a chair facing a desk), pass create_item's
        `faceItem=<that item's name>` — do NOT compute rotationY yourself; the system turns it correctly.
        A single chair/seat with no orientation already auto-faces the nearest desk/table, so you can also
        just say nothing. Only use the raw rotationY angle for deliberate, non-facing rotations. To seat
        several chairs around a table/desk, prefer arrange_around(targetName, 'chair', count) — it positions
        AND rotates them to face the target automatically; don't place them one-by-one.

        Omit position to let the system auto-place items in free space. Keep replies short; after
        calling tools, briefly confirm what you did.

        Spaces: the whole scene can be saved, reopened and managed. Use save_space (optionally with a
        name to Save As), new_space to start fresh, open_space to reload a saved space by name, and
        list_spaces to see what exists.
        """;

    public ChatEngine(IConfiguration config, SceneTools tools, SceneStore store, SpaceManager spaces)
    {
        _tools = tools;
        _store = store;
        _spaces = spaces;
        var endpoint = config["OpenAI:AzureEndpoint"];
        var apiKey = config["OpenAI:ApiKey"];
        var deployment = config["OpenAI:ChatDeployment"] ?? "gpt-4o";
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
        {
            var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            _chat = client.GetChatClient(deployment);
        }
    }

    public bool IsConfigured => _chat is not null;

    public async Task<ChatReply> ChatAsync(ChatRequest request, CancellationToken ct)
    {
        if (_chat is null)
            return new ChatReply("Azure OpenAI is not configured. Set OpenAI:AzureEndpoint / OpenAI:ApiKey.", []);

        var messages = new List<OpenAI.Chat.ChatMessage> { new SystemChatMessage(SystemPrompt) };
        // Conversation context comes from the active space's transcript (per-space history), not the client.
        foreach (var turn in _spaces.CurrentChat())
        {
            if (turn.Kind == "ai") messages.Add(new AssistantChatMessage(turn.Text));
            else if (turn.Kind == "user") messages.Add(new UserChatMessage(turn.Text));
            // "tool" lines are UI-only and are not replayed to the model
        }
        messages.Add(BuildSceneContextMessage());
        messages.Add(new UserChatMessage(request.Message));

        var options = new ChatCompletionOptions();
        foreach (var tool in BuildTools()) options.Tools.Add(tool);

        var actions = new List<string>();
        var completion = (await _chat.CompleteChatAsync(messages, options, ct)).Value;

        while (completion.FinishReason == ChatFinishReason.ToolCalls)
        {
            messages.Add(new AssistantChatMessage(completion));
            foreach (var call in completion.ToolCalls)
            {
                var result = Dispatch(call.FunctionName, call.FunctionArguments);
                actions.Add(result);
                messages.Add(new ToolChatMessage(call.Id, result));
            }
            completion = (await _chat.CompleteChatAsync(messages, options, ct)).Value;
        }

        var text = string.Join("\n", completion.Content.Select(p => p.Text));

        // Record the turn on the active space so its conversation persists and stays isolated per space.
        var transcript = new List<Spaces.ChatMessage> { new("user", request.Message) };
        transcript.AddRange(actions.Select(a => new Spaces.ChatMessage("tool", a)));
        if (!string.IsNullOrEmpty(text)) transcript.Add(new("ai", text));
        _spaces.AppendChat(transcript);

        return new ChatReply(text, actions);
    }

    private SystemChatMessage BuildSceneContextMessage()
    {
        var current = _store.Current;
        var body = current.Rooms.Count == 0 && current.Items.Count == 0
            ? "(the scene is currently empty)"
            : SceneContext.ToJson(current);
        return new SystemChatMessage(
            "CURRENT SCENE (JSON, refreshed each turn). Use it: reference existing rooms/items by name, " +
            "do not recreate what already exists, and edit/extend existing rooms, openings and items " +
            "rather than duplicating them.\n" + body);
    }

    private string Dispatch(string name, BinaryData argsData)
    {
        var args = JsonDocument.Parse(argsData).RootElement;
        return SpaceTools.Handles(name)
            ? SpaceTools.Invoke(_spaces, name, args)
            : SceneToolRouter.Invoke(_tools, name, args);
    }

    private static List<ChatTool> BuildTools() =>
    [
        Tool("create_room", "Create a rectangular room with a floor and walls. Can include windows, doors, a ceiling and a roof.", new {
            type = "object",
            properties = new {
                name = new { type = "string", description = "Room name" },
                width = new { type = "number", description = "Width along X (m)" },
                depth = new { type = "number", description = "Depth along Z (m)" },
                centerX = new { type = "number", description = "Center X (m), default 0" },
                centerZ = new { type = "number", description = "Center Z (m), default 0" },
                height = new { type = "number", description = "Wall height (m), default 2.5" },
                windows = new { type = "integer", description = "Number of windows to auto-place on the walls" },
                doors = new { type = "integer", description = "Number of doors to auto-place" },
                ceiling = new { type = "boolean", description = "Close the top with a ceiling" },
                roof = new { type = "string", @enum = new[] { "none", "flat", "gable" }, description = "Exterior roof style" },
                elevation = new { type = "number", description = "Base Y of this storey's floor (m). 0 = ground; e.g. 3 for the first floor above it." },
                level = new { type = "integer", description = "Storey index (0 = ground, 1 = first floor, -1 = basement)" }
            },
            required = new[] { "name", "width", "depth" }
        }),
        Tool("add_window", "Add a window to a specific wall of a room.", new {
            type = "object",
            properties = new {
                roomName = new { type = "string", description = "Optional target room" },
                wall = new { type = "string", @enum = new[] { "north", "south", "east", "west" }, description = "Which wall" },
                offset = new { type = "number", description = "Offset along the wall from center (m)" },
                width = new { type = "number", description = "Window width (m), default 1.2" },
                height = new { type = "number", description = "Window height (m), default 1.2" },
                sill = new { type = "number", description = "Sill height above floor (m), default 0.9" }
            },
            required = new[] { "wall" }
        }),
        Tool("add_door", "Add a door (open doorway) to a specific wall of a room.", new {
            type = "object",
            properties = new {
                roomName = new { type = "string", description = "Optional target room" },
                wall = new { type = "string", @enum = new[] { "north", "south", "east", "west" }, description = "Which wall" },
                offset = new { type = "number", description = "Offset along the wall from center (m)" },
                width = new { type = "number", description = "Door width (m), default 0.9" },
                height = new { type = "number", description = "Door height (m), default 2.1" }
            },
            required = new[] { "wall" }
        }),
        Tool("add_partition", "Add an interior partition wall that divides a room, with an optional doorway.", new {
            type = "object",
            properties = new {
                roomName = new { type = "string", description = "Optional target room" },
                axis = new { type = "string", @enum = new[] { "x", "z" }, description = "'x' = wall runs along X at a fixed Z; 'z' = runs along Z at a fixed X" },
                position = new { type = "number", description = "The fixed coordinate of the wall (Z for axis x, X for axis z)" },
                doorWidth = new { type = "number", description = "Width of a centered doorway (0 = solid wall)" }
            },
            required = new[] { "axis", "position" }
        }),
        Tool("set_ceiling", "Add or remove a room's ceiling.", new {
            type = "object",
            properties = new {
                roomName = new { type = "string", description = "Optional target room" },
                on = new { type = "boolean", description = "true to add a ceiling, false to remove" }
            }
        }),
        Tool("set_roof", "Set a room's exterior roof style.", new {
            type = "object",
            properties = new {
                roomName = new { type = "string", description = "Optional target room" },
                style = new { type = "string", @enum = new[] { "none", "flat", "gable" } }
            },
            required = new[] { "style" }
        }),
        Tool("set_building_roof", "Put a single roof over the WHOLE building (all rooms), spanning the full footprint. Use for a multi-room/multi-storey house instead of per-room roofs. Pass 'none' to remove.", new {
            type = "object",
            properties = new {
                style = new { type = "string", @enum = new[] { "none", "flat", "gable", "hip", "mansard" }, description = "Building roof style" },
                height = new { type = "number", description = "Optional roof rise above the walls (m)" },
                dormers = new { type = "integer", description = "Optional number of dormers on the roof" }
            },
            required = new[] { "style" }
        }),
        Tool("create_item", "Create a furniture item built from a detailed multi-part model. Pick a kind; the system assembles it. Omit size for realistic defaults; omit position to auto-place in free space.", new {
            type = "object",
            properties = new {
                name = new { type = "string", description = "Item name, e.g. 'Chair'" },
                kind = new { type = "string", description = "Furniture: chair, stool, desk, table, coffee_table, sofa, bed, nightstand, wardrobe, bookshelf, floor_lamp, table_lamp, monitor, tv, rug, plant, bench, mirror. Kitchen: kitchen_counter, kitchen_island, sink, stove, fridge, dishwasher. Bathroom: toilet, bathtub, basin, shower. Appliances/heating: radiator, fireplace, ac_unit, washing_machine. Structural: column, railing, staircase. Tabletop: plate, cup, bowl, book, vase, laptop. Outdoor/site: tree, bush, hedge, lawn, fence, gate, car, terrace, garage, steps. Or box/cylinder/sphere for a plain block." },
                width = new { type = "number", description = "Optional overall width X (m)" },
                height = new { type = "number", description = "Optional overall height Y (m)" },
                depth = new { type = "number", description = "Optional overall depth Z (m)" },
                colorR = new { type = "number", description = "Red 0..1 (tints the main material)" },
                colorG = new { type = "number", description = "Green 0..1" },
                colorB = new { type = "number", description = "Blue 0..1" },
                positionX = new { type = "number", description = "Optional X (m)" },
                positionZ = new { type = "number", description = "Optional Z (m)" },
                rotationY = new { type = "number", description = "Raw facing in degrees: 0 faces +Z, 90 faces +X, 180 faces -Z, -90 faces -X. Only for deliberate non-facing rotations; to face another item use faceItem instead." },
                faceItem = new { type = "string", description = "Turn this item to FACE the named item (e.g. a chair facing a desk). Preferred over rotationY; the system computes the angle." },
                onItem = new { type = "string", description = "Place this item ON TOP of the named item's surface (e.g. a lamp on a desk). Height is computed automatically." },
                roomName = new { type = "string", description = "Optional target room" }
            },
            required = new[] { "name", "kind" }
        }),
        Tool("arrange_on", "Place several items on top of a target's surface (e.g. plates on a table). Prefer this for tableware/objects on tables and desks.", new {
            type = "object",
            properties = new {
                targetName = new { type = "string", description = "Name of the surface item (e.g. 'Table')" },
                kind = new { type = "string", description = "What to place (default 'plate'); e.g. plate, cup, bowl, book, vase, laptop" },
                count = new { type = "integer", description = "How many to place (default 1)" },
                radius = new { type = "number", description = "Optional spread radius on the surface (m)" },
                roomName = new { type = "string", description = "Optional target room" }
            },
            required = new[] { "targetName" }
        }),
        Tool("arrange_around", "Place several items evenly around a target item (e.g. chairs around a table), each automatically rotated to face it. Prefer this for seating around tables/desks.", new {
            type = "object",
            properties = new {
                targetName = new { type = "string", description = "Name of the item to surround (e.g. 'Desk')" },
                kind = new { type = "string", description = "What to place around it (default 'chair')" },
                count = new { type = "integer", description = "How many to place (default 4)" },
                radius = new { type = "number", description = "Optional distance from the target's center (m); omit to tuck up to its edge" },
                roomName = new { type = "string", description = "Optional target room" }
            },
            required = new[] { "targetName" }
        }),
        Tool("compose_item", "Build a custom object NOT covered by create_item's kinds, from explicit primitive parts. Each part offset is relative to the object's footprint centre, Y measured up from the floor.", new {
            type = "object",
            properties = new {
                name = new { type = "string", description = "Item name" },
                parts = new {
                    type = "array",
                    description = "The primitive pieces that make up the object",
                    items = new {
                        type = "object",
                        properties = new {
                            shape = new { type = "string", @enum = new[] { "box", "cylinder", "sphere" } },
                            offsetX = new { type = "number" }, offsetY = new { type = "number" }, offsetZ = new { type = "number" },
                            sizeX = new { type = "number" }, sizeY = new { type = "number" }, sizeZ = new { type = "number" },
                            rotationY = new { type = "number", description = "Optional Y rotation (deg)" },
                            colorR = new { type = "number" }, colorG = new { type = "number" }, colorB = new { type = "number" }
                        },
                        required = new[] { "shape", "offsetX", "offsetY", "offsetZ", "sizeX", "sizeY", "sizeZ", "colorR", "colorG", "colorB" }
                    }
                },
                positionX = new { type = "number", description = "Optional X (m)" },
                positionZ = new { type = "number", description = "Optional Z (m)" },
                onItem = new { type = "string", description = "Place this object ON TOP of the named item's surface; height computed automatically." },
                faceItem = new { type = "string", description = "Turn this object to FACE the named item." },
                roomName = new { type = "string", description = "Optional target room" }
            },
            required = new[] { "name", "parts" }
        }),
        Tool("move_item", "Move an item to a new floor position.", new {
            type = "object",
            properties = new {
                itemName = new { type = "string" },
                positionX = new { type = "number" },
                positionZ = new { type = "number" }
            },
            required = new[] { "itemName", "positionX", "positionZ" }
        }),
        Tool("rotate_item", "Rotate an item around the vertical axis by N degrees.", new {
            type = "object",
            properties = new { itemName = new { type = "string" }, degrees = new { type = "number" } },
            required = new[] { "itemName", "degrees" }
        }),
        Tool("scale_item", "Scale an item uniformly by a factor.", new {
            type = "object",
            properties = new { itemName = new { type = "string" }, factor = new { type = "number" } },
            required = new[] { "itemName", "factor" }
        }),
        Tool("recolor_item", "Change an item's color (RGB 0..1).", new {
            type = "object",
            properties = new { itemName = new { type = "string" }, colorR = new { type = "number" }, colorG = new { type = "number" }, colorB = new { type = "number" } },
            required = new[] { "itemName", "colorR", "colorG", "colorB" }
        }),
        Tool("delete_item", "Remove an item from the scene.", new {
            type = "object",
            properties = new { itemName = new { type = "string" } },
            required = new[] { "itemName" }
        }),
        Tool("list_scene", "List the rooms and items currently in the scene.", new { type = "object", properties = new { } }),
        Tool("find_unused_areas", "Find the largest unused floor areas in a room and highlight them.", new {
            type = "object", properties = new { roomName = new { type = "string", description = "Optional room name" } }
        }),
        Tool("analyze_ergonomics", "Review the workspace for ergonomic issues relative to the user.", new {
            type = "object",
            properties = new {
                roomName = new { type = "string", description = "Optional room name" },
                userX = new { type = "number", description = "Optional user X" },
                userZ = new { type = "number", description = "Optional user Z" }
            }
        }),
        Tool("save_space", "Save the current scene as a named space. Pass a name to save a new copy (Save As); omit it to save over the current space.", new {
            type = "object",
            properties = new { name = new { type = "string", description = "Optional name to save as a new space" } }
        }),
        Tool("new_space", "Start a fresh, empty space (clears the scene). Save it later to persist.", new {
            type = "object",
            properties = new { name = new { type = "string", description = "Name for the new space" } },
            required = new[] { "name" }
        }),
        Tool("open_space", "Open a previously saved space by name, replacing the current scene.", new {
            type = "object",
            properties = new { name = new { type = "string", description = "Name of the saved space to open" } },
            required = new[] { "name" }
        }),
        Tool("list_spaces", "List the saved spaces by name.", new { type = "object", properties = new { } })
    ];

    private static ChatTool Tool(string name, string description, object schema) =>
        ChatTool.CreateFunctionTool(name, description, BinaryData.FromObjectAsJson(schema));
}
