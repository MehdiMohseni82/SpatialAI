using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using SpatialAI.Api.Tenancy;
using SpatialAI.Core.Model;

namespace SpatialAI.Api.Collab;

/// <summary>One collaborator's live presence in a room (identity + colour + cursor/selection/camera).</summary>
public sealed class Participant
{
    public required string UserId { get; init; }
    public required string Name { get; init; }
    public required string Color { get; init; }
    public DateTime LastSeenUtc { get; set; }
    public Guid? SelectedItemId { get; set; }
    public double[]? Pointer { get; set; }   // [x, z] floor point
    public double[]? Camera { get; set; }    // [px, py, pz, tx, ty, tz]
}

/// <summary>
/// Manages shared collaboration rooms. A room IS a shared <see cref="TenantContext"/> keyed by
/// <c>room:{code}</c> in the <see cref="TenantRegistry"/> — so once several users resolve that key,
/// they share one scene + hub and every edit broadcasts to all of them (no SceneHub changes needed).
/// This service adds room lifecycle + a lightweight, coalesced presence channel (cursors/selection).
/// </summary>
public sealed class RoomService(TenantRegistry tenants)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] Palette =
        { "#fb923c", "#38bdf8", "#a78bfa", "#34d399", "#f472b6", "#facc15", "#f87171", "#22d3ee" };
    private static readonly long CoalesceTicks = TimeSpan.FromMilliseconds(90).Ticks;

    private sealed class Room
    {
        public required string Code { get; init; }
        public required string HostUserId { get; init; }
        public DateTime CreatedAt { get; init; }
        public ConcurrentDictionary<string, Participant> Participants { get; } = new();
        public ConcurrentDictionary<Guid, Channel<string>> Subscribers { get; } = new();
        public long LastBroadcastTicks;
        public int ColorCursor;
    }

    private readonly ConcurrentDictionary<string, Room> _rooms = new();

    public bool Exists(string code) => _rooms.ContainsKey(code);
    public static string TenantKey(string code) => "room:" + code;

    /// <summary>Creates a room seeded with a deep copy of the host's current scene, and joins the host.</summary>
    public string Create(string hostUserId, string hostName)
    {
        var code = NewCode();
        _rooms[code] = new Room { Code = code, HostUserId = hostUserId, CreatedAt = DateTime.UtcNow };

        var hostCtx = tenants.For(hostUserId);
        var roomCtx = tenants.For(TenantKey(code));
        roomCtx.Store.Load(Clone(hostCtx.Store.Current));   // seed: host's scene → shared room scene

        Join(code, hostUserId, hostName);
        return code;
    }

    public bool IsHost(string code, string userId) => _rooms.TryGetValue(code, out var r) && r.HostUserId == userId;

    public Participant? Join(string code, string userId, string name)
    {
        if (!_rooms.TryGetValue(code, out var room)) return null;
        var p = room.Participants.GetOrAdd(userId, _ => new Participant
        {
            UserId = userId,
            Name = name,
            Color = Palette[Math.Abs(Interlocked.Increment(ref room.ColorCursor)) % Palette.Length],
            LastSeenUtc = DateTime.UtcNow,
        });
        p.LastSeenUtc = DateTime.UtcNow;
        BroadcastNow(room);
        return p;
    }

    public void Leave(string code, string userId)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;
        room.Participants.TryRemove(userId, out _);
        BroadcastNow(room);
        if (room.Participants.IsEmpty) _rooms.TryRemove(code, out _);   // evict empty room
    }

    public void UpdatePresence(string code, string userId, Guid? selectedItemId, double[]? pointer, double[]? camera)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;
        if (!room.Participants.TryGetValue(userId, out var p)) return;
        p.SelectedItemId = selectedItemId;
        p.Pointer = pointer;
        p.Camera = camera;
        p.LastSeenUtc = DateTime.UtcNow;
        MaybeBroadcast(room);
    }

    /// <summary>Roster + host flag for the UI. Null if the room is gone.</summary>
    public object? CurrentInfo(string code, string forUserId)
    {
        if (!_rooms.TryGetValue(code, out var room)) return null;
        EvictStale(room);
        return new
        {
            inRoom = true,
            code,
            isHost = room.HostUserId == forUserId,
            participants = room.Participants.Values
                .OrderBy(p => p.Name)
                .Select(p => new { p.Name, p.Color })
                .ToArray(),
        };
    }

    public int ParticipantCount(string code) => _rooms.TryGetValue(code, out var r) ? r.Participants.Count : 0;

    public (Guid id, ChannelReader<string> reader)? Subscribe(string code)
    {
        if (!_rooms.TryGetValue(code, out var room)) return null;
        var ch = Channel.CreateUnbounded<string>();
        var id = Guid.NewGuid();
        room.Subscribers[id] = ch;
        ch.Writer.TryWrite(Snapshot(room));   // immediate baseline
        return (id, ch.Reader);
    }

    public void Unsubscribe(string code, Guid id)
    {
        if (_rooms.TryGetValue(code, out var room)) room.Subscribers.TryRemove(id, out _);
    }

    // ── internals ──────────────────────────────────────────────────────────
    private void MaybeBroadcast(Room room)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref room.LastBroadcastTicks);
        if (now - last < CoalesceTicks) return;
        if (Interlocked.CompareExchange(ref room.LastBroadcastTicks, now, last) != last) return;
        Push(room);
    }

    private void BroadcastNow(Room room)
    {
        Interlocked.Exchange(ref room.LastBroadcastTicks, DateTime.UtcNow.Ticks);
        Push(room);
    }

    private static void Push(Room room)
    {
        EvictStale(room);
        var snap = Snapshot(room);
        foreach (var ch in room.Subscribers.Values) ch.Writer.TryWrite(snap);
    }

    private static string Snapshot(Room room) => JsonSerializer.Serialize(new
    {
        type = "presence",
        players = room.Participants.Values.Select(p => new
        {
            userId = p.UserId,
            name = p.Name,
            color = p.Color,
            selectedItemId = p.SelectedItemId,
            pointer = p.Pointer,
            camera = p.Camera,
        }),
    }, Json);

    private static void EvictStale(Room room)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-15);
        foreach (var kv in room.Participants)
            if (kv.Value.LastSeenUtc < cutoff)
                room.Participants.TryRemove(kv.Key, out _);
    }

    private static Scene Clone(Scene s) =>
        JsonSerializer.Deserialize<Scene>(JsonSerializer.Serialize(s, Json), Json)!;

    private static string NewCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no ambiguous 0/O/1/I
        var buf = new char[6];
        for (var i = 0; i < buf.Length; i++) buf[i] = chars[Random.Shared.Next(chars.Length)];
        return new string(buf);
    }
}
