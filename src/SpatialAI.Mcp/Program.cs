using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpatialAI.Mcp;

// SpatialAI MCP server: exposes the scene tools (create_room, create_item, ...) over the Model Context
// Protocol via stdio. Tools forward to the SpatialAI API, so any MCP client edits the live 3D scene.
//
//   SpatialApi base URL: config key "SpatialApi" / env SpatialApi (default http://localhost:5005)

var builder = Host.CreateApplicationBuilder(args);

// stdout is the MCP protocol channel — send all logs to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

var apiBase = builder.Configuration["SpatialApi"] ?? "http://localhost:5005";
builder.Services.AddHttpClient<SpatialApiClient>(c => c.BaseAddress = new Uri(apiBase));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
