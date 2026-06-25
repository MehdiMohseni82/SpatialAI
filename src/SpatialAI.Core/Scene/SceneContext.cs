using System.Text.Json;
using System.Text.Json.Serialization;
using SpatialAI.Core.Model;

namespace SpatialAI.Core.Scene;

/// <summary>
/// Builds a compact JSON snapshot of the scene for LLM context — rooms, groups, items, openings;
/// omits render-only data (parts, highlights). To stay token-bounded at scale, scenes with more than
/// <see cref="MaxDetailedItems"/> items are summarized (group counts, kind histogram, bounds) instead
/// of enumerating every item; the model can then drill in with query tools.
/// </summary>
public static class SceneContext
{
    /// <summary>Above this item count the snapshot summarizes rather than listing every item.</summary>
    public const int MaxDetailedItems = 40;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Returns a slim camelCase JSON projection of <paramref name="scene"/>.</summary>
    public static string ToJson(Model.Scene scene)
    {
        var rooms = scene.Rooms.Select(ProjectRoom).ToList();
        var groups = ProjectGroups(scene);

        Snapshot snapshot = scene.Items.Count <= MaxDetailedItems
            ? new Snapshot(rooms, groups, scene.Items.Select(i => ProjectItem(scene, i)).ToList(), null)
            : new Snapshot(rooms, groups, null, Summarize(scene));

        return JsonSerializer.Serialize(snapshot, Json);
    }

    private static float R(float v) => MathF.Round(v, 2);

    private static List<GroupSnap> ProjectGroups(Model.Scene scene)
    {
        if (scene.Groups.Count == 0) return [];
        var counts = scene.Items
            .Where(i => i.GroupId is not null)
            .GroupBy(i => i.GroupId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        return scene.Groups
            .Select(g => new GroupSnap(g.Name, counts.GetValueOrDefault(g.Id, 0)))
            .ToList();
    }

    private static SummarySnap Summarize(Model.Scene scene)
    {
        var byKind = scene.Items
            .GroupBy(i => i.Kind ?? i.Shape.ToString().ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
        return new SummarySnap(scene.Items.Count, byKind, ComputeBounds(scene));
    }

    private static BoundsSnap? ComputeBounds(Model.Scene scene)
    {
        var hasAny = false;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;

        void Extend(float cx, float cz, float halfX, float halfZ)
        {
            hasAny = true;
            minX = MathF.Min(minX, cx - halfX); maxX = MathF.Max(maxX, cx + halfX);
            minZ = MathF.Min(minZ, cz - halfZ); maxZ = MathF.Max(maxZ, cz + halfZ);
        }

        foreach (var room in scene.Rooms)
            Extend(room.Center.X, room.Center.Z, room.Width / 2f, room.Depth / 2f);
        foreach (var item in scene.Items)
            Extend(item.Position.X, item.Position.Z, item.Size.X / 2f, item.Size.Z / 2f);

        return hasAny ? new BoundsSnap(R(minX), R(maxX), R(minZ), R(maxZ)) : null;
    }

    private static RoomSnap ProjectRoom(Room room) => new(
        room.Name,
        new Vec2Snap(R(room.Center.X), R(room.Center.Z)),
        R(room.Width),
        R(room.Depth),
        R(room.Height),
        room.Ceiling,
        room.Roof,
        room.Openings.Where(o => o.Type == "window").Select(ProjectWindow).ToList(),
        room.Openings.Where(o => o.Type == "door").Select(ProjectDoor).ToList(),
        room.Partitions.Select(ProjectPartition).ToList());

    private static WindowSnap ProjectWindow(Opening o) =>
        new(o.Wall, R(o.Offset), R(o.Width), R(o.Height), R(o.Sill));

    private static DoorSnap ProjectDoor(Opening o) =>
        new(o.Wall, R(o.Offset), R(o.Width), R(o.Height));

    private static PartitionSnap ProjectPartition(Partition p) =>
        new(p.Axis, R(p.Position), R(p.DoorWidth));

    private static ItemSnap ProjectItem(Model.Scene scene, Item item) => new(
        item.Name,
        item.Kind ?? item.Shape.ToString().ToLowerInvariant(),
        new Vec3Snap(R(item.Position.X), R(item.Position.Y), R(item.Position.Z)),
        new Vec3Snap(R(item.Size.X), R(item.Size.Y), R(item.Size.Z)),
        R(item.RotationY),
        new ColorSnap(R(item.Color.R), R(item.Color.G), R(item.Color.B)),
        item.GroupId is { } gid ? scene.Groups.FirstOrDefault(g => g.Id == gid)?.Name : null);

    private sealed record Snapshot(
        IReadOnlyList<RoomSnap> Rooms,
        IReadOnlyList<GroupSnap> Groups,
        IReadOnlyList<ItemSnap>? Items,
        SummarySnap? Summary);

    private sealed record GroupSnap(string Name, int ItemCount);

    private sealed record SummarySnap(int ItemCount, IReadOnlyDictionary<string, int> ItemsByKind, BoundsSnap? Bounds);

    private sealed record BoundsSnap(float MinX, float MaxX, float MinZ, float MaxZ);

    private sealed record RoomSnap(
        string Name,
        Vec2Snap Center,
        float Width,
        float Depth,
        float Height,
        bool Ceiling,
        string Roof,
        IReadOnlyList<WindowSnap> Windows,
        IReadOnlyList<DoorSnap> Doors,
        IReadOnlyList<PartitionSnap> Partitions);

    private sealed record WindowSnap(string Wall, float Offset, float Width, float Height, float Sill);

    private sealed record DoorSnap(string Wall, float Offset, float Width, float Height);

    private sealed record PartitionSnap(string Axis, float Position, float DoorWidth);

    private sealed record ItemSnap(
        string Name,
        string Kind,
        Vec3Snap Position,
        Vec3Snap Size,
        float RotationY,
        ColorSnap Color,
        string? Group);

    private sealed record Vec2Snap(float X, float Z);

    private sealed record Vec3Snap(float X, float Y, float Z);

    private sealed record ColorSnap(float R, float G, float B);
}
