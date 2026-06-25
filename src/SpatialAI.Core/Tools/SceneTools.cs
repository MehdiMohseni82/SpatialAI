using System.Globalization;
using System.Text;
using SpatialAI.Core.Analysis;
using SpatialAI.Core.Furniture;
using SpatialAI.Core.Model;
using SpatialAI.Core.Scene;

namespace SpatialAI.Core.Tools;

/// <summary>
/// The spatial operations the LLM can invoke. Each method mutates the shared <see cref="SceneStore"/>
/// and returns a short human/LLM-readable result. This single class backs BOTH the in-app
/// function-calling loop and the MCP server, so the two never diverge.
/// </summary>
public sealed class SceneTools(SceneStore store)
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    // ── Creation ────────────────────────────────────────────────────────────

    public string CreateRoom(string name, float width = 4f, float depth = 4f,
        float centerX = 0f, float centerZ = 0f, float height = 2.5f,
        int windows = 0, int doors = 0, bool ceiling = false, string roof = "none",
        float elevation = 0f, int level = 0, bool inRoof = false)
    {
        var room = new Room
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Room" : name,
            Center = new Vec3(centerX, 0, centerZ),
            Width = MathF.Max(1f, width),
            Depth = MathF.Max(1f, depth),
            Height = MathF.Max(2f, height),
            Elevation = elevation,
            Level = level,
            InRoof = inRoof,
            Ceiling = ceiling,
            Roof = NormalizeRoof(roof)
        };
        AutoOpenings(room, windows, doors);
        store.Mutate(s => { s.Rooms.Add(room); s.Highlights.Clear(); return 0; });

        var extras = new List<string>();
        if (windows > 0) extras.Add($"{windows} window(s)");
        if (doors > 0) extras.Add($"{doors} door(s)");
        if (ceiling) extras.Add("a ceiling");
        if (room.Roof != "none") extras.Add($"a {room.Roof} roof");
        var suffix = extras.Count > 0 ? $" with {string.Join(", ", extras)}" : "";
        return $"Created room '{room.Name}' ({width.ToString("F1", CI)} x {depth.ToString("F1", CI)} m){suffix}.";
    }

    // ── Room shell (windows, doors, ceiling, roof, partitions) ───────────────

    public string AddWindow(string? roomName, string wall, float? offset = null, float? width = null, float? height = null, float? sill = null)
        => WithRoom(roomName, room =>
        {
            var w = NormalizeWall(wall);
            room.Openings.Add(new Opening { Wall = w, Type = "window", Offset = offset ?? 0f, Width = width ?? 1.2f, Height = height ?? 1.2f, Sill = sill ?? 0.9f });
            return $"Added a window to the {w} wall of '{room.Name}'.";
        });

    public string AddDoor(string? roomName, string wall, float? offset = null, float? width = null, float? height = null)
        => WithRoom(roomName, room =>
        {
            var w = NormalizeWall(wall);
            room.Openings.Add(new Opening { Wall = w, Type = "door", Offset = offset ?? 0f, Width = width ?? 0.9f, Height = height ?? 2.1f, Sill = 0f });
            return $"Added a door to the {w} wall of '{room.Name}'.";
        });

    public string AddPartition(string? roomName, string axis, float position, float? doorWidth = null)
        => WithRoom(roomName, room =>
        {
            room.Partitions.Add(new Partition { Axis = axis?.Trim().ToLowerInvariant() == "z" ? "z" : "x", Position = position, DoorWidth = doorWidth ?? 0f });
            return $"Added a partition wall to '{room.Name}'.";
        });

    public string SetCeiling(string? roomName, bool on)
        => WithRoom(roomName, room => { room.Ceiling = on; return $"{(on ? "Closed" : "Opened")} the ceiling of '{room.Name}'."; });

    public string SetRoof(string? roomName, string style)
        => WithRoom(roomName, room => { room.Roof = NormalizeRoof(style); return $"Set the roof of '{room.Name}' to {room.Roof}."; });

    /// <summary>
    /// Sets a building-wide roof spanning the whole footprint (all rooms). Footprint and base height are
    /// computed from the current rooms; pass "none" to remove it. Style: flat | gable | hip | mansard.
    /// </summary>
    public string SetBuildingRoof(string style, float? height = null, int dormers = 0,
        float? baseY = null, float? ridgeY = null, float? breakY = null)
        => store.Mutate(s =>
        {
            var st = NormalizeBuildingRoof(style);
            if (st == "none" || s.Rooms.Count == 0) { s.Roof = null; return st == "none" ? "Removed the building roof." : "No rooms to roof."; }

            // The roof footprint is the union of the TOP-level rooms; its base is the EAVE — passed explicitly
            // by an import (top of the masonry wall), else the tallest wall top of the current rooms.
            var topLevel = s.Rooms.Max(r => r.Level);
            var topRooms = s.Rooms.Where(r => r.Level == topLevel).ToList();
            if (topRooms.Count == 0) topRooms = s.Rooms;

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var r in topRooms)
            {
                minX = MathF.Min(minX, r.Center.X - r.Width / 2f); maxX = MathF.Max(maxX, r.Center.X + r.Width / 2f);
                minZ = MathF.Min(minZ, r.Center.Z - r.Depth / 2f); maxZ = MathF.Max(maxZ, r.Center.Z + r.Depth / 2f);
            }
            var span = MathF.Min(maxX - minX, maxZ - minZ);
            var b = baseY ?? s.Rooms.Max(r => r.Elevation + r.Height);
            var h = ridgeY is { } rg ? MathF.Max(0.8f, rg - b) : Math.Clamp(height ?? span * 0.35f, 1.0f, span * 0.45f);
            s.Roof = new BuildingRoof
            {
                Style = st, MinX = minX, MinZ = minZ, MaxX = maxX, MaxZ = maxZ,
                BaseY = b, Height = h, BreakY = breakY ?? 0f, Dormers = Math.Clamp(dormers, 0, 8)
            };
            return $"Set a {st} roof over the building ({(maxX - minX).ToString("F1", CI)} x {(maxZ - minZ).ToString("F1", CI)} m).";
        });

    public string CreateItem(string name, string kind = "box",
        float? width = null, float? height = null, float? depth = null,
        float? colorR = null, float? colorG = null, float? colorB = null,
        float? positionX = null, float? positionZ = null, string? roomName = null,
        float? rotationY = null, string? onItem = null, string? faceItem = null)
    {
        Rgba? color = (colorR is not null && colorG is not null && colorB is not null)
            ? new Rgba(colorR.Value, colorG.Value, colorB.Value)
            : null;

        var (parts, size, primary, catalogKind, label) = BuildGeometry(kind, width, height, depth, color);

        return store.Mutate(s =>
        {
            var room = ResolveRoom(s, roomName);

            var (px, pz, py) = Place(s, room, size, positionX, positionZ, onItem);
            var storey = StoreyRoomAt(s, px, pz, room);  // the room the item actually sits in (null = outdoors)

            var item = new Item
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Item" : name,
                Shape = parts.Count == 1 ? parts[0].Shape : Shape.Box,
                Size = size,
                Position = new Vec3(px, py, pz),  // floor, or on top of onItem's surface
                RotationY = ResolveFacing(s, storey, catalogKind, px, pz, rotationY, faceItem),
                Color = primary,
                Parts = parts,
                Kind = catalogKind,
                RoomId = storey?.Id,
                Level = storey?.Level ?? 0
            };
            s.Items.Add(item);
            s.Highlights.Clear();
            return $"Created {label} '{item.Name}' " +
                   $"({size.X.ToString("F2", CI)} x {size.Y.ToString("F2", CI)} x {size.Z.ToString("F2", CI)} m) " +
                   $"at ({px.ToString("F1", CI)}, {pz.ToString("F1", CI)})" +
                   (room != null ? $" in '{room.Name}'." : ".");
        });
    }

    /// <summary>
    /// Computes a placement (x, z, y) for a new item of <paramref name="size"/>: x/z from explicit
    /// coords, else the surface center if <paramref name="onItem"/> resolves, else auto-placed; y rests
    /// the item on that surface's top (or the floor).
    /// </summary>
    private static (float px, float pz, float py) Place(
        Model.Scene s, Room? room, Vec3 size, float? posX, float? posZ, string? onItem)
    {
        var surface = string.IsNullOrWhiteSpace(onItem) ? null : FindItem(s, onItem);
        float px, pz;
        if (posX is not null && posZ is not null) { px = posX.Value; pz = posZ.Value; }
        else if (surface is not null) { px = surface.Position.X; pz = surface.Position.Z; }
        else { var (x, z) = SuggestPlacement(s, room, size); px = x; pz = z; }

        // Rest on the target's surface, else on the storey the point falls within (its elevation), else ground.
        var storey = StoreyRoomAt(s, px, pz, room);
        var baseY = surface is not null ? surface.Position.Y + surface.Size.Y / 2f : (storey?.Elevation ?? 0f);
        return (px, pz, baseY + size.Y / 2f);
    }

    private static bool RoomContains(Room r, float x, float z)
        => x >= r.Center.X - r.Width / 2f && x <= r.Center.X + r.Width / 2f
        && z >= r.Center.Z - r.Depth / 2f && z <= r.Center.Z + r.Depth / 2f;

    /// <summary>
    /// The storey a point belongs to: the <paramref name="preferred"/> room if it contains the point, else the
    /// last room whose footprint contains it, else null (outdoors / ground level).
    /// </summary>
    private static Room? StoreyRoomAt(Model.Scene s, float x, float z, Room? preferred)
    {
        if (preferred is not null && RoomContains(preferred, x, z)) return preferred;
        Room? found = null;
        foreach (var r in s.Rooms) if (RoomContains(r, x, z)) found = r;
        return found;
    }

    // Seats that should face a work/dining surface, and the surfaces they face.
    private static readonly HashSet<string> SeatKinds = new(StringComparer.OrdinalIgnoreCase)
        { "chair", "stool", "armchair", "office_chair", "dining_chair", "bench" };
    private static readonly HashSet<string> SurfaceKinds = new(StringComparer.OrdinalIgnoreCase)
        { "desk", "table", "dining_table", "coffee_table", "kitchen_island", "kitchen_counter" };

    /// <summary>
    /// Resolves an item's Y rotation deterministically so the model never computes facing by hand:
    /// an explicit <paramref name="faceItem"/> target wins; else an explicit <paramref name="rotationY"/>;
    /// else a seat with no orientation auto-faces the nearest surface in its room; else 0 (faces +Z).
    /// </summary>
    private static float ResolveFacing(Model.Scene s, Room? room, string? kind,
        float px, float pz, float? rotationY, string? faceItem)
    {
        // 1) Explicit target wins (deterministic, general — any item).
        var target = string.IsNullOrWhiteSpace(faceItem) ? null : FindItem(s, faceItem);
        if (target is not null) return FaceToward(px, pz, target.Position.X, target.Position.Z);
        // 2) Caller gave an explicit angle.
        if (rotationY is not null) return rotationY.Value;
        // 3) A seat with no orientation auto-faces the nearest surface in its room.
        if (!string.IsNullOrEmpty(kind) && SeatKinds.Contains(kind))
        {
            var near = ItemsIn(s, room)
                .Where(i => SurfaceKinds.Contains(i.Kind ?? ""))
                .OrderBy(i => Dist2(px, pz, i.Position.X, i.Position.Z))
                .FirstOrDefault();
            if (near is not null) return FaceToward(px, pz, near.Position.X, near.Position.Z);
        }
        return 0f;
    }

    private static float Dist2(float ax, float az, float bx, float bz)
        => (ax - bx) * (ax - bx) + (az - bz) * (az - bz);

    /// <summary>
    /// Places <paramref name="count"/> items of <paramref name="kind"/> on top of a target's surface,
    /// spread on a ring inset from its edge (e.g. plates on a table).
    /// </summary>
    public string ArrangeOn(string targetName, string kind = "plate", int count = 1,
        float? radius = null, string? roomName = null)
    {
        if (count < 1) count = 1;
        return store.Mutate(s =>
        {
            var target = FindItem(s, targetName);
            if (target is null) return $"No item matching '{targetName}' to place items on.";

            var topY = target.Position.Y + target.Size.Y / 2f;
            var label = char.ToUpperInvariant(kind[0]) + kind[1..].Replace('_', ' ');
            float tx = target.Position.X, tz = target.Position.Z;

            for (var i = 0; i < count; i++)
            {
                var (parts, size, primary, catalogKind, _) = BuildGeometry(kind, null, null, null, null);
                float r = count == 1 ? 0f
                    : radius ?? MathF.Max(0.05f, MathF.Min(target.Size.X, target.Size.Z) / 2f - MathF.Max(size.X, size.Z) / 2f - 0.05f);
                var a = MathF.Tau * i / count;
                float px = tx + r * MathF.Sin(a), pz = tz + r * MathF.Cos(a);
                s.Items.Add(new Item
                {
                    Name = $"{label} {i + 1}",
                    Shape = parts.Count == 1 ? parts[0].Shape : Shape.Box,
                    Size = size,
                    Position = new Vec3(px, topY + size.Y / 2f, pz),
                    Color = primary,
                    Parts = parts,
                    Kind = catalogKind,
                    RoomId = target.RoomId
                });
            }
            s.Highlights.Clear();
            return $"Placed {count} {kind}(s) on '{target.Name}'.";
        });
    }

    /// <summary>
    /// Places <paramref name="count"/> items of <paramref name="kind"/> evenly on a ring around a target
    /// item, each rotated to face the target's center (e.g. chairs around a table).
    /// </summary>
    public string ArrangeAround(string targetName, string kind = "chair", int count = 4,
        float? radius = null, string? roomName = null)
    {
        if (count < 1) count = 1;
        return store.Mutate(s =>
        {
            var target = FindItem(s, targetName);
            if (target is null) return $"No item matching '{targetName}' to arrange around.";

            var r = radius ?? (MathF.Max(target.Size.X, target.Size.Z) / 2f + 0.35f);
            float tx = target.Position.X, tz = target.Position.Z;
            var label = char.ToUpperInvariant(kind[0]) + kind[1..].Replace('_', ' ');

            for (var i = 0; i < count; i++)
            {
                var a = MathF.Tau * i / count;
                float px = tx + r * MathF.Sin(a), pz = tz + r * MathF.Cos(a);
                var (parts, size, primary, catalogKind, _) = BuildGeometry(kind, null, null, null, null);
                s.Items.Add(new Item
                {
                    Name = $"{label} {i + 1}",
                    Shape = parts.Count == 1 ? parts[0].Shape : Shape.Box,
                    Size = size,
                    Position = new Vec3(px, size.Y / 2f, pz),
                    RotationY = FaceToward(px, pz, tx, tz),
                    Color = primary,
                    Parts = parts,
                    Kind = catalogKind,
                    RoomId = target.RoomId
                });
            }
            s.Highlights.Clear();
            return $"Placed {count} {kind}(s) around '{target.Name}', each facing it.";
        });
    }

    /// <summary>Degrees to rotate an item at (fromX,fromZ) so its +Z face points at (targetX,targetZ).</summary>
    public static float FaceToward(float fromX, float fromZ, float targetX, float targetZ)
        => MathF.Atan2(targetX - fromX, targetZ - fromZ) * 180f / MathF.PI;

    /// <summary>
    /// Builds an item from natural-language NL parts: a furniture <c>kind</c> via the catalog, a plain
    /// primitive (box/cylinder/sphere), or a single labelled box fallback for an unknown kind.
    /// </summary>
    private static (List<Part> parts, Vec3 size, Rgba primary, string? kind, string label) BuildGeometry(
        string kind, float? w, float? h, float? d, Rgba? color)
    {
        if (FurnitureFactory.IsKnown(kind))
        {
            var built = FurnitureFactory.Build(kind, w, h, d, color)!;
            var norm = FurnitureFactory.Normalize(kind);
            return (built.Parts, built.Size, built.Primary, norm, norm.Replace('_', ' '));
        }

        var shape = Enum.TryParse<Shape>(kind, ignoreCase: true, out var sh) ? sh : Shape.Box;
        var size = new Vec3(w ?? 0.5f, h ?? 0.5f, d ?? 0.5f);
        var primary = color ?? Rgba.Gray;
        var parts = new List<Part> { new() { Shape = shape, Offset = Vec3.Zero, Size = size, Color = primary } };
        return (parts, size, primary, null, shape.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// Builds an item from an explicit list of primitive parts (the LLM-compose fallback). Offsets are
    /// floor-relative (Y up from 0); they're recentered to the bounding-box center on store.
    /// </summary>
    public string ComposeItem(string name, IReadOnlyList<Part> parts,
        float? positionX = null, float? positionZ = null, string? roomName = null,
        string? onItem = null, string? faceItem = null)
    {
        if (parts is null || parts.Count == 0) return "compose_item needs at least one part.";

        var list = parts.Select(p => new Part
        {
            Shape = p.Shape, Offset = p.Offset, Size = p.Size, RotationY = p.RotationY, Color = p.Color
        }).ToList();
        var primary = list[0].Color;
        var built = FurnitureFactory.Finalize(list, primary);

        return store.Mutate(s =>
        {
            var room = ResolveRoom(s, roomName);

            var (px, pz, py) = Place(s, room, built.Size, positionX, positionZ, onItem);
            var storey = StoreyRoomAt(s, px, pz, room);

            var item = new Item
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Item" : name,
                Shape = Shape.Box,
                Size = built.Size,
                Position = new Vec3(px, py, pz),
                RotationY = ResolveFacing(s, storey, null, px, pz, null, faceItem),
                Color = primary,
                Parts = built.Parts,
                RoomId = storey?.Id,
                Level = storey?.Level ?? 0
            };
            s.Items.Add(item);
            s.Highlights.Clear();
            return $"Composed '{item.Name}' from {built.Parts.Count} part(s) " +
                   $"({built.Size.X.ToString("F2", CI)} x {built.Size.Y.ToString("F2", CI)} x {built.Size.Z.ToString("F2", CI)} m)" +
                   (room != null ? $" in '{room.Name}'." : ".");
        });
    }

    // ── Manipulation ────────────────────────────────────────────────────────

    public string MoveItem(string itemName, float positionX, float positionZ)
        => WithItem(itemName, item =>
        {
            item.Position = item.Position with { X = positionX, Z = positionZ };
            return $"Moved '{item.Name}' to ({positionX.ToString("F1", CI)}, {positionZ.ToString("F1", CI)}).";
        });

    public string RotateItem(string itemName, float degrees)
        => WithItem(itemName, item =>
        {
            item.RotationY = (item.RotationY + degrees) % 360f;
            return $"Rotated '{item.Name}' to {item.RotationY.ToString("F0", CI)}°.";
        });

    public string ScaleItem(string itemName, float factor)
        => WithItem(itemName, item =>
        {
            var f = MathF.Max(0.05f, factor);
            item.Size = new Vec3(item.Size.X * f, item.Size.Y * f, item.Size.Z * f);
            foreach (var p in item.Parts)
            {
                p.Offset = new Vec3(p.Offset.X * f, p.Offset.Y * f, p.Offset.Z * f);
                p.Size = new Vec3(p.Size.X * f, p.Size.Y * f, p.Size.Z * f);
            }
            item.Position = item.Position with { Y = item.Size.Y / 2f };
            return $"Scaled '{item.Name}' by {f.ToString("F2", CI)}x.";
        });

    public string RecolorItem(string itemName, float colorR, float colorG, float colorB)
        => WithItem(itemName, item =>
        {
            var newColor = new Rgba(colorR, colorG, colorB);
            item.Color = newColor;
            if (item.Kind is not null && FurnitureFactory.IsKnown(item.Kind))
            {
                // Rebuild from the catalog so the per-part palette re-derives from the new color.
                var rebuilt = FurnitureFactory.Build(item.Kind, item.Size.X, item.Size.Y, item.Size.Z, newColor)!;
                item.Parts = rebuilt.Parts;
            }
            else
            {
                foreach (var p in item.Parts) p.Color = newColor;
            }
            return $"Recolored '{item.Name}'.";
        });

    public string DeleteItem(string itemName)
        => store.Mutate(s =>
        {
            var item = FindItem(s, itemName);
            if (item is null) return $"No item matching '{itemName}'.";
            s.Items.Remove(item);
            return $"Deleted '{item.Name}'.";
        });

    // ── Queries & analysis ──────────────────────────────────────────────────

    public string ListScene() => store.Read(s =>
    {
        if (s.Rooms.Count == 0 && s.Items.Count == 0) return "The scene is empty.";
        var sb = new StringBuilder();
        foreach (var room in s.Rooms)
            sb.AppendLine($"Room '{room.Name}': {room.Width.ToString("F1", CI)} x {room.Depth.ToString("F1", CI)} m at ({room.Center.X.ToString("F1", CI)}, {room.Center.Z.ToString("F1", CI)}).");
        foreach (var item in s.Items)
            sb.AppendLine($"- {item.Shape} '{item.Name}' at ({item.Position.X.ToString("F1", CI)}, {item.Position.Z.ToString("F1", CI)}), size {item.Size.X.ToString("F2", CI)}x{item.Size.Y.ToString("F2", CI)}x{item.Size.Z.ToString("F2", CI)} m.");
        return sb.ToString().TrimEnd();
    });

    public string FindUnusedAreas(string? roomName = null) => store.Mutate(s =>
    {
        var room = ResolveRoom(s, roomName);
        var items = ItemsIn(s, room);
        var result = UnusedAreaAnalyzer.Analyze(room, items);

        s.Highlights.Clear();
        foreach (var r in result.Regions)
            s.Highlights.Add(new Highlight
            {
                Position = new Vec3(r.Center.X, 0.02f, r.Center.Z),
                Size = new Vec3(r.Width, 0.04f, r.Depth),
                Label = $"{r.AreaM2.ToString("F1", CI)} m² free"
            });

        return UnusedAreaAnalyzer.Describe(result);
    });

    public string AnalyzeErgonomics(string? roomName = null, float? userX = null, float? userZ = null) => store.Read(s =>
    {
        var room = ResolveRoom(s, roomName);
        var items = ItemsIn(s, room);
        var user = new Vec3(userX ?? room?.Center.X ?? 0f, 1.6f, userZ ?? room?.Center.Z ?? 0f);
        var result = ErgonomicsAnalyzer.Analyze(items, user);
        return ErgonomicsAnalyzer.Describe(result);
    });

    // ── Helpers ─────────────────────────────────────────────────────────────

    private string WithItem(string itemName, Func<Item, string> action) => store.Mutate(s =>
    {
        var item = FindItem(s, itemName);
        return item is null ? $"No item matching '{itemName}'." : action(item);
    });

    private static Room? ResolveRoom(Model.Scene s, string? roomName)
    {
        if (s.Rooms.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(roomName)) return s.Rooms[^1];
        return s.Rooms.FirstOrDefault(r => r.Name.Contains(roomName, StringComparison.OrdinalIgnoreCase))
               ?? s.Rooms[^1];
    }

    private string WithRoom(string? roomName, Func<Room, string> action) => store.Mutate(s =>
    {
        var room = ResolveRoom(s, roomName);
        if (room is null) return "No room to modify. Create a room first.";
        s.Highlights.Clear();
        return action(room);
    });

    private static void AutoOpenings(Room room, int windows, int doors)
    {
        string[] order = ["north", "east", "west", "south"];
        for (var i = 0; i < windows; i++)
            room.Openings.Add(new Opening { Wall = order[i % order.Length], Type = "window", Offset = 0f, Width = 1.2f, Height = 1.2f, Sill = 0.9f });
        for (var i = 0; i < doors; i++)
            room.Openings.Add(new Opening { Wall = "south", Type = "door", Offset = (i - (doors - 1) / 2f) * 1.2f, Width = 0.9f, Height = 2.1f, Sill = 0f });
    }

    private static string NormalizeWall(string? wall) => (wall ?? "").Trim().ToLowerInvariant() switch
    {
        "south" or "front" => "south",
        "east" or "right" => "east",
        "west" or "left" => "west",
        _ => "north",
    };

    private static string NormalizeRoof(string? roof) => (roof ?? "").Trim().ToLowerInvariant() switch
    {
        "flat" => "flat",
        "gable" or "pitched" or "pitch" or "pitched_roof" => "gable",
        _ => "none",
    };

    private static string NormalizeBuildingRoof(string? roof) => (roof ?? "").Trim().ToLowerInvariant() switch
    {
        "flat" => "flat",
        "gable" or "pitched" or "pitch" => "gable",
        "hip" or "hipped" => "hip",
        "mansard" or "gambrel" => "mansard",
        _ => "none",
    };

    private static List<Item> ItemsIn(Model.Scene s, Room? room) =>
        room is null ? s.Items : s.Items.Where(i => i.RoomId == room.Id || i.RoomId is null).ToList();

    private static Item? FindItem(Model.Scene s, string name)
    {
        if (string.Equals(name, "last", StringComparison.OrdinalIgnoreCase))
            return s.Items.Count > 0 ? s.Items[^1] : null;
        return s.Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? s.Items.FirstOrDefault(i => i.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static (float x, float z) SuggestPlacement(Model.Scene s, Room? room, Vec3 size)
    {
        if (room is null) return (0f, 0f);
        var result = UnusedAreaAnalyzer.Analyze(room, ItemsIn(s, room));
        // Prefer the center of the largest region that can fit the item.
        var region = result.Regions.FirstOrDefault(r => r.Width >= size.X && r.Depth >= size.Z);
        if (region is not null) return (region.Center.X, region.Center.Z);
        return (room.Center.X, room.Center.Z);
    }
}
