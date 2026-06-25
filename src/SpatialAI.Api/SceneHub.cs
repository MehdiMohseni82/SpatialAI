using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using SpatialAI.Core.Model;
using SpatialAI.Core.Scene;

namespace SpatialAI.Api;

/// <summary>
/// Broadcasts scene changes to connected SSE clients. Supports two transports:
/// <list type="bullet">
/// <item><b>full</b> (default): the entire scene JSON on every change — simplest, used by tiny scenes.</item>
/// <item><b>patch</b>: a baseline full snapshot on connect, then per-change deltas (upsert/remove keyed
/// by id) so edits are O(changed) instead of O(scene). Used by the scalable viewer path.</item>
/// </list>
/// The full path is preserved unchanged so the existing demo never regresses while the patch path scales.
/// </summary>
public sealed class SceneHub : IDisposable
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SceneStore _store;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();

    // Per-entity serialized JSON from the last broadcast, used to compute deltas.
    private Dictionary<Guid, string> _rooms = new();
    private Dictionary<Guid, string> _items = new();
    private Dictionary<Guid, string> _groups = new();

    private sealed record Client(Channel<string> Channel, bool Patch);

    public SceneHub(SceneStore store)
    {
        _store = store;
        _store.Changed += Broadcast;
    }

    /// <summary>The full scene as JSON (used by the REST endpoint and full-mode clients).</summary>
    public string CurrentJson() => JsonSerializer.Serialize(_store.Current, Json);

    /// <summary>
    /// Registers a client. The first message is its baseline (full scene for full mode, a
    /// <c>{"type":"full"}</c> envelope for patch mode); subsequent messages follow the chosen transport.
    /// </summary>
    public (Guid id, ChannelReader<string> reader) Subscribe(bool patch)
    {
        var channel = Channel.CreateUnbounded<string>();
        var id = Guid.NewGuid();
        lock (_gate)
        {
            channel.Writer.TryWrite(patch ? FullEnvelope() : CurrentJson());
            _clients[id] = new Client(channel, patch);
        }
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_clients.TryRemove(id, out var client))
            client.Channel.Writer.TryComplete();
    }

    private string FullEnvelope() =>
        JsonSerializer.Serialize(new { type = "full", scene = _store.Current }, Json);

    private void Broadcast()
    {
        string full;
        string patch;
        lock (_gate)
        {
            full = CurrentJson();
            patch = ComputePatch();
        }
        foreach (var client in _clients.Values)
            client.Channel.Writer.TryWrite(client.Patch ? patch : full);
    }

    /// <summary>Diffs the current scene against the last broadcast and advances the stored baseline.</summary>
    private string ComputePatch()
    {
        var scene = _store.Current;

        var (roomUpserts, roomRemoves, nextRooms) = Diff(scene.Rooms, r => r.Id, _rooms);
        var (itemUpserts, itemRemoves, nextItems) = Diff(scene.Items, i => i.Id, _items);
        var (groupUpserts, groupRemoves, nextGroups) = Diff(scene.Groups, g => g.Id, _groups);

        _rooms = nextRooms;
        _items = nextItems;
        _groups = nextGroups;

        var envelope = new
        {
            type = "patch",
            rooms = new { upsert = roomUpserts, remove = roomRemoves },
            items = new { upsert = itemUpserts, remove = itemRemoves },
            groups = new { upsert = groupUpserts, remove = groupRemoves },
            highlights = scene.Highlights, // transient + few: replace wholesale
            roof = scene.Roof              // small + rare: send each time (null = no building roof)
        };
        return JsonSerializer.Serialize(envelope, Json);
    }

    private static (List<T> upserts, List<Guid> removes, Dictionary<Guid, string> next) Diff<T>(
        IEnumerable<T> current, Func<T, Guid> idOf, Dictionary<Guid, string> previous)
    {
        var upserts = new List<T>();
        var next = new Dictionary<Guid, string>();
        foreach (var entity in current)
        {
            var id = idOf(entity);
            var json = JsonSerializer.Serialize(entity, Json);
            next[id] = json;
            if (!previous.TryGetValue(id, out var prior) || prior != json)
                upserts.Add(entity);
        }
        var removes = previous.Keys.Where(id => !next.ContainsKey(id)).ToList();
        return (upserts, removes, next);
    }

    public void Dispose() => _store.Changed -= Broadcast;
}
