using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using SpatialAI.Api;
using SpatialAI.Api.Auth;
using SpatialAI.Api.Blueprint;
using SpatialAI.Api.Catalog;
using SpatialAI.Api.Collab;
using SpatialAI.Api.Email;
using SpatialAI.Api.Spaces;
using SpatialAI.Api.Tenancy;
using SpatialAI.Core.Analysis;
using SpatialAI.Core.Furniture;
using SpatialAI.Core.Model;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<BudgetStore>();
builder.Services.AddSingleton<ChatEngine>();
builder.Services.AddSingleton(new TenantRegistry(
    builder.Configuration["Spaces:Directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "spaces")));
builder.Services.AddSingleton<RoomService>();

// ── Per-user isolation ──────────────────────────────────────────────────────
// Each request resolves the caller's TenantContext (keyed by the `uid` cookie set in middleware).
// The scene/tools/spaces/hub the endpoint handlers inject are that ONE tenant's own instances, so
// every user works in a completely separate scene.
builder.Services.AddScoped(sp =>
{
    var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext
        ?? throw new InvalidOperationException("No HttpContext available for tenant resolution.");
    // The scene key is the room (when collaborating) or the personal user id — set by the middleware.
    var key = http.Items.TryGetValue("tenantKey", out var v) && v is string s && s.Length > 0 ? s : "anon";
    return sp.GetRequiredService<TenantRegistry>().For(key);
});
builder.Services.AddScoped(sp => sp.GetRequiredService<TenantContext>().Store);
builder.Services.AddScoped(sp => sp.GetRequiredService<TenantContext>().Tools);
builder.Services.AddScoped(sp => sp.GetRequiredService<TenantContext>().Spaces);
builder.Services.AddScoped(sp => sp.GetRequiredService<TenantContext>().Hub);

builder.Services.AddSingleton<VisionClient>();
builder.Services.AddSingleton<BlueprintService>();
builder.Services.AddScoped<BuildingReconstructor>();   // consumes the scoped per-tenant SceneTools
builder.Services.AddSingleton(new CatalogRepository(
    builder.Configuration["Catalog:Database"] ?? Path.Combine(builder.Environment.ContentRootPath, "catalog.db")));

// ── Auth (registration + magic-link) ────────────────────────────────────────
var dataProtection = builder.Services.AddDataProtection();
// Persist the key ring on the data volume so session cookies survive container restarts/redeploys.
var keysDir = builder.Configuration["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(keysDir))
{
    Directory.CreateDirectory(keysDir);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysDir));
}
builder.Services.AddSingleton(new AuthRepository(
    builder.Configuration["Auth:Database"] ?? Path.Combine(builder.Environment.ContentRootPath, "app.db")));
builder.Services.AddSingleton<SessionCodec>();
builder.Services.AddHttpClient();
// Prefer Brevo's REST API when an API key is set (uses the xkeysib key directly); else fall back to SMTP.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Email:BrevoApiKey"]))
    builder.Services.AddSingleton<IEmailSender, BrevoEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

var app = builder.Build();

// Load the item catalog from the database (seeding it on first run) so the catalog — and every kind list
// the LLM sees — is generated from one source of truth.
{
    var catalog = app.Services.GetRequiredService<CatalogRepository>();
    catalog.EnsureSeeded();
    FurnitureFactory.UseCatalog(catalog.Load());
}
// Behind nginx (TLS terminator): trust X-Forwarded-Proto/For so Request.Scheme/IsHttps are correct
// (secure cookies, https magic-link URLs). The container is bound to 127.0.0.1, so only the host proxy
// can reach it — safe to accept forwarded headers from any caller here.
var fwd = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
fwd.KnownNetworks.Clear();
fwd.KnownProxies.Clear();
app.UseForwardedHeaders(fwd);

app.UseDefaultFiles();
// No-cache for the viewer assets so edits/restarts always show the latest build (demo + dev friendly).
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate"
});

// ── Identity: resolve the request's user id (the tenant + budget key) ────────
// Prefer a valid signed session cookie (a registered user); otherwise fall back to an anonymous
// per-browser id so the app still works in open/dev mode.
var sessionCodec = app.Services.GetRequiredService<SessionCodec>();
var roomService = app.Services.GetRequiredService<RoomService>();
var authRepo = app.Services.GetRequiredService<AuthRepository>();
// Open/dev/workshop mode (no auth) shares ONE scene across every surface — so the in-app chat, an MCP
// client and the bridge all edit the same viewer ("three surfaces, one scene"). The public deployment
// (Auth:Required) keeps a private scene per signed-in user.
var singleScene = !app.Configuration.GetValue("Auth:Required", false);
app.Use(async (http, next) =>
{
    string? uid = null;
    var authed = false;
    // 1. Bearer API token (or X-Api-Key) — the headless twin of the sid cookie, so an MCP client can
    //    authenticate AS a user against the remote API and edit that user's own scene.
    var authz = http.Request.Headers.Authorization.ToString();
    var token = authz.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authz["Bearer ".Length..].Trim()
        : http.Request.Headers["X-Api-Key"].ToString();
    if (!string.IsNullOrEmpty(token) && authRepo.FindByApiToken(token) is { } tokenUser) { uid = tokenUser.Id; authed = true; }
    // 2. Signed session cookie (browsers).
    if (uid is null)
    {
        var sid = http.Request.Cookies["sid"];
        if (!string.IsNullOrEmpty(sid) && sessionCodec.TryValidate(sid, out var userId)) { uid = userId; authed = true; }
    }
    // 3. Anonymous per-browser id (open/dev mode).
    if (uid is null)
    {
        uid = http.Request.Cookies["uid"];
        if (string.IsNullOrEmpty(uid))
        {
            uid = Guid.NewGuid().ToString("N");
            http.Response.Cookies.Append("uid", uid,
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = http.Request.IsHttps, MaxAge = TimeSpan.FromDays(1) });
        }
    }
    // Identity (uid) drives budget + presence; the scene key (tenantKey) is the room when collaborating.
    var roomCode = http.Request.Cookies["room"];
    var inRoom = !string.IsNullOrEmpty(roomCode) && roomService.Exists(roomCode);
    http.Items["uid"] = uid;
    http.Items["authed"] = authed;
    http.Items["room"] = inRoom ? roomCode : null;
    http.Items["tenantKey"] = inRoom ? RoomService.TenantKey(roomCode!) : (singleScene ? "local" : uid);
    await next();
});

// ── Gate: when Auth:Required (the public deployment), all /api/* except /api/auth/* need a session.
if (app.Configuration.GetValue("Auth:Required", false))
{
    app.Use(async (http, next) =>
    {
        var path = http.Request.Path;
        // /api/auth/* and /api/me stay open so an unregistered visitor can register and the SPA can
        // learn it needs to redirect to the sign-up page.
        if (path.StartsWithSegments("/api")
            && !path.StartsWithSegments("/api/auth")
            && !path.StartsWithSegments("/api/me")
            && http.Items["authed"] is not true)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await next();
    });
}

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
    ctx.Response.Headers["X-Accel-Buffering"] = "no"; // belt-and-suspenders: tell any proxy not to buffer

    var patch = ctx.Request.Query["mode"] == "patch";
    var (id, reader) = hub.Subscribe(patch);
    var ct = ctx.RequestAborted;
    try
    {
        // Drain scene updates as they arrive; if none for 20s, emit a heartbeat. The heartbeat keeps
        // proxies from idling the connection AND lets the client detect a dead stream (its watchdog
        // reconnects if heartbeats stop — covers the stale half-open socket a server restart can leave).
        while (!ct.IsCancellationRequested)
        {
            var read = reader.WaitToReadAsync(ct).AsTask();
            var winner = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(20), ct));
            if (winner == read)
            {
                if (!await read) break; // channel completed
                while (reader.TryRead(out var json))
                    await WriteEvent(ctx, $"data: {json}\n\n", ct);
            }
            else
            {
                await WriteEvent(ctx, "data: {\"type\":\"ping\"}\n\n", ct);
            }
        }
    }
    catch (OperationCanceledException) { /* client disconnected */ }
    finally { hub.Unsubscribe(id); }

    static async Task WriteEvent(HttpContext ctx, string payload, CancellationToken ct)
    {
        await ctx.Response.WriteAsync(payload, ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
});

// ── Chat (natural language -> Claude tool use -> scene) ─────────────────────
app.MapPost("/api/chat", async (HttpContext http, ChatRequest req, ChatEngine engine,
    SceneTools tools, SceneStore store, SpaceManager spaces, CancellationToken ct) =>
{
    var uid = (string)http.Items["uid"]!;   // set by the uid middleware; also keys this tenant's scene
    return Results.Ok(await engine.ChatAsync(req, uid, tools, store, spaces, ct));
});

app.MapGet("/api/chat/history", (SpaceManager spaces) => Results.Ok(spaces.CurrentChat()));

// Vision "correcting gate": the client posts an AFTER screenshot of the scene it just changed; when the
// gate is enabled the model looks for visible mistakes (wrong facing, overlap, floating…) and fixes them.
app.MapPost("/api/chat/verify", async (HttpContext http, VerifyRequest req, ChatEngine engine,
    SceneTools tools, SceneStore store, SpaceManager spaces, CancellationToken ct) =>
{
    var uid = (string)http.Items["uid"]!;
    var actions = await engine.VerifyAsync(req.AfterImage, req.Request, tools, store, spaces, uid, ct);
    return Results.Ok(new { actions });
});

// Context-aware follow-up suggestions for the current (tenant) scene. Deterministic by default;
// `?refine=1` runs the hybrid path (LLM refinement when configured + budget healthy).
app.MapGet("/api/suggestions", async (HttpContext http, string? refine, SceneStore store, ChatEngine engine,
    CancellationToken ct) =>
{
    var scene = store.Current;
    if (refine is "1" or "true")
    {
        var uid = (string)http.Items["uid"]!;
        return Results.Ok(new { suggestions = await engine.SuggestAsync(scene, uid, ct) });
    }
    return Results.Ok(new { suggestions = SuggestionEngine.Suggest(scene) });
});

// ── Blueprint import: plan images → structured BuildingPlan → live 3D reconstruction ──
app.MapPost("/api/import/plans", async (HttpRequest req, BlueprintService blueprint,
    BuildingReconstructor reconstructor, SpaceManager spaces, BudgetStore budget, IConfiguration cfg, CancellationToken ct) =>
{
    // Vision/blueprint import is a heavy token sink (3+N high-detail image calls per import). It's off by
    // default (Import:Enabled) and, when on, charged several messages against the caller's per-user budget.
    if (!cfg.GetValue("Import:Enabled", false))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!blueprint.IsConfigured)
        return Results.BadRequest(new { error = "Vision model is not configured (set LLM:ApiKey / ANTHROPIC_API_KEY)." });
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

    // Charge the import against the caller's message budget BEFORE the expensive vision calls.
    var uid = req.HttpContext.Items["uid"] as string ?? "anon";
    var cost = cfg.GetValue("Import:MessageCost", 5);
    if (!budget.TryConsume(uid, cost, out var remaining))
        return Results.Json(new { error = $"Plan import costs {cost} messages — you have {remaining} left." },
            statusCode: StatusCodes.Status429TooManyRequests);

    // Optional user hint: the uploaded plan(s) are the top/attic floor(s) — render open to the roof.
    var atticHint = string.Equals(form["attic"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
    var plan = await blueprint.BuildAsync(images, atticHint, ct);

    // Reconstruct into a fresh space so the building appears live; the user refines from there.
    spaces.NewSpace("Imported building");
    var rooms = reconstructor.Reconstruct(plan);

    return Results.Ok(new { plan, roomsBuilt = rooms, messagesRemaining = remaining });
}).DisableAntiforgery();

app.MapGet("/api/configured", (ChatEngine engine) => Results.Ok(new { configured = engine.IsConfigured }));

// Lightweight ops view: global message + token spend and live tenant count (handy on the day).
app.MapGet("/api/admin/stats", (BudgetStore budget, TenantRegistry tenants) =>
{
    var (used, ceiling, inTok, outTok, cacheRead) = budget.Stats();
    return Results.Ok(new
    {
        messagesUsed = used,
        globalCeiling = ceiling,
        activeTenants = tenants.Count,
        inputTokens = inTok,
        outputTokens = outTok,
        cacheReadTokens = cacheRead,
    });
});

// ── Auth: register (capture email) → magic link → verify → session ──────────
app.MapPost("/api/auth/register", async (RegisterRequest req, AuthRepository auth, SessionCodec codec,
    IEmailSender email, IConfiguration cfg, HttpContext http, CancellationToken ct) =>
{
    var e = (req.Email ?? "").Trim();
    var name = (req.Name ?? "").Trim();
    if (!IsValidEmail(e)) return Results.BadRequest(new { error = "Please enter a valid email address." });
    if (name.Length == 0) name = e.Split('@')[0];

    // Hedge / dev: when verification is off, register + sign in immediately (still captures the email).
    if (!cfg.GetValue("Auth:RequireVerification", false))
    {
        var user = auth.GetOrCreateVerifiedUser(e, name);
        SetSession(http, codec, user.Id);
        return Results.Ok(new { authed = true, sent = false });
    }

    var token = auth.CreateMagicLink(e, name, TimeSpan.FromMinutes(60));   // reusable until expiry (SafeLinks-tolerant)
    var baseUrl = (cfg["PublicBaseUrl"] ?? $"{http.Request.Scheme}://{http.Request.Host}").TrimEnd('/');
    var link = $"{baseUrl}/api/auth/verify?token={token}";
    await email.SendMagicLinkAsync(e, name, link, ct);
    return Results.Ok(new { authed = false, sent = true });
});

app.MapGet("/api/auth/verify", (string token, AuthRepository auth, SessionCodec codec, HttpContext http) =>
{
    var consumed = auth.ConsumeMagicLink(token);
    if (consumed is null) return Results.Redirect("/register.html?error=expired");
    var user = auth.GetOrCreateVerifiedUser(consumed.Value.email, consumed.Value.name);
    SetSession(http, codec, user.Id);
    return Results.Redirect("/");
});

app.MapPost("/api/auth/logout", (HttpContext http) =>
{
    http.Response.Cookies.Delete("sid");
    return Results.Ok();
});

// Identity + budget snapshot; the SPA uses this on load to decide whether to redirect to /register.html.
app.MapGet("/api/me", (HttpContext http, AuthRepository auth, BudgetStore budget, ChatEngine engine, IConfiguration cfg) =>
{
    var uid = (string)http.Items["uid"]!;
    var authed = http.Items["authed"] is true;
    string? name = null, mail = null, mcpToken = null;
    if (authed)
    {
        var u = auth.GetUser(uid); name = u?.Name; mail = u?.Email;
        mcpToken = auth.GetOrCreateApiToken(uid);   // personal token for connecting an MCP client (Claude Desktop)
    }
    return Results.Ok(new
    {
        userId = uid,               // presence key (server-derived from the signed cookie; not settable)
        authRequired = cfg.GetValue("Auth:Required", false),
        authenticated = authed,
        importEnabled = cfg.GetValue("Import:Enabled", false),   // plan import (vision) is opt-in
        visionEnabled = engine.VisionEnabled,   // screenshot before/after gate (opt-in) → client captures only when on
        name,
        email = mail,
        mcpToken,                   // only ever returned to the authenticated owner of this session
        messagesRemaining = budget.Remaining(uid),
        messagesPerUser = budget.PerUser,
    });
});

// ── Collaboration rooms ─────────────────────────────────────────────────────
// A room is a shared scene several users co-edit live. Creating one seeds it with the host's current
// scene; the `room` cookie routes all the caller's requests (scene, stream, chat, tools) at it.
app.MapPost("/api/rooms", (HttpContext http, RoomService rooms, AuthRepository auth) =>
{
    var uid = (string)http.Items["uid"]!;
    var name = auth.GetUser(uid)?.Name ?? "Guest";
    var code = rooms.Create(uid, name);
    SetRoomCookie(http, code);
    return Results.Ok(new { code, url = $"{http.Request.Scheme}://{http.Request.Host}/?room={code}" });
});

app.MapPost("/api/rooms/{code}/join", (string code, HttpContext http, RoomService rooms, AuthRepository auth) =>
{
    var uid = (string)http.Items["uid"]!;
    var name = auth.GetUser(uid)?.Name ?? "Guest";
    if (rooms.Join(code, uid, name) is null)
        return Results.NotFound(new { error = "That room no longer exists." });
    SetRoomCookie(http, code);
    return Results.Ok(new { code });
});

app.MapPost("/api/rooms/leave", (HttpContext http, RoomService rooms) =>
{
    var uid = (string)http.Items["uid"]!;
    var code = http.Request.Cookies["room"];
    if (!string.IsNullOrEmpty(code)) rooms.Leave(code, uid);
    http.Response.Cookies.Delete("room");
    return Results.Ok();
});

app.MapGet("/api/rooms/current", (HttpContext http, RoomService rooms) =>
{
    var uid = (string)http.Items["uid"]!;
    var code = http.Request.Cookies["room"];
    if (string.IsNullOrEmpty(code) || !rooms.Exists(code)) return Results.Ok(new { inRoom = false });
    return Results.Ok(rooms.CurrentInfo(code, uid));
});

// Presence: clients POST their cursor/selection/camera (~10Hz); the SSE pushes the room's roster.
app.MapPost("/api/rooms/{code}/presence", (string code, PresenceUpdate body, HttpContext http, RoomService rooms) =>
{
    var uid = (string)http.Items["uid"]!;
    rooms.UpdatePresence(code, uid, body.SelectedItemId, body.Pointer, body.Camera);
    return Results.Ok();
});

app.MapGet("/api/rooms/{code}/presence/stream", async (string code, HttpContext ctx, RoomService rooms) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    var sub = rooms.Subscribe(code);
    if (sub is null) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    var (id, reader) = sub.Value;
    try
    {
        await foreach (var json in reader.ReadAllAsync(ctx.RequestAborted))
        {
            await ctx.Response.WriteAsync($"data: {json}\n\n");
            await ctx.Response.Body.FlushAsync();
        }
    }
    catch (OperationCanceledException) { }
    finally { rooms.Unsubscribe(code, id); }
});

// The item catalog (kinds grouped by category) — sourced from the database, so the UI/clients can show
// exactly what the model can create.
app.MapGet("/api/catalog", () => Results.Ok(
    FurnitureFactory.Current.AllKinds
        .GroupBy(e => e.Category)
        .Select(g => new
        {
            category = g.Key,
            kinds = g.Select(e => new { kind = e.Kind, description = e.Description, aliases = e.Aliases })
        })));

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

static void SetSession(HttpContext http, SessionCodec codec, string userId)
{
    var sid = codec.Issue(userId, TimeSpan.FromDays(1));
    http.Response.Cookies.Append("sid", sid, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = http.Request.IsHttps,
        MaxAge = TimeSpan.FromDays(1),
    });
}

static bool IsValidEmail(string e) =>
    !string.IsNullOrWhiteSpace(e) && !e.Contains(' ') && e.Count(ch => ch == '@') == 1
    && e.IndexOf('@') > 0 && e.LastIndexOf('.') > e.IndexOf('@') + 1 && e.LastIndexOf('.') < e.Length - 1;

static void SetRoomCookie(HttpContext http, string code) =>
    http.Response.Cookies.Append("room", code, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = http.Request.IsHttps,
        MaxAge = TimeSpan.FromHours(8),
    });

internal sealed record RegisterRequest(string? Email, string? Name);
internal sealed record PresenceUpdate(Guid? SelectedItemId, double[]? Pointer, double[]? Camera);

internal sealed record ItemTransform(
    float? PositionX, float? PositionY, float? PositionZ,
    float? RotationY,
    float? SizeX, float? SizeY, float? SizeZ);

internal sealed record SpaceNameRequest(string Name);
internal sealed record SaveSpaceRequest(string? Name);
