using System.Net.Http.Json;
using System.Text.Json;

namespace SpatialAI.Mcp;

/// <summary>
/// Thin HTTP client to the SpatialAI API's generic tool endpoint. Because the MCP server forwards to
/// the running API, MCP clients (Inspector, VS Code, the bridge) edit the SAME live scene the viewer shows.
/// </summary>
public sealed class SpatialApiClient(HttpClient http)
{
    public async Task<string> InvokeAsync(string tool, object args, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync($"api/tools/{tool}", args, ct);
            response.EnsureSuccessStatusCode();
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return doc.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "(no result)";
        }
        catch (Exception ex)
        {
            return $"Failed to reach SpatialAI API ({http.BaseAddress}): {ex.Message}";
        }
    }
}
