# SpatialAI

A small, self-contained demo for **WeAreDevelopers 2026** — *Bridging LLMs and Systems: Practical
Automation with MCP Tools and Function Calls*.

Natural language → **MCP tools + function calling** → an interactive **3D scene**. Ask it to create a
room, add items, or even invent objects ("make me a chair") and watch them appear; then move/rotate/
scale them by hand. No Unity, no branding — just the LLM-automation story, end to end.

```
"create a 6x5 office, add a desk and chair, where can I put a couch?"
        │
        ▼
 Azure OpenAI ──(function calling)──► SceneTools ──► Scene ──► Three.js viewer (live)
        ▲                                  ▲
        └── SpatialAI.Bridge ──(MCP)──► SpatialAI.Mcp ──(HTTP)──┘
```

**One implementation, three surfaces.** Every spatial operation lives once in `SceneTools`
(`SpatialAI.Core`) and is driven three ways: the in-app function-calling chat, an MCP server, and a
function-calling-over-MCP bridge. All three edit the **same live scene**.

## What it can do

- **Create rooms** — `"create a 6 by 5 office"`
- **Place items** — `"add a desk and a chair"` (auto-placed in free space)
- **Invent items** — `"make me a red lamp"` (the LLM infers shape, size, color)
- **Edit** — move / rotate / scale / recolor / delete, by chat **or** by dragging gizmos in 3D
- **Analyze** — `find_unused_areas` (highlights free floor) and `analyze_ergonomics` (reach, monitor
  distance, clearances)

## Projects

| Project | Role |
|---|---|
| `src/SpatialAI.Core` | Scene model, `SceneStore`, `SceneTools`, analyzers, `SceneToolRouter`. Pure C#. |
| `src/SpatialAI.Api` | ASP.NET Core: function-calling chat engine, REST + SSE, serves the Three.js viewer. |
| `src/SpatialAI.Mcp` | MCP server exposing the tools; forwards to the API so MCP clients edit the live scene. |
| `src/SpatialAI.Bridge` | Console: discovers MCP tools → Azure OpenAI function calls → MCP. The talk's headline. |
| `tests/SpatialAI.Tests` | xUnit tests for the analyzers and tools. |

## Prerequisites

- .NET 9 SDK (builds with the .NET 10 SDK too).
- Optional: Azure OpenAI (a `gpt-4o` deployment) for the natural-language features. Without it, the
  viewer, manual editing, `/api/seed`, and the analysis buttons all still work.

## Run the app + viewer

```bash
dotnet build                       # builds SpatialAI.slnx
dotnet run --project src/SpatialAI.Api
# open http://localhost:5005
```

In the viewer:
- Click an item to select it; **W/E/R** = move/rotate/scale; drag the gizmo. Changes sync to the server.
- Toolbar: **Unused areas**, **Ergonomics**, **Reset**.
- No Azure key? `curl -X POST http://localhost:5005/api/seed` to drop in a sample room you can play with.

### Enable the LLM chat (Azure OpenAI)

```bash
cd src/SpatialAI.Api
dotnet user-secrets set "OpenAI:AzureEndpoint" "https://<your-aoai>.openai.azure.com/"
dotnet user-secrets set "OpenAI:ApiKey" "<key>"
dotnet user-secrets set "OpenAI:ChatDeployment" "gpt-4o"
```

(or env vars `OpenAI__AzureEndpoint` / `OpenAI__ApiKey` / `OpenAI__ChatDeployment`). Restart the API,
then type in the chat panel.

## MCP server

The MCP server forwards to the running API, so MCP clients edit the **same** scene the viewer shows.

```bash
# Start the API first (above), then inspect the MCP server:
npx @modelcontextprotocol/inspector dotnet run --project src/SpatialAI.Mcp
# Tools tab → create_room { "name":"Office","width":6,"depth":5 } → watch the viewer
```

VS Code Agent mode: see `.vscode/mcp.json`.

## Bridge (function calling over MCP)

```bash
# API running + Azure OpenAI configured for the bridge:
cd src/SpatialAI.Bridge
dotnet user-secrets set "OpenAI:AzureEndpoint" "https://<your-aoai>.openai.azure.com/"
dotnet user-secrets set "OpenAI:ApiKey" "<key>"
dotnet user-secrets set "OpenAI:ChatDeployment" "gpt-4o"
cd ../..

dotnet run --project src/SpatialAI.Bridge -- "Create a 6x5 office, add a desk, a chair and a lamp"
# changes appear live in the viewer

# No Azure? Prove the MCP path only:
dotnet run --project src/SpatialAI.Bridge -- --list-tools
```

## Tests

```bash
dotnet test
```

## Notes

- The solution is `SpatialAI.slnx` (the XML solution format). `dotnet build` with no argument picks it up.
- The viewer vendors Three.js under `src/SpatialAI.Api/wwwroot/vendor/` — it runs fully offline.
