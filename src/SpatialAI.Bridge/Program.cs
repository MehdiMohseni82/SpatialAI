using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI.Chat;

// ─────────────────────────────────────────────────────────────────────────────
// SpatialAI Bridge — "MCP tools + function calls", literally.
//
// 1. Connect to the SpatialAI MCP server (stdio) as an MCP CLIENT.
// 2. Convert each MCP tool schema into an Azure OpenAI ChatTool.
// 3. Run the function-calling loop; forward each model tool call over MCP (CallToolAsync).
//    The MCP server forwards to the SpatialAI API, so changes appear LIVE in the 3D viewer.
//
// Prereqs: the SpatialAI API must be running (default http://localhost:5005) so the viewer is live.
// Config (user-secrets or env OpenAI__AzureEndpoint / OpenAI__ApiKey / OpenAI__ChatDeployment).
//   --list-tools : discover + call one tool, no Azure OpenAI needed.
// ─────────────────────────────────────────────────────────────────────────────

var config = new ConfigurationBuilder()
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

var listToolsOnly = args.Contains("--list-tools", StringComparer.OrdinalIgnoreCase);
var endpoint = config["OpenAI:AzureEndpoint"];
var apiKey = config["OpenAI:ApiKey"];
var deployment = config["OpenAI:ChatDeployment"] ?? "gpt-4o";

if (!listToolsOnly && (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)))
{
    Console.Error.WriteLine("Missing Azure OpenAI config. Set OpenAI__AzureEndpoint and OpenAI__ApiKey (env or user-secrets).");
    Console.Error.WriteLine("Tip: run with --list-tools to demo MCP discovery without Azure OpenAI.");
    return 1;
}

var prompt = args.FirstOrDefault(a => !a.StartsWith('-'))
    ?? "Create a 6x5 office, then add a desk, a chair and a lamp, and tell me where I could put a couch.";

var serverDll = Environment.GetEnvironmentVariable("MCP_SERVER_DLL") ?? DefaultServerDll();
Console.WriteLine($"▶ Launching MCP server: {serverDll}");
if (!listToolsOnly) Console.WriteLine($"▶ Prompt: {prompt}");
Console.WriteLine();

await using var mcp = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "SpatialAI",
    Command = "dotnet",
    Arguments = [serverDll]
}));

var mcpTools = await mcp.ListToolsAsync();
Console.WriteLine($"✔ Discovered {mcpTools.Count} MCP tool(s): {string.Join(", ", mcpTools.Select(t => t.Name))}\n");

if (listToolsOnly)
{
    foreach (var tool in mcpTools)
        Console.WriteLine($"   • {tool.Name} — {tool.Description}");
    Console.WriteLine("\n▶ Direct MCP call: create_room('Office', 6, 5)");
    var probe = await mcp.CallToolAsync("create_room", new Dictionary<string, object?>
    {
        ["name"] = "Office", ["width"] = 6.0, ["depth"] = 5.0
    });
    Console.WriteLine(string.Join("\n", probe.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    return 0;
}

var chatTools = mcpTools
    .Select(t => ChatTool.CreateFunctionTool(t.Name, t.Description, BinaryData.FromString(t.JsonSchema.GetRawText())))
    .ToList();

var aoai = new AzureOpenAIClient(new Uri(endpoint!), new AzureKeyCredential(apiKey!));
var chatClient = aoai.GetChatClient(deployment);

var options = new ChatCompletionOptions();
foreach (var tool in chatTools) options.Tools.Add(tool);

var messages = new List<ChatMessage>
{
    new SystemChatMessage(
        "You build a 3D scene by calling tools. Always create a room before adding items. " +
        "Infer sensible sizes/shapes/colors for real objects (chair, desk, lamp, ...). Keep replies short."),
    new UserChatMessage(prompt)
};

var completion = (await chatClient.CompleteChatAsync(messages, options)).Value;
while (completion.FinishReason == ChatFinishReason.ToolCalls)
{
    messages.Add(new AssistantChatMessage(completion));
    foreach (var toolCall in completion.ToolCalls)
    {
        Console.WriteLine($"→ {toolCall.FunctionName}({toolCall.FunctionArguments})");
        var arguments = ParseArguments(toolCall.FunctionArguments);
        var result = await mcp.CallToolAsync(toolCall.FunctionName, arguments);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Console.WriteLine($"   {text}");
        messages.Add(new ToolChatMessage(toolCall.Id, text));
    }
    completion = (await chatClient.CompleteChatAsync(messages, options)).Value;
}

Console.WriteLine($"\n💬 {string.Join("\n", completion.Content.Select(p => p.Text))}");
Console.WriteLine("\n(Watch the changes appear live in the SpatialAI viewer.)");
return 0;

static IReadOnlyDictionary<string, object?> ParseArguments(BinaryData functionArguments)
{
    using var doc = JsonDocument.Parse(functionArguments);
    var dict = new Dictionary<string, object?>();
    foreach (var prop in doc.RootElement.EnumerateObject())
    {
        dict[prop.Name] = prop.Value.ValueKind switch
        {
            JsonValueKind.Number => prop.Value.GetDouble(),
            JsonValueKind.String => prop.Value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => prop.Value.GetRawText()
        };
    }
    return dict;
}

static string DefaultServerDll()
{
    var baseDir = AppContext.BaseDirectory;
    var configDir = Path.GetFileName(Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar))!); // Debug/Release
    var srcRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
    return Path.Combine(srcRoot, "SpatialAI.Mcp", "bin", configDir, "net9.0", "SpatialAI.Mcp.dll");
}
