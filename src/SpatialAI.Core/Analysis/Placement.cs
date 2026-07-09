using SpatialAI.Core.Model;

namespace SpatialAI.Core.Analysis;

/// <summary>
/// Deterministic, server-side placement: turns a desired position OR a semantic anchor into a concrete
/// (x, z) that keeps an item's footprint INSIDE its room and CLEAR of other items. The model expresses
/// intent ("corner", "against the north wall", "next to the desk") and the geometry is computed here —
/// the same philosophy used for facing in <see cref="Tools"/>.SceneTools, so the LLM never has to do math.
/// </summary>
public static class Placement
{
    public const float DefaultMargin = 0.1f;
    private const float Clearance = 0.05f;   // smallest allowed gap to another item
    private const float Step = 0.25f;        // spiral search resolution

    /// <summary>
    /// Resolves the floor position for an item of <paramref name="size"/> (rotated by
    /// <paramref name="rotationY"/>). Precedence: <paramref name="anchor"/> → explicit desired coords →
    /// auto free-space. The result is clamped inside <paramref name="room"/> and nudged off any overlap
    /// with other items (excluding <paramref name="ignore"/>, the item being moved).
    /// </summary>
    public static (float x, float z) Resolve(
        Model.Scene s, Room? room, Vec3 size, float rotationY,
        float? desiredX, float? desiredZ, string? anchor, Item? ignore, float margin = DefaultMargin)
    {
        float dx, dz;
        Room? target;
        if (!string.IsNullOrWhiteSpace(anchor) && room is not null)
        {
            // Intent relative to a room → keep it in that room.
            (dx, dz) = FromAnchor(s, room, size, rotationY, anchor!, CollisionItems(s, room, ignore), margin);
            target = room;
        }
        else if (desiredX is not null && desiredZ is not null)
        {
            // Explicit coordinate → only constrain it to the room it actually lands in (else it's outdoors).
            (dx, dz) = (desiredX.Value, desiredZ.Value);
            target = RoomContaining(s, dx, dz, room);
        }
        else
        {
            (dx, dz) = Suggest(s, room, size, ignore);
            target = room;
        }

        if (target is null) return (dx, dz); // outdoors / on site: nothing to clamp to

        var others = CollisionItems(s, target, ignore);
        var (cx, cz) = Clamp(target, size, rotationY, dx, dz, margin);

        // An item larger than the room can never "fit" — leave it centered rather than spiral forever.
        var fp = Geometry.FootprintOf(new Vec3(cx, 0, cz), size, rotationY);
        if (fp.Width > target.Width - 2 * margin + 1e-3f || fp.Depth > target.Depth - 2 * margin + 1e-3f)
            return (cx, cz);

        return Nudge(target, size, rotationY, cx, cz, others, margin);
    }

    private static Room? RoomContaining(Model.Scene s, float x, float z, Room? prefer)
    {
        if (prefer is not null && Contains(prefer, x, z)) return prefer;
        Room? found = null;
        foreach (var r in s.Rooms) if (Contains(r, x, z)) found = r;
        return found;

        static bool Contains(Room r, float x, float z)
            => x >= r.Center.X - r.Width / 2f && x <= r.Center.X + r.Width / 2f
            && z >= r.Center.Z - r.Depth / 2f && z <= r.Center.Z + r.Depth / 2f;
    }

    // ── Anchors ───────────────────────────────────────────────────────────────

    private static (float x, float z) FromAnchor(
        Model.Scene s, Room room, Vec3 size, float rot, string anchor, List<Item> others, float margin)
    {
        var raw = anchor.Trim().ToLowerInvariant().Replace('-', ' ');
        var split = raw.Split(':', 2);
        var key = split[0].Trim();
        var val = split.Length > 1 ? split[1].Trim() : "";

        var fp = Geometry.FootprintOf(Vec3.Zero, size, rot);
        float halfW = fp.Width / 2f, halfD = fp.Depth / 2f;
        float west = room.Center.X - room.Width / 2f + margin + halfW;
        float east = room.Center.X + room.Width / 2f - margin - halfW;
        float south = room.Center.Z - room.Depth / 2f + margin + halfD; // -Z
        float north = room.Center.Z + room.Depth / 2f - margin - halfD; // +Z

        return key switch
        {
            "center" or "centre" or "middle" => (room.Center.X, room.Center.Z),
            "wall" => Wall(val),
            "corner" => Corner(val),
            "near" or "beside" or "next" or "next to" or "by" => Beside(s, room, size, rot, val, null, margin),
            "left" => Beside(s, room, size, rot, val, "left", margin),
            "right" => Beside(s, room, size, rot, val, "right", margin),
            "front" or "in front of" => Beside(s, room, size, rot, val, "front", margin),
            "behind" or "back" => Beside(s, room, size, rot, val, "behind", margin),
            _ => (room.Center.X, room.Center.Z),
        };

        (float, float) Wall(string w) => w switch
        {
            "north" => (room.Center.X, north),
            "south" => (room.Center.X, south),
            "east" => (east, room.Center.Z),
            "west" => (west, room.Center.Z),
            _ => (room.Center.X, room.Center.Z),
        };

        (float, float) Corner(string c)
        {
            (float x, float z) ne = (east, north), nw = (west, north), se = (east, south), sw = (west, south);
            switch (c.Replace(" ", ""))
            {
                case "ne" or "northeast" or "topright": return ne;
                case "nw" or "northwest" or "topleft": return nw;
                case "se" or "southeast" or "bottomright": return se;
                case "sw" or "southwest" or "bottomleft": return sw;
            }
            // Unspecified corner → the one with the most clearance from existing items.
            var best = ne; var bestScore = float.MinValue;
            foreach (var cand in new[] { ne, nw, se, sw })
            {
                var cfp = Geometry.FootprintOf(new Vec3(cand.x, 0, cand.z), size, rot);
                var score = others.Count == 0 ? 0f
                    : others.Min(o => Geometry.Gap(cfp, Geometry.FootprintOf(o)));
                if (score > bestScore) { bestScore = score; best = cand; }
            }
            return best;
        }
    }

    private static (float x, float z) Beside(
        Model.Scene s, Room room, Vec3 size, float rot, string itemName, string? side, float margin)
    {
        var target = FindItem(s, itemName);
        if (target is null) return (room.Center.X, room.Center.Z);

        var tf = Geometry.FootprintOf(target);
        var fp = Geometry.FootprintOf(Vec3.Zero, size, rot);
        float halfW = fp.Width / 2f, halfD = fp.Depth / 2f;
        const float gap = 0.12f;

        (float x, float z) Right() => (tf.MaxX + gap + halfW, target.Position.Z);
        (float x, float z) Left() => (tf.MinX - gap - halfW, target.Position.Z);
        (float x, float z) Behind() => (target.Position.X, tf.MaxZ + gap + halfD); // +Z
        (float x, float z) Front() => (target.Position.X, tf.MinZ - gap - halfD);  // -Z

        if (side is not null)
            return side switch { "left" => Left(), "right" => Right(), "front" => Front(), _ => Behind() };

        // No side given: pick the first side that lands fully inside the room.
        foreach (var cand in new[] { Right(), Left(), Behind(), Front() })
        {
            var (qx, qz) = Clamp(room, size, rot, cand.x, cand.z, margin);
            if (MathF.Abs(qx - cand.x) < 1e-3f && MathF.Abs(qz - cand.z) < 1e-3f) return cand;
        }
        return Right();
    }

    // ── Clamping & collision ────────────────────────────────────────────────────

    private static (float x, float z) Clamp(Room room, Vec3 size, float rot, float x, float z, float margin)
    {
        var fp = Geometry.FootprintOf(new Vec3(x, 0, z), size, rot);
        float halfW = fp.Width / 2f, halfD = fp.Depth / 2f;
        float minX = room.Center.X - room.Width / 2f + margin + halfW;
        float maxX = room.Center.X + room.Width / 2f - margin - halfW;
        float minZ = room.Center.Z - room.Depth / 2f + margin + halfD;
        float maxZ = room.Center.Z + room.Depth / 2f - margin - halfD;
        float cx = minX <= maxX ? Math.Clamp(x, minX, maxX) : room.Center.X; // oversized → center the axis
        float cz = minZ <= maxZ ? Math.Clamp(z, minZ, maxZ) : room.Center.Z;
        return (cx, cz);
    }

    private static (float x, float z) Nudge(
        Room room, Vec3 size, float rot, float x, float z, List<Item> others, float margin)
    {
        if (Fits(room, size, rot, x, z, others, margin)) return (x, z);

        var maxR = MathF.Max(room.Width, room.Depth);
        for (var r = Step; r <= maxR; r += Step)
        {
            var n = Math.Max(8, (int)(MathF.Tau * r / Step));
            for (var i = 0; i < n; i++)
            {
                var a = MathF.Tau * i / n;
                var (qx, qz) = Clamp(room, size, rot, x + r * MathF.Cos(a), z + r * MathF.Sin(a), margin);
                if (Fits(room, size, rot, qx, qz, others, margin)) return (qx, qz);
            }
        }
        return (x, z); // no clear spot found — best effort (room is full)
    }

    private static bool Fits(Room room, Vec3 size, float rot, float x, float z, List<Item> others, float margin)
    {
        var inner = Geometry.Inset(Geometry.FootprintOf(room), margin);
        var fp = Geometry.FootprintOf(new Vec3(x, 0, z), size, rot);
        if (fp.MinX < inner.MinX - 1e-3f || fp.MaxX > inner.MaxX + 1e-3f) return false;
        if (fp.MinZ < inner.MinZ - 1e-3f || fp.MaxZ > inner.MaxZ + 1e-3f) return false;
        foreach (var o in others)
            if (Geometry.Gap(fp, Geometry.FootprintOf(o)) < Clearance) return false;
        return true;
    }

    /// <summary>
    /// Items that can block placement: in the same room and resting on this floor (not stacked on a
    /// surface, e.g. a laptop on a desk), excluding the one being moved.
    /// </summary>
    private static List<Item> CollisionItems(Model.Scene s, Room? room, Item? ignore)
    {
        var floorY = room?.Elevation ?? 0f;
        return s.Items.Where(i =>
                !ReferenceEquals(i, ignore)
                && (room is null ? i.RoomId is null : i.RoomId == room.Id)
                && MathF.Abs((i.Position.Y - i.Size.Y / 2f) - floorY) < 0.15f)
            .ToList();
    }

    private static (float x, float z) Suggest(Model.Scene s, Room? room, Vec3 size, Item? ignore)
    {
        if (room is null) return (0f, 0f);
        var items = s.Items.Where(i => !ReferenceEquals(i, ignore) && (i.RoomId == room.Id || i.RoomId is null));
        var result = UnusedAreaAnalyzer.Analyze(room, items);
        var region = result.Regions.FirstOrDefault(r => r.Width >= size.X && r.Depth >= size.Z);
        return region is not null ? (region.Center.X, region.Center.Z) : (room.Center.X, room.Center.Z);
    }

    private static Item? FindItem(Model.Scene s, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (string.Equals(name, "last", StringComparison.OrdinalIgnoreCase))
            return s.Items.Count > 0 ? s.Items[^1] : null;
        return s.Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? s.Items.FirstOrDefault(i => i.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }
}
