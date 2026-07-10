using SpatialAI.Core.Scene;

namespace SpatialAI.Api.Spaces;

/// <summary>
/// Owns the single active space for the server (single-user demo model) and coordinates the live
/// <see cref="SceneStore"/> with the on-disk <see cref="SpaceRepository"/>: new / save / save-as /
/// open / rename / delete.
/// </summary>
public sealed class SpaceManager
{
    private readonly SceneStore _store;
    private readonly SpaceRepository _repo;
    private readonly Lock _gate = new();
    private SpaceRecord _current = new() { Name = "Untitled" };

    public SpaceManager(SceneStore store, SpaceRepository repo)
    {
        _store = store;
        _repo = repo;
    }

    public CurrentSpaceInfo Current
    {
        get
        {
            lock (_gate)
            {
                return new CurrentSpaceInfo(_current.Id, _current.Name, _current.CreatedAt, _current.UpdatedAt, _repo.Exists(_current.Id));
            }
        }
    }

    public IReadOnlyList<SpaceSummary> List() => _repo.List();

    /// <summary>Starts a fresh, empty, unsaved space and makes it active.</summary>
    public CurrentSpaceInfo NewSpace(string name)
    {
        lock (_gate)
        {
            _current = new SpaceRecord { Name = Clean(name, "Untitled") };
        }
        _store.Load(new SpatialAI.Core.Model.Scene());
        return Current;
    }

    /// <summary>Persists the current scene under the current space record.</summary>
    public CurrentSpaceInfo Save()
    {
        lock (_gate)
        {
            _current.Scene = _store.Current;
            _current.UpdatedAt = DateTimeOffset.UtcNow;
            _repo.Save(_current);
        }
        return Current;
    }

    /// <summary>Persists the current scene as a brand-new space (new id) and makes it active.</summary>
    public CurrentSpaceInfo SaveAs(string name)
    {
        lock (_gate)
        {
            _current = new SpaceRecord { Name = Clean(name, "Untitled"), Scene = _store.Current, Chat = _current.Chat };
            _repo.Save(_current);
        }
        return Current;
    }

    public CurrentSpaceInfo? Open(Guid id)
    {
        var record = _repo.Load(id);
        if (record is null) return null;
        lock (_gate) { _current = record; }
        _store.Load(record.Scene);
        return Current;
    }

    public CurrentSpaceInfo? OpenByName(string name)
    {
        var match = ResolveByName(name);
        return match is null ? null : Open(match.Id);
    }

    public bool Rename(Guid id, string name)
    {
        var ok = _repo.Rename(id, name);
        if (ok)
            lock (_gate)
            {
                if (_current.Id == id) _current.Name = Clean(name, _current.Name);
            }
        return ok;
    }

    public bool Delete(Guid id) => _repo.Delete(id);

    /// <summary>The conversation transcript of the active space.</summary>
    public IReadOnlyList<ChatMessage> CurrentChat()
    {
        lock (_gate) { return _current.Chat.ToList(); }
    }

    /// <summary>
    /// Appends messages to the active space's transcript. If that space is already persisted, re-writes it so the
    /// chat survives a reopen — without bumping <c>UpdatedAt</c> (chatting shouldn't reorder the gallery).
    /// </summary>
    public void AppendChat(IEnumerable<ChatMessage> messages)
    {
        lock (_gate)
        {
            _current.Chat.AddRange(messages);
            if (_repo.Exists(_current.Id)) _repo.Save(_current);
        }
    }

    /// <summary>Clears the active space's conversation transcript (the LLM's context memory), re-writing the
    /// space if it is already persisted. Used by Reset so the scene and the chat clear together.</summary>
    public void ClearChat()
    {
        lock (_gate)
        {
            _current.Chat.Clear();
            if (_repo.Exists(_current.Id)) _repo.Save(_current);
        }
    }

    /// <summary>Saves a copy of an existing space under a new id/name without changing the active space.</summary>
    public SpaceSummary? Duplicate(Guid id)
    {
        var source = _repo.Load(id);
        if (source is null) return null;
        var copy = new SpaceRecord { Name = Clean($"{source.Name} copy", "Untitled"), Scene = source.Scene, Chat = source.Chat };
        _repo.Save(copy);
        return new SpaceSummary(copy.Id, copy.Name, copy.CreatedAt, copy.UpdatedAt,
            copy.Scene.Rooms.Count, copy.Scene.Items.Count, SpacePreviewBuilder.Build(copy.Scene));
    }

    public SpaceSummary? ResolveByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var spaces = _repo.List();
        return spaces.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? spaces.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string Clean(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
}
