using System.Text.Json;

namespace SpatialAI.Api.Spaces;

/// <summary>
/// Maps the space-management tools (save_space / new_space / open_space / list_spaces) to
/// <see cref="SpaceManager"/> calls. Shared by the in-app function-calling engine and the generic
/// tool endpoint the MCP server forwards to — so chat and MCP behave identically.
/// </summary>
public static class SpaceTools
{
    public static readonly IReadOnlyList<string> ToolNames =
        ["save_space", "new_space", "open_space", "list_spaces"];

    public static bool Handles(string name) => ToolNames.Contains(name);

    public static string Invoke(SpaceManager manager, string name, JsonElement a)
    {
        string? OptS(string k) =>
            a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        switch (name)
        {
            case "new_space":
            {
                var info = manager.NewSpace(OptS("name") ?? "Untitled");
                return $"Started a new empty space '{info.Name}'.";
            }
            case "save_space":
            {
                var requested = OptS("name");
                var info = string.IsNullOrWhiteSpace(requested) ? manager.Save() : manager.SaveAs(requested);
                return $"Saved the current space as '{info.Name}'.";
            }
            case "open_space":
            {
                var target = OptS("name");
                if (string.IsNullOrWhiteSpace(target)) return "open_space needs a space name.";
                var info = manager.OpenByName(target);
                return info is null
                    ? $"No saved space matching '{target}'."
                    : $"Opened space '{info.Name}'.";
            }
            case "list_spaces":
            {
                var spaces = manager.List();
                if (spaces.Count == 0) return "No saved spaces yet.";
                return "Saved spaces: " + string.Join(", ", spaces.Select(s => $"'{s.Name}'"));
            }
            default:
                return $"Unknown space tool: {name}";
        }
    }
}
