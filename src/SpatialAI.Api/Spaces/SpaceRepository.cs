using System.Text.Json;

namespace SpatialAI.Api.Spaces;

/// <summary>
/// Persists spaces as one JSON file per space (<c>{id}.json</c>) under a configurable directory.
/// Serializes the full scene (rooms, openings, items with parts) using camelCase, matching the viewer.
/// </summary>
public sealed class SpaceRepository
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _dir;

    public SpaceRepository(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
    }

    private string PathFor(Guid id) => Path.Combine(_dir, $"{id:N}.json");

    /// <summary>All saved spaces, newest-updated first. Skips any file that fails to parse.</summary>
    public IReadOnlyList<SpaceSummary> List()
    {
        var summaries = new List<SpaceSummary>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            var record = TryRead(file);
            if (record is not null)
                summaries.Add(new SpaceSummary(record.Id, record.Name, record.CreatedAt, record.UpdatedAt,
                    record.Scene.Rooms.Count, record.Scene.Items.Count, SpacePreviewBuilder.Build(record.Scene)));
        }
        return summaries.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    public SpaceRecord? Load(Guid id)
    {
        var file = PathFor(id);
        return File.Exists(file) ? TryRead(file) : null;
    }

    public void Save(SpaceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var json = JsonSerializer.Serialize(record, Json);
        File.WriteAllText(PathFor(record.Id), json);
    }

    public bool Exists(Guid id) => File.Exists(PathFor(id));

    public bool Rename(Guid id, string name)
    {
        var record = Load(id);
        if (record is null) return false;
        record.Name = string.IsNullOrWhiteSpace(name) ? record.Name : name.Trim();
        record.UpdatedAt = DateTimeOffset.UtcNow;
        Save(record);
        return true;
    }

    public bool Delete(Guid id)
    {
        var file = PathFor(id);
        if (!File.Exists(file)) return false;
        File.Delete(file);
        return true;
    }

    private static SpaceRecord? TryRead(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceRecord>(File.ReadAllText(file), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
