using System.Text.Json;
using SpatialAI.Core.Model;

namespace SpatialAI.Core.Tools;

/// <summary>
/// Maps a tool name + JSON arguments to a <see cref="SceneTools"/> call. Shared by the in-app
/// function-calling engine, the REST tool endpoint, and (over HTTP) the MCP server — one router,
/// so every surface behaves identically.
/// </summary>
public static class SceneToolRouter
{
    public static readonly IReadOnlyList<string> ToolNames =
    [
        "create_room", "delete_room", "create_item", "compose_item", "move_item", "rotate_item", "scale_item",
        "recolor_item", "delete_item", "list_scene", "find_unused_areas", "analyze_ergonomics",
        "add_window", "add_door", "add_partition", "set_ceiling", "set_roof", "set_building_roof",
        "arrange_around", "arrange_on",
        "create_group", "add_to_group", "move_group", "delete_group",
        "create_warehouse", "create_production_line", "create_rack_aisles",
        "enclose_room"
    ];

    public static string Invoke(SceneTools tools, string name, JsonElement a)
    {
        float? OptF(string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : null;
        float ReqF(string k, float dflt = 0) => OptF(k) ?? dflt;
        int ReqI(string k, int dflt = 0) => OptF(k) is { } f ? (int)MathF.Round(f) : dflt;
        bool ReqB(string k, bool dflt = false) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => dflt } : dflt;
        string Str(string k, string dflt = "") => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? dflt : dflt;
        string? OptS(string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        List<string> StrArr(string k)
        {
            var list = new List<string>();
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var el in v.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is { } str) list.Add(str);
            return list;
        }

        return name switch
        {
            "create_room" => tools.CreateRoom(Str("name", "Room"), ReqF("width", 4), ReqF("depth", 4), ReqF("centerX"), ReqF("centerZ"), ReqF("height", 2.5f), ReqI("windows"), ReqI("doors"), ReqB("ceiling"), Str("roof", "none"), ReqF("elevation"), ReqI("level")),
            "delete_room" => tools.DeleteRoom(Str("roomName")),
            "add_window" => tools.AddWindow(OptS("roomName"), Str("wall", "north"), OptF("offset"), OptF("width"), OptF("height"), OptF("sill")),
            "add_door" => tools.AddDoor(OptS("roomName"), Str("wall", "south"), OptF("offset"), OptF("width"), OptF("height")),
            "add_partition" => tools.AddPartition(OptS("roomName"), Str("axis", "x"), ReqF("position"), OptF("doorWidth")),
            "set_ceiling" => tools.SetCeiling(OptS("roomName"), ReqB("on", true)),
            "set_roof" => tools.SetRoof(OptS("roomName"), Str("style", "flat")),
            "set_building_roof" => tools.SetBuildingRoof(Str("style", "gable"), OptF("height"), ReqI("dormers")),
            "create_item" => tools.CreateItem(Str("name", "Item"), OptS("kind") ?? OptS("shape") ?? "box", OptF("width"), OptF("height"), OptF("depth"), OptF("colorR"), OptF("colorG"), OptF("colorB"), OptF("positionX"), OptF("positionZ"), OptS("roomName"), OptF("rotationY"), OptS("onItem"), OptS("faceItem"), OptS("anchor")),
            "arrange_around" => tools.ArrangeAround(Str("targetName"), OptS("kind") ?? "chair", ReqI("count", 4), OptF("radius"), OptS("roomName")),
            "arrange_on" => tools.ArrangeOn(Str("targetName"), OptS("kind") ?? "plate", ReqI("count", 1), OptF("radius"), OptS("roomName")),
            "compose_item" => tools.ComposeItem(Str("name", "Item"), ParseParts(a), OptF("positionX"), OptF("positionZ"), OptS("roomName"), OptS("onItem"), OptS("faceItem"), OptS("anchor")),
            "move_item" => tools.MoveItem(Str("itemName"), OptF("positionX"), OptF("positionZ"), OptS("anchor")),
            "rotate_item" => tools.RotateItem(Str("itemName"), ReqF("degrees")),
            "scale_item" => tools.ScaleItem(Str("itemName"), ReqF("factor", 1f)),
            "recolor_item" => tools.RecolorItem(Str("itemName"), ReqF("colorR"), ReqF("colorG"), ReqF("colorB")),
            "delete_item" => tools.DeleteItem(Str("itemName")),
            "create_group" => tools.CreateGroup(Str("name"), OptS("parentName")),
            "add_to_group" => tools.AddToGroup(Str("groupName"), StrArr("itemNames")),
            "move_group" => tools.MoveGroup(Str("groupName"), OptF("positionX"), OptF("positionZ"), OptS("anchor")),
            "delete_group" => tools.DeleteGroup(Str("groupName"), ReqB("deleteItems", true)),
            "create_warehouse" => tools.CreateWarehouse(Str("name", "Warehouse"), ReqF("width", 24f), ReqF("depth", 36f), ReqF("height", 8f), ReqI("dockDoors", 2)),
            "create_production_line" => tools.CreateProductionLine(Str("name", "Production Line"), ReqI("stations", 4), OptS("roomName"), OptS("anchor"), OptF("spacing")),
            "create_rack_aisles" => tools.CreateRackAisles(Str("name", "Racking"), ReqI("rows", 3), ReqI("racksPerRow", 4), OptF("aisleWidth"), OptS("rackKind") ?? "pallet_rack", OptS("roomName"), OptS("anchor")),
            "enclose_room" => tools.EncloseRoom(OptS("roomName"), OptS("kind") ?? "fence", OptF("height"), OptS("gateWall")),
            "list_scene" => tools.ListScene(),
            "find_unused_areas" => tools.FindUnusedAreas(OptS("roomName")),
            "analyze_ergonomics" => tools.AnalyzeErgonomics(OptS("roomName"), OptF("userX"), OptF("userZ")),
            _ => $"Unknown tool: {name}"
        };
    }

    private static List<Part> ParseParts(JsonElement a)
    {
        var result = new List<Part>();
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("parts", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var el in arr.EnumerateArray())
        {
            float F(string k, float dflt = 0) => el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : dflt;
            string S(string k) => el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

            var shape = Enum.TryParse<Shape>(S("shape"), ignoreCase: true, out var sh) ? sh : Shape.Box;
            result.Add(new Part
            {
                Shape = shape,
                Offset = new Vec3(F("offsetX"), F("offsetY"), F("offsetZ")),
                Size = new Vec3(F("sizeX", 0.2f), F("sizeY", 0.2f), F("sizeZ", 0.2f)),
                RotationY = F("rotationY"),
                Color = new Rgba(F("colorR", 0.7f), F("colorG", 0.7f), F("colorB", 0.7f))
            });
        }
        return result;
    }
}
