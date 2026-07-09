using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using SpatialAI.Api.Spaces;
using SpatialAI.Core.Analysis;
using SpatialAI.Core.Furniture;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;

namespace SpatialAI.Api;

public sealed record ChatTurn(string Role, string Content);
public sealed record ChatRequest(string Message, List<ChatTurn>? History);
public sealed record ChatReply(string Assistant, List<string> Actions, int? MessagesRemaining = null,
    bool BudgetExhausted = false, List<string>? Suggestions = null);

/// <summary>
/// Runs the Claude (Anthropic) tool-use loop. Every tool the model can call maps to a method on
/// <see cref="SceneTools"/>, which mutates the scene — the same SceneTools also backs the MCP server.
/// The tool schemas + system prompt are prompt-cached (stable prefix), the volatile scene JSON goes in
/// the user turn, and per-message token usage is metered for the per-user budget.
/// </summary>
public sealed class ChatEngine
{
    private readonly AnthropicClient? _client;
    private readonly string _model;
    private readonly BudgetStore _budget;
    private readonly List<ToolUnion> _toolDefs;
    private readonly bool _useLlmSuggestions;
    private readonly int _minBudgetForLlm;
    private const int MaxToolRounds = 12;   // hard loop cap (backstop for the token budget)
    private const int MaxOutputTokens = 1024;

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
        size/colour. Pick the most SPECIFIC kind — variants are distinct models, so don't fall back to a
        generic when a precise one exists. Supported kinds (grouped by category):
        {KINDS}
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

        Positioning: to place or move something relative to the room or another item, prefer the `anchor`
        argument (e.g. `corner`, `corner:nw`, `wall:north`, `near:Desk`, `left:Sofa`) over guessing raw
        coordinates. The system keeps the item fully inside the room and clear of other items, and turns
        wall/corner pieces to face into the room — so do NOT hand-pick coordinates for vague requests like
        "in the corner" or "against the wall". Use positionX/Z only when an exact coordinate is intended.
        Omit both to let the system auto-place items in free space. Keep replies short; after calling
        tools, briefly confirm what you did.

        Enclosures: to fence or wall around a room/yard, call enclose_room — it rings the perimeter with
        four thin segments (grouped). NEVER stretch a single fence across an area: a fence's depth is its
        thickness, so a large depth fills the whole footprint instead of ringing it.

        Grouping: relate items with create_group + add_to_group (e.g. a production line, a racking zone)
        so the whole set can be repositioned with move_group or removed with delete_group as one unit.

        Industrial layouts: to build a whole system in one step, prefer the layout tools over placing each
        item — create_warehouse (a full shell with dock doors), create_production_line (conveyor + machine
        stations) and create_rack_aisles (rows of racking). They auto-group their output so you can then
        move_group / delete_group the whole line or zone.

        Spaces: the whole scene can be saved, reopened and managed. Use save_space (optionally with a
        name to Save As), new_space to start fresh, open_space to reload a saved space by name, and
        list_spaces to see what exists.
        """;

    public ChatEngine(IConfiguration config, BudgetStore budget)
    {
        _budget = budget;
        _model = config["LLM:Model"] ?? "claude-haiku-4-5";
        var apiKey = config["LLM:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
            _client = new AnthropicClient { ApiKey = apiKey };
        _toolDefs = BuildTools().Select(t => new ToolUnion(t)).ToList();
        // Suggestions: deterministic engine always runs; the optional LLM refinement is on by default but
        // never spends a message credit and only fires while a user's budget is comfortably above this floor.
        _useLlmSuggestions = !string.Equals(config["Suggestions:UseLlm"], "false", StringComparison.OrdinalIgnoreCase);
        _minBudgetForLlm = int.TryParse(config["Suggestions:MinBudgetForLlm"], out var m) && m >= 0 ? m : 8;
    }

    public bool IsConfigured => _client is not null;

    /// <summary>
    /// One chat turn: enforce the caller's message budget, then run the Claude tool-use loop
    /// (prompt-cached tools+system) until the model stops calling tools or the loop cap is hit.
    /// </summary>
    public async Task<ChatReply> ChatAsync(ChatRequest request, string userId,
        SceneTools tools, SceneStore store, SpaceManager spaces, CancellationToken ct)
    {
        if (_client is null)
            return new ChatReply("Claude is not configured. Set ANTHROPIC_API_KEY (or LLM:ApiKey).", []);

        if (!_budget.TryConsume(userId, out var remaining))
            return new ChatReply(
                "You've used all your demo credits for this session. You can still build by hand — " +
                "click an item and use W/E/R to move/rotate/scale, or press Reset. The AI chat is paused.",
                [], MessagesRemaining: _budget.Remaining(userId), BudgetExhausted: true);

        // Stable, cacheable prefix: the kind list is generated from the catalog (one source of truth).
        var systemPrompt = SystemPrompt.Replace("{KINDS}", FurnitureFactory.DescribeKinds());
        var system = new List<TextBlockParam>
        {
            new() { Text = systemPrompt, CacheControl = new CacheControlEphemeral() },
        };

        // Conversation context comes from the active space's transcript (per-space history).
        var messages = new List<MessageParam>();
        foreach (var turn in spaces.CurrentChat())
        {
            if (turn.Kind == "ai") messages.Add(new MessageParam { Role = Role.Assistant, Content = turn.Text });
            else if (turn.Kind == "user") messages.Add(new MessageParam { Role = Role.User, Content = turn.Text });
            // "tool" lines are UI-only and are not replayed to the model
        }
        // Volatile scene JSON + the new user message go LAST, after the cached prefix.
        messages.Add(new MessageParam { Role = Role.User, Content = BuildSceneContext(store) + "\n\n" + request.Message });

        var actions = new List<string>();
        long inTok = 0, outTok = 0, cacheRead = 0;
        var text = "";

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var resp = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = MaxOutputTokens,
                System = system,
                Tools = _toolDefs,
                Messages = messages,
            }, cancellationToken: ct);

            inTok += resp.Usage.InputTokens;
            outTok += resp.Usage.OutputTokens;
            cacheRead += resp.Usage.CacheReadInputTokens ?? 0;

            var assistant = new List<ContentBlockParam>();
            var toolResults = new List<ContentBlockParam>();
            foreach (var block in resp.Content)
            {
                if (block.TryPickText(out TextBlock? t))
                {
                    text = t.Text;
                    assistant.Add(new TextBlockParam { Text = t.Text });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? tu))
                {
                    assistant.Add(new ToolUseBlockParam { ID = tu.ID, Name = tu.Name, Input = tu.Input });
                    var result = Dispatch(tu.Name, JsonSerializer.SerializeToElement(tu.Input), tools, spaces);
                    actions.Add(result);
                    toolResults.Add(new ToolResultBlockParam { ToolUseID = tu.ID, Content = result });
                }
            }

            if (resp.StopReason != "tool_use" || toolResults.Count == 0) break;

            messages.Add(new MessageParam { Role = Role.Assistant, Content = assistant });
            messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
        }

        _budget.RecordTokens(userId, inTok, outTok, cacheRead);

        // Record the turn on the active space so its conversation persists and stays isolated per space.
        var transcript = new List<Spaces.ChatMessage> { new("user", request.Message) };
        transcript.AddRange(actions.Select(a => new Spaces.ChatMessage("tool", a)));
        if (!string.IsNullOrEmpty(text)) transcript.Add(new("ai", text));
        spaces.AppendChat(transcript);

        // Deterministic follow-up suggestions ride along with the reply (instant, free). The client may
        // then upgrade them to LLM-refined ones via GET /api/suggestions?refine=1 — off the reply's path.
        return new ChatReply(text, actions, MessagesRemaining: remaining,
            Suggestions: SuggestionEngine.Suggest(store.Current));
    }

    /// <summary>
    /// Hybrid follow-up suggestions: the deterministic <see cref="SuggestionEngine"/> is the floor; when
    /// Claude is configured, LLM suggestions are enabled, and the user's budget is healthy, one short
    /// (no-tools) call refines them for variety. Never consumes a message credit — only records tokens.
    /// Any failure falls back to the deterministic list.
    /// </summary>
    public async Task<List<string>> SuggestAsync(SpatialAI.Core.Model.Scene scene, string userId, CancellationToken ct)
    {
        var baseList = SuggestionEngine.Suggest(scene);
        if (_client is null || !_useLlmSuggestions || _budget.Remaining(userId) <= _minBudgetForLlm)
            return baseList;

        // On an empty scene the deterministic openers ("create a room …") are exactly right — don't let
        // the LLM refine them into "add a sofa" before any room exists (you must create a room first).
        if (scene.Rooms.Count == 0 && scene.Items.Count == 0)
            return baseList;

        try
        {
            var sceneJson = scene.Rooms.Count == 0 && scene.Items.Count == 0
                ? "(the scene is currently empty)"
                : SceneContext.ToJson(scene);
            var prompt =
                "Available object kinds:\n" + FurnitureFactory.DescribeKindsInline() + "\n\n" +
                "Current scene JSON:\n" + sceneJson + "\n\n" +
                "Suggest exactly 3 short next commands the user could type to extend or improve THIS scene. " +
                "Reference what already exists; don't repeat what's done. Each must be an imperative phrase of " +
                "at most 6 words, buildable with the kinds/tools. Return ONLY a JSON array of 3 strings.";

            var resp = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 150,
                System = new List<TextBlockParam>
                {
                    new() { Text = "You propose concise next-step commands for a 3D room-design app. Reply with a JSON array of short imperative strings only." },
                },
                Messages = new List<MessageParam> { new() { Role = Role.User, Content = prompt } },
            }, cancellationToken: ct);

            _budget.RecordTokens(userId, resp.Usage.InputTokens, resp.Usage.OutputTokens, resp.Usage.CacheReadInputTokens ?? 0);

            var text = "";
            foreach (var block in resp.Content)
                if (block.TryPickText(out TextBlock? t)) text += t.Text;

            var parsed = ParseStringArray(text);
            return parsed.Count > 0 ? parsed.Take(3).ToList() : baseList;
        }
        catch
        {
            return baseList;   // model error / timeout / bad JSON → deterministic floor
        }
    }

    /// <summary>Extracts a JSON string array from model text (tolerates prose around the array).</summary>
    private static List<string> ParseStringArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return [];
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(text[start..(end + 1)]);
            return arr?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? [];
        }
        catch { return []; }
    }

    private static string BuildSceneContext(SceneStore store)
    {
        var current = store.Current;
        var body = current.Rooms.Count == 0 && current.Items.Count == 0
            ? "(the scene is currently empty)"
            : SceneContext.ToJson(current);
        return "CURRENT SCENE (JSON, refreshed each turn). Use it: reference existing rooms/items by name, " +
               "do not recreate what already exists, and edit/extend existing rooms, openings and items " +
               "rather than duplicating them.\n" + body;
    }

    private static string Dispatch(string name, JsonElement args, SceneTools tools, SpaceManager spaces) =>
        SpaceTools.Handles(name)
            ? SpaceTools.Invoke(spaces, name, args)
            : SceneToolRouter.Invoke(tools, name, args);

    private static List<Tool> BuildTools() =>
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
                kind = new { type = "string", description = FurnitureFactory.DescribeKindsInline() },
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
                anchor = new { type = "string", description = "Position by intent instead of raw coordinates: 'center', 'wall:north|south|east|west', 'corner' (or 'corner:ne|nw|se|sw'), 'near:<item>', or 'left|right|front|behind:<item>'. Preferred over positionX/Z; the system keeps the item inside the room and clear of others." },
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
                anchor = new { type = "string", description = "Position by intent: 'center', 'wall:north|south|east|west', 'corner' (or 'corner:ne|nw|se|sw'), 'near:<item>', or 'left|right|front|behind:<item>'. Preferred over positionX/Z." },
                roomName = new { type = "string", description = "Optional target room" }
            },
            required = new[] { "name", "parts" }
        }),
        Tool("move_item", "Move an item to a new floor position. Prefer `anchor` (corner/wall/near) over raw coordinates; the system keeps the item inside the room and clear of others.", new {
            type = "object",
            properties = new {
                itemName = new { type = "string" },
                anchor = new { type = "string", description = "Where to move it, by intent: 'center', 'wall:north|south|east|west', 'corner' (or 'corner:ne|nw|se|sw'), 'near:<item>', or 'left|right|front|behind:<item>'. Use this for vague targets like 'the corner' or 'against the wall'." },
                positionX = new { type = "number", description = "Optional exact X (m); use only for precise coordinates." },
                positionZ = new { type = "number", description = "Optional exact Z (m); use only for precise coordinates." }
            },
            required = new[] { "itemName" }
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
        Tool("delete_room", "Delete a room by name, along with the items inside it.", new {
            type = "object",
            properties = new { roomName = new { type = "string" } },
            required = new[] { "roomName" }
        }),
        Tool("create_group", "Create a named group to hold related items (e.g. a production line or a storage zone) so they can be moved or deleted together.", new {
            type = "object",
            properties = new {
                name = new { type = "string", description = "Group name, e.g. 'Line A'" },
                parentName = new { type = "string", description = "Optional parent group to nest under" }
            },
            required = new[] { "name" }
        }),
        Tool("add_to_group", "Add existing items to a group by name (creates the group if it doesn't exist).", new {
            type = "object",
            properties = new {
                groupName = new { type = "string" },
                itemNames = new { type = "array", items = new { type = "string" }, description = "Names of items to add to the group" }
            },
            required = new[] { "groupName", "itemNames" }
        }),
        Tool("move_group", "Move every item in a group together. Prefer `anchor` (corner/wall/near), or give positionX/Z to move the group's center there.", new {
            type = "object",
            properties = new {
                groupName = new { type = "string" },
                anchor = new { type = "string", description = "Where to move the group, by intent: 'center', 'wall:north|south|east|west', 'corner' (or 'corner:ne|nw|se|sw'), 'near:<item>'." },
                positionX = new { type = "number", description = "Optional target X for the group's center (m)." },
                positionZ = new { type = "number", description = "Optional target Z for the group's center (m)." }
            },
            required = new[] { "groupName" }
        }),
        Tool("delete_group", "Delete a group. By default also deletes its items; pass deleteItems=false to keep the items and only disband the group.", new {
            type = "object",
            properties = new {
                groupName = new { type = "string" },
                deleteItems = new { type = "boolean", description = "Also delete the group's items (default true)." }
            },
            required = new[] { "groupName" }
        }),
        Tool("create_warehouse", "Build a complete warehouse shell in one call: a large, tall room with a flat roof, concrete floor and dock doors. Prefer this over create_room for industrial spaces.", new {
            type = "object",
            properties = new {
                name = new { type = "string", description = "Warehouse name" },
                width = new { type = "number", description = "Width along X (m), default 24" },
                depth = new { type = "number", description = "Depth along Z (m), default 36" },
                height = new { type = "number", description = "Wall height (m), default 8" },
                dockDoors = new { type = "integer", description = "Number of dock doors on the front wall, default 2" }
            }
        }),
        Tool("create_production_line", "Build a production line in one call: a conveyor spine with a machine station per segment (cnc, robot, press, workbench), all facing the belt and grouped so they can be moved/deleted together.", new {
            type = "object",
            properties = new {
                name = new { type = "string", description = "Group name for the line, e.g. 'Line A'" },
                stations = new { type = "integer", description = "Number of machine stations, default 4" },
                roomName = new { type = "string", description = "Optional target room" },
                anchor = new { type = "string", description = "Where to place the line: 'center', 'wall:north|south|east|west', 'corner[:ne|nw|se|sw]', 'near:<item>'." },
                spacing = new { type = "number", description = "Optional spacing between stations (m)" }
            }
        }),
        Tool("create_rack_aisles", "Lay out warehouse racking in one call: rows of storage racks separated by aisles, grouped together. Prefer this over placing racks one by one.", new {
            type = "object",
            properties = new {
                name = new { type = "string", description = "Group name, e.g. 'Racking'" },
                rows = new { type = "integer", description = "Number of rack rows / aisles, default 3" },
                racksPerRow = new { type = "integer", description = "Racks per row, default 4" },
                aisleWidth = new { type = "number", description = "Aisle width between rows (m), default 2.8" },
                rackKind = new { type = "string", description = "Rack kind: pallet_rack (default), cantilever_rack, shelving_unit" },
                roomName = new { type = "string", description = "Optional target room" },
                anchor = new { type = "string", description = "Where to place the block: 'center', 'wall:<dir>', 'corner[:..]', 'near:<item>'." }
            }
        }),
        Tool("enclose_room", "Build a fence/wall around a room's PERIMETER from four thin segments (grouped), optionally leaving one wall open as a gateway. Use this for 'fence/wall around the yard' — never stretch a single fence to enclose an area, which fills the whole footprint.", new {
            type = "object",
            properties = new {
                roomName = new { type = "string", description = "Optional target room (defaults to the most recent room)" },
                kind = new { type = "string", description = "Barrier kind: fence (default), hedge, or railing" },
                height = new { type = "number", description = "Optional barrier height (m)" },
                gateWall = new { type = "string", @enum = new[] { "north", "south", "east", "west" }, description = "Optional wall to leave open for an entrance" }
            }
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

    // Reuse the existing JSON-Schema objects verbatim, reshaped into an Anthropic tool definition
    // (properties + required lifted out of the schema; input_schema Type is auto-set to "object").
    private static Tool Tool(string name, string description, object schema)
    {
        var el = JsonSerializer.SerializeToElement(schema);
        var props = new Dictionary<string, JsonElement>();
        if (el.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object)
            foreach (var kv in p.EnumerateObject())
                props[kv.Name] = kv.Value.Clone();
        var required = new List<string>();
        if (el.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.Array)
            foreach (var item in r.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) required.Add(item.GetString()!);

        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = new() { Properties = props, Required = required },
        };
    }
}
