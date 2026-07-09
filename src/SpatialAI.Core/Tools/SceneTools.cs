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
        float? rotationY = null, string? onItem = null, string? faceItem = null,
        string? anchor = null)
    {
        Rgba? color = (colorR is not null && colorG is not null && colorB is not null)
            ? new Rgba(colorR.Value, colorG.Value, colorB.Value)
            : null;

        var (parts, size, primary, catalogKind, label) = BuildGeometry(kind, width, height, depth, color);

        return store.Mutate(s =>
        {
            var room = ResolveRoom(s, roomName);

            var (px, pz, py) = Place(s, room, size, rotationY ?? AnchorRotation(anchor, room) ?? 0f, positionX, positionZ, anchor, onItem, ignore: null);
            var storey = StoreyRoomAt(s, px, pz, room);  // the room the item actually sits in (null = outdoors)

            var item = new Item
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Item" : name,
                Shape = parts.Count == 1 ? parts[0].Shape : Shape.Box,
                Size = size,
                Position = new Vec3(px, py, pz),  // floor, or on top of onItem's surface
                RotationY = ResolveFacing(s, storey, catalogKind, px, pz, rotationY, faceItem, anchor),
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
    /// Computes a placement (x, z, y) for a new item of <paramref name="size"/>. When dropping ON a
    /// surface (<paramref name="onItem"/>) with no explicit coords/anchor, it centers on that surface;
    /// otherwise <see cref="Placement.Resolve"/> turns the anchor/coords into an in-bounds, non-overlapping
    /// spot. Y rests the item on that surface's top (or the storey floor).
    /// </summary>
    private static (float px, float pz, float py) Place(
        Model.Scene s, Room? room, Vec3 size, float rotationY,
        float? posX, float? posZ, string? anchor, string? onItem, Item? ignore)
    {
        var surface = string.IsNullOrWhiteSpace(onItem) ? null : FindItem(s, onItem);
        float px, pz;
        if (surface is not null && posX is null && posZ is null && string.IsNullOrWhiteSpace(anchor))
            { px = surface.Position.X; pz = surface.Position.Z; }
        else
            (px, pz) = Placement.Resolve(s, room, size, rotationY, posX, posZ, anchor, ignore);

        // Rest on the target's surface, else on the storey the point falls within (its elevation), else ground.
        var storey = StoreyRoomAt(s, px, pz, room);
        var baseY = surface is not null ? surface.Position.Y + surface.Size.Y / 2f : (storey?.Elevation ?? 0f);
        return (px, pz, baseY + size.Y / 2f);
    }

    /// <summary>
    /// The deterministic facing implied by a wall/corner <paramref name="anchor"/>, in degrees, or null
    /// for other anchors. Walls turn the item's front into the room (back to that wall); corners are
    /// placed unrotated here (their final north/south facing is resolved from the landed Z in
    /// <see cref="ResolveFacing"/>). Used as BOTH the footprint rotation while placing and the final
    /// facing, so a re-oriented item is guaranteed to stay in bounds.
    /// </summary>
    private static float? AnchorRotation(string? anchor, Room? room)
    {
        if (room is null) return null;
        var a = (anchor ?? "").Trim().ToLowerInvariant();
        if (a.StartsWith("corner")) return 0f;       // footprint stays axis-aligned; facing set later
        if (a.StartsWith("wall:"))
            return a["wall:".Length..].Trim() switch
            {
                "north" => 180f, "south" => 0f, "east" => -90f, "west" => 90f, _ => null
            };
        return null;
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
        { "desk", "computer_desk", "table", "dining_table", "coffee_table", "kitchen_island", "kitchen_counter", "workbench" };

    /// <summary>
    /// Resolves an item's Y rotation deterministically so the model never computes facing by hand:
    /// an explicit <paramref name="faceItem"/> target wins; else an explicit <paramref name="rotationY"/>;
    /// else a seat with no orientation auto-faces the nearest surface in its room; else 0 (faces +Z).
    /// </summary>
    private static float ResolveFacing(Model.Scene s, Room? room, string? kind,
        float px, float pz, float? rotationY, string? faceItem, string? anchor = null)
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
        // 4) Placed against a wall / into a corner → turn its front to the room (back to the wall).
        if (room is not null && !string.IsNullOrWhiteSpace(anchor))
        {
            if (anchor!.Trim().StartsWith("corner", StringComparison.OrdinalIgnoreCase))
                return pz > room.Center.Z ? 180f : 0f; // north corner faces south, and vice-versa
            if (AnchorRotation(anchor, room) is { } ar) return ar;
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
        string? onItem = null, string? faceItem = null, string? anchor = null)
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

            var (px, pz, py) = Place(s, room, built.Size, AnchorRotation(anchor, room) ?? 0f, positionX, positionZ, anchor, onItem, ignore: null);
            var storey = StoreyRoomAt(s, px, pz, room);

            var item = new Item
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Item" : name,
                Shape = Shape.Box,
                Size = built.Size,
                Position = new Vec3(px, py, pz),
                RotationY = ResolveFacing(s, storey, null, px, pz, null, faceItem, anchor),
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

    public string MoveItem(string itemName, float? positionX, float? positionZ, string? anchor = null)
        => store.Mutate(s =>
        {
            var item = FindItem(s, itemName);
            if (item is null) return $"No item matching '{itemName}'.";

            // Place it relative to the room it belongs to (or the latest room if it had drifted outdoors).
            var room = (item.RoomId is { } rid ? s.Rooms.FirstOrDefault(r => r.Id == rid) : null)
                       ?? (s.Rooms.Count > 0 ? s.Rooms[^1] : null);
            var anchorRot = AnchorRotation(anchor, room);
            var rot = anchorRot ?? item.RotationY;

            var (px, pz) = Placement.Resolve(s, room, item.Size, rot, positionX, positionZ, anchor, ignore: item);
            var storey = StoreyRoomAt(s, px, pz, room);

            item.RoomId = storey?.Id;
            item.Level = storey?.Level ?? 0;
            item.Position = new Vec3(px, (storey?.Elevation ?? 0f) + item.Size.Y / 2f, pz); // re-rest on the floor
            // Re-orient wall/corner placements to face into the room (reuses the create-time logic).
            if (storey is not null && anchor is not null && anchor.Trim().StartsWith("wall", StringComparison.OrdinalIgnoreCase))
                item.RotationY = anchorRot ?? item.RotationY;
            else if (storey is not null && anchor is not null && anchor.Trim().StartsWith("corner", StringComparison.OrdinalIgnoreCase))
                item.RotationY = pz > storey.Center.Z ? 180f : 0f;

            return $"Moved '{item.Name}' to ({px.ToString("F1", CI)}, {pz.ToString("F1", CI)})" +
                   (storey != null ? $" in '{storey.Name}'." : ".");
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

    // ── Groups (scene-graph: treat a line / zone as one unit) ─────────────────

    public string CreateGroup(string name, string? parentName = null)
        => store.Mutate(s =>
        {
            if (string.IsNullOrWhiteSpace(name)) return "A group needs a name.";
            if (FindGroup(s, name) is not null) return $"A group named '{name}' already exists.";
            var parent = string.IsNullOrWhiteSpace(parentName) ? null : FindGroup(s, parentName);
            s.Groups.Add(new Group { Name = name.Trim(), ParentId = parent?.Id });
            return $"Created group '{name.Trim()}'.";
        });

    public string AddToGroup(string groupName, IReadOnlyList<string> itemNames)
        => store.Mutate(s =>
        {
            if (string.IsNullOrWhiteSpace(groupName)) return "Specify a group name.";
            var group = FindGroup(s, groupName);
            if (group is null) { group = new Group { Name = groupName.Trim() }; s.Groups.Add(group); }
            var n = 0;
            foreach (var name in itemNames ?? [])
            {
                var item = FindItem(s, name);
                if (item is not null) { item.GroupId = group.Id; n++; }
            }
            return n == 0 ? $"No matching items to add to '{group.Name}'." : $"Added {n} item(s) to group '{group.Name}'.";
        });

    public string MoveGroup(string groupName, float? positionX, float? positionZ, string? anchor = null)
        => store.Mutate(s =>
        {
            var group = FindGroup(s, groupName);
            if (group is null) return $"No group matching '{groupName}'.";
            var members = s.Items.Where(i => i.GroupId == group.Id).ToList();
            if (members.Count == 0) return $"Group '{group.Name}' has no items to move.";

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var m in members)
            {
                var fp = Geometry.FootprintOf(m);
                minX = MathF.Min(minX, fp.MinX); maxX = MathF.Max(maxX, fp.MaxX);
                minZ = MathF.Min(minZ, fp.MinZ); maxZ = MathF.Max(maxZ, fp.MaxZ);
            }
            float cx = (minX + maxX) / 2f, cz = (minZ + maxZ) / 2f;

            float tx, tz;
            var room = members[0].RoomId is { } rid ? s.Rooms.FirstOrDefault(r => r.Id == rid)
                       : (s.Rooms.Count > 0 ? s.Rooms[^1] : null);
            if (!string.IsNullOrWhiteSpace(anchor) && room is not null)
            {
                // Treat the whole group as one footprint so it stays inside the room.
                var size = new Vec3(maxX - minX, 0, maxZ - minZ);
                (tx, tz) = Placement.Resolve(s, room, size, 0f, null, null, anchor, ignore: null);
            }
            else { tx = positionX ?? cx; tz = positionZ ?? cz; }

            float dx = tx - cx, dz = tz - cz;
            foreach (var m in members)
                m.Position = m.Position with { X = m.Position.X + dx, Z = m.Position.Z + dz };
            return $"Moved group '{group.Name}' ({members.Count} item(s)).";
        });

    public string DeleteGroup(string groupName, bool deleteItems = true)
        => store.Mutate(s =>
        {
            var group = FindGroup(s, groupName);
            if (group is null) return $"No group matching '{groupName}'.";
            var count = s.Items.Count(i => i.GroupId == group.Id);
            if (deleteItems) s.Items.RemoveAll(i => i.GroupId == group.Id);
            else foreach (var m in s.Items.Where(i => i.GroupId == group.Id)) m.GroupId = null;
            s.Groups.Remove(group);
            return deleteItems
                ? $"Deleted group '{group.Name}' and its {count} item(s)."
                : $"Disbanded group '{group.Name}' (kept {count} item(s)).";
        });

    // ── Layouts (build a whole system in one call; output is auto-grouped) ────

    /// <summary>A large industrial shell: big tall room, flat roof, concrete floor, and dock doors.</summary>
    public string CreateWarehouse(string name = "Warehouse", float width = 24f, float depth = 36f,
        float height = 8f, int dockDoors = 2)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Warehouse";
        width = MathF.Max(6f, width); depth = MathF.Max(6f, depth); height = MathF.Max(3f, height);
        dockDoors = Math.Clamp(dockDoors, 0, 8);

        CreateRoom(name, width, depth, 0f, 0f, height, 0, 0, ceiling: false, roof: "flat");
        store.Mutate(s =>
        {
            var room = s.Rooms.LastOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                       ?? s.Rooms.LastOrDefault();
            if (room is not null) room.FloorColor = Palette.Concrete;
            return 0;
        });
        for (var i = 0; i < dockDoors; i++)
            AddDoor(name, "south", (i - (dockDoors - 1) / 2f) * (width / (dockDoors + 1)), 3.0f, 4.0f);

        var doors = dockDoors > 0 ? $" with {dockDoors} dock door(s)" : "";
        return $"Built warehouse '{name}' ({width.ToString("F0", CI)} x {depth.ToString("F0", CI)} m){doors}.";
    }

    /// <summary>A conveyor spine with one machine station per segment, all facing the belt and grouped.</summary>
    public string CreateProductionLine(string name = "Production Line", int stations = 4,
        string? roomName = null, string? anchor = null, float? spacing = null)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Production Line";
        stations = Math.Clamp(stations, 1, 20);
        var sp = MathF.Max(1.5f, spacing ?? 2.5f);
        string[] machineKinds = ["cnc_machine", "robot_arm", "press", "workbench"];

        return store.Mutate(s =>
        {
            var room = ResolveRoom(s, roomName);
            var group = new Group { Name = name };
            s.Groups.Add(group);

            var conv = FurnitureFactory.Build("conveyor", null, null, null, null)!;
            float lineLen = (stations - 1) * sp;
            var footprint = new Vec3(lineLen + conv.Size.X, 0, conv.Size.Z + 3.0f);
            var (cx, cz) = room is not null
                ? Placement.Resolve(s, room, footprint, 0f, room.Center.X, room.Center.Z, anchor, ignore: null)
                : (0f, 0f);

            float startX = cx - lineLen / 2f;
            float backZ = cz - (conv.Size.Z / 2f + 1.0f); // machines sit behind the belt, facing it (+Z)
            for (var i = 0; i < stations; i++)
            {
                float x = startX + i * sp;
                AddCatalogItem(s, $"Conveyor {i + 1}", "conveyor", x, cz, room, group.Id, 0f);
                var kind = machineKinds[i % machineKinds.Length];
                var label = char.ToUpperInvariant(kind[0]) + kind[1..].Replace('_', ' ');
                AddCatalogItem(s, $"{label} {i + 1}", kind, x, backZ, room, group.Id, FaceToward(x, backZ, x, cz));
            }
            s.Highlights.Clear();
            return $"Built a {stations}-station production line (grouped as '{name}').";
        });
    }

    /// <summary>Rows of storage racks separated by aisles, centered in the room (or at an anchor), grouped.</summary>
    public string CreateRackAisles(string name = "Racking", int rows = 3, int racksPerRow = 4,
        float? aisleWidth = null, string rackKind = "pallet_rack", string? roomName = null, string? anchor = null)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Racking";
        rows = Math.Clamp(rows, 1, 12);
        racksPerRow = Math.Clamp(racksPerRow, 1, 20);
        var aisle = MathF.Max(1.0f, aisleWidth ?? 2.8f);
        if (!FurnitureFactory.IsKnown(rackKind)) rackKind = "pallet_rack";

        return store.Mutate(s =>
        {
            var room = ResolveRoom(s, roomName);
            var group = new Group { Name = name };
            s.Groups.Add(group);

            var rack = FurnitureFactory.Build(rackKind, null, null, null, null)!;
            float colPitch = rack.Size.X + 0.1f, rowPitch = rack.Size.Z + aisle;
            float gridW = racksPerRow * colPitch, gridD = rows * rowPitch;
            var (cx, cz) = room is not null
                ? Placement.Resolve(s, room, new Vec3(gridW, 0, gridD), 0f, room.Center.X, room.Center.Z, anchor, ignore: null)
                : (0f, 0f);

            float startX = cx - gridW / 2f + colPitch / 2f;
            float startZ = cz - gridD / 2f + rowPitch / 2f;
            var n = 0;
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < racksPerRow; c++)
                    AddCatalogItem(s, $"Rack {++n}", rackKind, startX + c * colPitch, startZ + r * rowPitch, room, group.Id, 0f);

            s.Highlights.Clear();
            return $"Laid out {rows} aisle(s) x {racksPerRow} = {rows * racksPerRow} racks (grouped as '{name}').";
        });
    }

    /// <summary>
    /// Rings a room's perimeter with a fence/wall built from FOUR thin segments (one per edge), grouped
    /// so the whole enclosure moves/deletes as a unit. Use this for "fence/wall around the yard": a single
    /// stretched fence would carpet the whole footprint instead of ringing it. Optionally leave one wall
    /// open as a gateway.
    /// </summary>
    public string EncloseRoom(string? roomName = null, string kind = "fence",
        float? height = null, string? gateWall = null)
    {
        var k = FurnitureFactory.IsKnown(kind) ? kind : "fence";
        var gate = string.IsNullOrWhiteSpace(gateWall) ? null : NormalizeWall(gateWall);

        return store.Mutate(s =>
        {
            var room = ResolveRoom(s, roomName);
            if (room is null) return "No room to enclose. Create a room first.";

            var group = new Group { Name = $"{room.Name} Fence" };
            s.Groups.Add(group);

            const float thickness = 0.1f;
            float cx = room.Center.X, cz = room.Center.Z;
            float hw = room.Width / 2f, hd = room.Depth / 2f;

            // Each edge: a thin segment as long as that wall, set just inside the footprint and turned to
            // run along the wall (N/S run along X at 0°; E/W run along Z at 90°).
            var sides = new[]
            {
                (wall: "north", len: room.Width, px: cx,                       pz: cz - hd + thickness / 2f, rot: 0f),
                (wall: "south", len: room.Width, px: cx,                       pz: cz + hd - thickness / 2f, rot: 0f),
                (wall: "west",  len: room.Depth, px: cx - hw + thickness / 2f, pz: cz,                       rot: 90f),
                (wall: "east",  len: room.Depth, px: cx + hw - thickness / 2f, pz: cz,                       rot: 90f),
            };

            var built = 0;
            foreach (var side in sides)
            {
                if (gate is not null && side.wall == gate) continue;
                var b = FurnitureFactory.Build(k, side.len, height, thickness, null);
                if (b is null) continue;
                s.Items.Add(new Item
                {
                    Name = $"{char.ToUpperInvariant(k[0])}{k[1..]} {char.ToUpperInvariant(side.wall[0])}{side.wall[1..]}",
                    Shape = b.Parts.Count == 1 ? b.Parts[0].Shape : Shape.Box,
                    Size = b.Size,
                    Position = new Vec3(side.px, room.Elevation + b.Size.Y / 2f, side.pz),
                    RotationY = side.rot,
                    Color = b.Primary,
                    Parts = b.Parts,
                    Kind = FurnitureFactory.Normalize(k),
                    RoomId = room.Id,
                    Level = room.Level,
                    GroupId = group.Id
                });
                built++;
            }

            s.Highlights.Clear();
            var opening = gate is not null ? $", open on the {gate} side" : "";
            return $"Enclosed '{room.Name}' with {built} {k} segment(s){opening} (grouped as '{group.Name}').";
        });
    }

    /// <summary>Builds a catalog item and adds it to the scene at (px,pz), resting on the floor, in a group.</summary>
    private static void AddCatalogItem(Model.Scene s, string name, string kind, float px, float pz,
        Room? room, Guid groupId, float rotationY)
    {
        var built = FurnitureFactory.Build(kind, null, null, null, null);
        if (built is null) return;
        var storey = StoreyRoomAt(s, px, pz, room);
        s.Items.Add(new Item
        {
            Name = name,
            Shape = built.Parts.Count == 1 ? built.Parts[0].Shape : Shape.Box,
            Size = built.Size,
            Position = new Vec3(px, (storey?.Elevation ?? 0f) + built.Size.Y / 2f, pz),
            RotationY = rotationY,
            Color = built.Primary,
            Parts = built.Parts,
            Kind = FurnitureFactory.Normalize(kind),
            RoomId = storey?.Id,
            Level = storey?.Level ?? 0,
            GroupId = groupId
        });
    }

    // ── Queries & analysis ──────────────────────────────────────────────────

    public string ListScene() => store.Read(s =>
    {
        if (s.Rooms.Count == 0 && s.Items.Count == 0) return "The scene is empty.";
        var sb = new StringBuilder();
        foreach (var room in s.Rooms)
            sb.AppendLine($"Room '{room.Name}': {room.Width.ToString("F1", CI)} x {room.Depth.ToString("F1", CI)} m at ({room.Center.X.ToString("F1", CI)}, {room.Center.Z.ToString("F1", CI)}).");
        foreach (var item in s.Items)
            sb.AppendLine($"- {item.Shape} '{item.Name}' at ({item.Position.X.ToString("F1", CI)}, {item.Position.Z.ToString("F1", CI)}), size {item.Size.X.ToString("F2", CI)}x{item.Size.Y.ToString("F2", CI)}x{item.Size.Z.ToString("F2", CI)} m.");
        foreach (var group in s.Groups)
            sb.AppendLine($"Group '{group.Name}': {s.Items.Count(i => i.GroupId == group.Id)} item(s).");
        return sb.ToString().TrimEnd();
    });

    public string FindUnusedAreas(string? roomName = null) => store.Mutate(s =>
    {
        var room = ResolveAnalysisRoom(s, roomName);
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
        var room = ResolveAnalysisRoom(s, roomName);
        var items = ItemsIn(s, room);
        var user = new Vec3(userX ?? room?.Center.X ?? 0f, 1.6f, userZ ?? room?.Center.Z ?? 0f);
        var result = ErgonomicsAnalyzer.Analyze(items, user);
        return ErgonomicsAnalyzer.Describe(result);
    });

    /// <summary>Deletes a room by name, along with the items inside it.</summary>
    public string DeleteRoom(string roomName) => store.Mutate(s =>
    {
        var room = s.Rooms.FirstOrDefault(r => string.Equals(r.Name, roomName, StringComparison.OrdinalIgnoreCase))
                ?? s.Rooms.FirstOrDefault(r => r.Name.Contains(roomName, StringComparison.OrdinalIgnoreCase));
        if (room is null) return $"No room matching '{roomName}'.";
        var removed = s.Items.RemoveAll(i => i.RoomId == room.Id);
        s.Rooms.Remove(room);
        s.Highlights.Clear();
        return removed > 0
            ? $"Deleted room '{room.Name}' and {removed} item(s) inside it."
            : $"Deleted room '{room.Name}'.";
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

    // For the analysis tools (ergonomics, unused areas): with no room named, analyze the room that
    // actually has furniture (the main workspace), not just the last room — which may be empty.
    private static Room? ResolveAnalysisRoom(Model.Scene s, string? roomName)
    {
        if (s.Rooms.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(roomName))
            return s.Rooms.FirstOrDefault(r => r.Name.Contains(roomName, StringComparison.OrdinalIgnoreCase)) ?? s.Rooms[^1];
        return s.Rooms.Where(r => s.Items.Any(i => i.RoomId == r.Id))
                      .OrderByDescending(r => s.Items.Count(i => i.RoomId == r.Id))
                      .FirstOrDefault() ?? s.Rooms[^1];
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

    private static Group? FindGroup(Model.Scene s, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return s.Groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? s.Groups.FirstOrDefault(g => g.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

}
