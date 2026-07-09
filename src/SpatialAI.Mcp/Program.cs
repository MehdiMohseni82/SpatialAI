using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpatialAI.Mcp;

// SpatialAI MCP server: exposes the scene tools (create_room, create_item, ...) over the Model Context
// Protocol via stdio. Tools forward to the SpatialAI API, so any MCP client edits the live 3D scene.
//
//   SpatialApi    base URL: config key "SpatialApi" / env SpatialApi (default http://localhost:5005)
//   SpatialApiKey personal API token (env SpatialApiKey). Set it to edit YOUR OWN space on the remote,
//                 multi-tenant API (https://spatial.dotnet-talk.com) — sent as Authorization: Bearer.
//                 Get it from the app's "Connect Claude Desktop" panel. Omit for a local single-scene run.

var builder = Host.CreateApplicationBuilder(args);

// stdout is the MCP protocol channel — send all logs to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

var apiBase = builder.Configuration["SpatialApi"] ?? "http://localhost:5005";
var apiKey = builder.Configuration["SpatialApiKey"];   // personal token → edit YOUR space on a remote API
builder.Services.AddHttpClient<SpatialApiClient>(c =>
{
    c.BaseAddress = new Uri(apiBase);
    if (!string.IsNullOrWhiteSpace(apiKey))
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
