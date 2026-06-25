using SpatialAI.Core.Model;

namespace SpatialAI.Api.Spaces;

/// <summary>A persisted space: identity, timestamps, and the full <see cref="Scene"/> (rooms, openings, items with parts).</summary>
public sealed class SpaceRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Scene Scene { get; set; } = new();

    /// <summary>The conversation transcript for this space (panel-render messages, persisted with the space).</summary>
    public List<ChatMessage> Chat { get; set; } = [];
}

/// <summary>One panel message: Kind is "user" | "ai" | "tool"; tool messages are tool-action strings.</summary>
public sealed record ChatMessage(string Kind, string Text);

/// <summary>Lightweight listing entry: identity, timestamps, counts, and a compact top-down preview.</summary>
public sealed record SpaceSummary(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    int RoomCount, int ItemCount, SpacePreview Preview);

/// <summary>Compact top-down geometry for drawing a space thumbnail (no full scene payload).</summary>
public sealed record SpacePreview(float MinX, float MinZ, float MaxX, float MaxZ,
    IReadOnlyList<PreviewRoom> Rooms, IReadOnlyList<PreviewItem> Items);

/// <summary>A room footprint: center (X,Z) and size (W along X, D along Z).</summary>
public sealed record PreviewRoom(float Cx, float Cz, float W, float D);

/// <summary>An item dot: floor position (X,Z) and a "#rrggbb" color.</summary>
public sealed record PreviewItem(float X, float Z, string Color);

/// <summary>The currently active space, plus whether it has been persisted to disk.</summary>
public sealed record CurrentSpaceInfo(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool Saved);
