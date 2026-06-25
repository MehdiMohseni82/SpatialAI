using System.Text;
using System.Text.Json;
using SpatialAI.Api;
using SpatialAI.Api.Blueprint;
using SpatialAI.Api.Spaces;
using SpatialAI.Core.Model;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SceneStore>();
builder.Services.AddSingleton<SceneTools>();
builder.Services.AddSingleton<SceneHub>();
builder.Services.AddSingleton<ChatEngine>();
builder.Services.AddSingleton(new SpaceRepository(
    builder.Configuration["Spaces:Directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "spaces")));
builder.Services.AddSingleton<SpaceManager>();
builder.Services.AddSingleton<VisionClient>();
builder.Services.AddSingleton<BlueprintService>();
builder.Services.AddSingleton<BuildingReconstructor>();

var app = builder.Build();
app.UseDefaultFiles();
// No-cache for the viewer assets so edits/restarts always show the latest build (demo + dev friendly).
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate"
});

// Eagerly create the hub so it subscribes to scene changes from the start.
app.Services.GetRequiredService<SceneHub>();

// ── Scene ────────────────────────────────────────────────────────────────
app.MapGet("/api/scene", (SceneHub hub) => Results.Content(hub.CurrentJson(), "application/json"));

app.MapPost("/api/reset", (SceneStore store) => { store.Reset(); return Results.Ok(); });

// Seed a sample scene (offline fallback / quick demo without the LLM).
app.MapPost("/api/seed", (SceneStore store, SceneTools tools) =>
{
    store.Reset();
    tools.CreateRoom("Office", 6, 5);
    tools.CreateItem("Desk", "desk", colorR: 0.55f, colorG: 0.36f, colorB: 0.2f, positionX: 0, positionZ: 1.6f);
    tools.CreateItem("Monitor", "monitor", positionX: 0, positionZ: 1.9f);
    tools.CreateItem("Chair", "chair", colorR: 0.2f, colorG: 0.4f, colorB: 0.7f, positionX: 0, positionZ: 1.0f);
    tools.CreateItem("Lamp", "floor_lamp", positionX: -2, positionZ: -1.5f);
    tools.CreateItem("Plant", "plant", positionX: 2, positionZ: -1.5f);
    return Results.Ok();
});

// ── Live updates (Server-Sent Events) ──────────────────────────────────────
app.MapGet("/api/stream", async (HttpContext ctx, SceneHub hub) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var patch = ctx.Request.Query["mode"] == "patch";
    var (id, reader) = hub.Subscribe(patch);
    try
    {
        await foreach (var json in reader.ReadAllAsync(ctx.RequestAborted))
            await WriteEvent(ctx, json);
    }
    catch (OperationCanceledException) { /* client disconnected */ }
    finally { hub.Unsubscribe(id); }

    static async Task WriteEvent(HttpContext ctx, string json)
    {
        await ctx.Response.WriteAsync($"data: {json}\n\n");
        await ctx.Response.Body.FlushAsync();
    }
});

// ── Chat (natural language -> function calling -> scene) ────────────────────
app.MapPost("/api/chat", async (ChatRequest req, ChatEngine engine, CancellationToken ct) =>
    Results.Ok(await engine.ChatAsync(req, ct)));

app.MapGet("/api/chat/history", (SpaceManager spaces) => Results.Ok(spaces.CurrentChat()));

// ── Blueprint import: plan images → structured BuildingPlan → live 3D reconstruction ──
app.MapPost("/api/import/plans", async (HttpRequest req, BlueprintService blueprint,
    BuildingReconstructor reconstructor, SpaceManager spaces, CancellationToken ct) =>
{
    if (!blueprint.IsConfigured)
        return Results.BadRequest(new { error = "Vision model is not configured (set OpenAI:AzureEndpoint / OpenAI:ApiKey)." });
    if (!req.HasFormContentType)
        return Results.BadRequest(new { error = "Expected a multipart/form-data upload of plan images." });

    var form = await req.ReadFormAsync(ct);
    var images = new List<VisionClient.Image>();
    foreach (var file in form.Files)
    {
        if (file.Length == 0) continue;
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var mime = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType;
        images.Add(new VisionClient.Image(ms.ToArray(), mime));
    }
    if (images.Count == 0) return Results.BadRequest(new { error = "No images were uploaded." });

    var plan = await blueprint.BuildAsync(images, ct);

    // Reconstruct into a fresh space so the building appears live; the user refines from there.
    spaces.NewSpace("Imported building");
    var rooms = reconstructor.Reconstruct(plan);

    return Results.Ok(new { plan, roomsBuilt = rooms });
}).DisableAntiforgery();

app.MapGet("/api/configured", (ChatEngine engine) => Results.Ok(new { configured = engine.IsConfigured }));

// ── Manual edits from the viewer (drag / rotate / scale gizmos) ─────────────
app.MapPost("/api/items/{id:guid}/transform", (Guid id, ItemTransform t, SceneStore store) =>
{
    var ok = store.Mutate(s =>
    {
        var item = s.Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return false;
        if (t.PositionX is not null && t.PositionZ is not null)
            item.Position = item.Position with { X = t.PositionX.Value, Z = t.PositionZ.Value };
        if (t.PositionY is not null)
            item.Position = item.Position with { Y = t.PositionY.Value };
        if (t.RotationY is not null) item.RotationY = t.RotationY.Value;
        if (t.SizeX is not null && t.SizeY is not null && t.SizeZ is not null)
        {
            var newSize = new Vec3(t.SizeX.Value, t.SizeY.Value, t.SizeZ.Value);
            // Scale the parts proportionally so a gizmo resize keeps composite items coherent.
            float fx = item.Size.X > 0 ? newSize.X / item.Size.X : 1f;
            float fy = item.Size.Y > 0 ? newSize.Y / item.Size.Y : 1f;
            float fz = item.Size.Z > 0 ? newSize.Z / item.Size.Z : 1f;
            foreach (var p in item.Parts)
            {
                p.Offset = new Vec3(p.Offset.X * fx, p.Offset.Y * fy, p.Offset.Z * fz);
                p.Size = new Vec3(p.Size.X * fx, p.Size.Y * fy, p.Size.Z * fz);
            }
            item.Size = newSize;
        }
        return true;
    });
    return ok ? Results.Ok() : Results.NotFound();
});

// ── Generic tool endpoint (used by the MCP server so it drives the same scene) ─
app.MapPost("/api/tools/{name}", async (string name, HttpRequest req, SceneTools tools, SpaceManager spaces) =>
{
    JsonElement args;
    try
    {
        using var doc = await JsonDocument.ParseAsync(req.Body);
        args = doc.RootElement.Clone();
    }
    catch (JsonException)
    {
        using var empty = JsonDocument.Parse("{}");
        args = empty.RootElement.Clone();
    }
    var result = SpaceTools.Handles(name)
        ? SpaceTools.Invoke(spaces, name, args)
        : SceneToolRouter.Invoke(tools, name, args);
    return Results.Ok(new { result });
});

// ── Saved spaces (save / new / open / modify later) ─────────────────────────
app.MapGet("/api/spaces", (SpaceManager spaces) => Results.Ok(spaces.List()));

app.MapGet("/api/spaces/current", (SpaceManager spaces) => Results.Ok(spaces.Current));

app.MapPost("/api/spaces", (SpaceNameRequest req, SpaceManager spaces) =>
    Results.Ok(spaces.NewSpace(req.Name)));

app.MapPost("/api/spaces/save", (SaveSpaceRequest? req, SpaceManager spaces) =>
    Results.Ok(string.IsNullOrWhiteSpace(req?.Name) ? spaces.Save() : spaces.SaveAs(req!.Name!)));

app.MapPost("/api/spaces/{id:guid}/open", (Guid id, SpaceManager spaces) =>
{
    var info = spaces.Open(id);
    return info is null ? Results.NotFound() : Results.Ok(info);
});

app.MapPut("/api/spaces/{id:guid}", (Guid id, SpaceNameRequest req, SpaceManager spaces) =>
    spaces.Rename(id, req.Name) ? Results.Ok() : Results.NotFound());

app.MapDelete("/api/spaces/{id:guid}", (Guid id, SpaceManager spaces) =>
    spaces.Delete(id) ? Results.Ok() : Results.NotFound());

app.MapPost("/api/spaces/{id:guid}/duplicate", (Guid id, SpaceManager spaces) =>
{
    var copy = spaces.Duplicate(id);
    return copy is null ? Results.NotFound() : Results.Ok(copy);
});

// ── Analysis (also callable without the LLM, for visual demos) ──────────────
app.MapGet("/api/analysis/unused", (string? roomName, SceneTools tools) =>
    Results.Ok(new { message = tools.FindUnusedAreas(roomName) }));

app.MapGet("/api/analysis/ergonomics", (string? roomName, float? userX, float? userZ, SceneTools tools) =>
    Results.Ok(new { message = tools.AnalyzeErgonomics(roomName, userX, userZ) }));

app.Run();

internal sealed record ItemTransform(
    float? PositionX, float? PositionY, float? PositionZ,
    float? RotationY,
    float? SizeX, float? SizeY, float? SizeZ);

internal sealed record SpaceNameRequest(string Name);
internal sealed record SaveSpaceRequest(string? Name);
