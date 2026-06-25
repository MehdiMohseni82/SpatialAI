using SpatialAI.Core.Tools;

namespace SpatialAI.Api.Blueprint;

/// <summary>
/// Turns a <see cref="BuildingPlan"/> into a live 3D scene by calling the existing <see cref="SceneTools"/>
/// (the same operations the LLM and MCP use). Deterministic — no model in the loop — so geometry is precise
/// and every call animates into the viewer over SSE. Rooms are named with a per-storey tag so duplicate room
/// names across floors (Bad, Schlafen…) stay addressable for their openings and furniture.
/// </summary>
public sealed class BuildingReconstructor(SceneTools tools)
{
    /// <summary>Builds the whole plan into the current scene. Caller should clear/reset the scene first.</summary>
    public int Reconstruct(BuildingPlan plan)
    {
        var built = 0;
        foreach (var floor in (plan.Floors ?? []).OrderBy(f => f.Level))
        {
            var tag = LevelTag(floor.Level);
            var rooms = floor.Rooms ?? [];
            foreach (var room in rooms)
            {
                var name = Unique(tag, room.Name);
                tools.CreateRoom(name, room.Width, room.Depth, room.CenterX, room.CenterZ,
                    floor.Height, windows: 0, doors: 0, ceiling: false, roof: "none",
                    elevation: floor.Elevation, level: floor.Level, inRoof: floor.InRoof);
                built++;

                foreach (var o in room.Openings ?? [])
                {
                    if (string.Equals(o.Type, "door", StringComparison.OrdinalIgnoreCase))
                        tools.AddDoor(name, o.Wall, o.Offset, NonZero(o.Width, 0.9f), NonZero(o.Height, 2.1f));
                    else
                        tools.AddWindow(name, o.Wall, o.Offset, NonZero(o.Width, 1.2f), NonZero(o.Height, 1.2f), null);
                }

                foreach (var fn in room.Furniture ?? [])
                {
                    // Keep furniture inside its room (extraction noise can place it just outside → it would
                    // otherwise fall to the wrong storey / ground).
                    var (fx, fz) = ClampInto(room, fn.X, fn.Z);
                    tools.CreateItem(ItemName(fn.Kind), fn.Kind, fn.Width, null, fn.Depth,
                        null, null, null, fx, fz, name, fn.RotationY);
                }
            }

            // Stairs once per floor (not per room) — anchor each to the room it sits in for correct elevation.
            foreach (var st in DedupeStairs(floor.Stairs))
            {
                var host = rooms.FirstOrDefault(r => Contains(r, st.X, st.Z)) ?? rooms.FirstOrDefault();
                if (host is null) continue;
                tools.CreateItem($"{tag} Stairs", "staircase", null, null, null,
                    null, null, null, st.X, st.Z, Unique(tag, host.Name), st.RotationY);
            }

            // Ground-floor MAIN ENTRANCE: a clear front door + an exterior stoop (read from the plan).
            if (floor.Level == 0 && rooms.Count > 0 && FindEntranceRoom(rooms, floor.EntranceRoom) is { } eRoom)
            {
                var outer = OuterWall(eRoom, rooms);
                var wall = NormWall(floor.EntranceWall) ?? outer;
                bool DoorOn(string w) => (eRoom.Openings ?? []).Any(o =>
                    string.Equals(o.Type, "door", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(NormWall(o.Wall) ?? "", w, StringComparison.OrdinalIgnoreCase));
                // Reuse the door the plan already drew (don't make a second one); else add one on the front wall.
                if (DoorOn(wall)) { /* the extracted door is the entrance */ }
                else if (DoorOn(outer)) wall = outer;
                else tools.AddDoor(Unique(tag, eRoom.Name), wall, 0f, 1.1f, 2.2f);

                var (sx, sz) = OutsideWallPoint(eRoom, wall, 0.7f);
                tools.CreateItem("Entrance Steps", "steps", null, null, null,
                    null, null, null, sx, sz, null, null, null, faceItem: Unique(tag, eRoom.Name)); // steps rise toward the door
            }
        }

        // Site items: no room → placed on the ground around the building.
        foreach (var s in plan.Site ?? [])
            tools.CreateItem(ItemName(s.Kind), s.Kind, null, null, null,
                null, null, null, s.X, s.Z, null, s.RotationY);

        if (plan.Roof is { } roof && !string.IsNullOrWhiteSpace(roof.Style))
            tools.SetBuildingRoof(roof.Style, roof.Height, roof.Dormers,
                baseY: roof.Eave > 0 ? roof.Eave : null,
                ridgeY: roof.Ridge > 0 ? roof.Ridge : null,
                breakY: roof.Break > 0 ? roof.Break : null);

        return built;
    }

    private static float? NonZero(float v, float dflt) => v > 0.05f ? v : dflt;

    // ── Entrance ──────────────────────────────────────────────────────────────
    private static readonly string[] EntranceKeywords =
        { "windfang", "eingang", "diele", "vorzimmer", "vorraum", "flur" };

    private static PlanRoom? FindEntranceRoom(List<PlanRoom> rooms, string? named)
    {
        if (!string.IsNullOrWhiteSpace(named) &&
            rooms.FirstOrDefault(r => (r.Name ?? "").Contains(named!, StringComparison.OrdinalIgnoreCase)) is { } m)
            return m;
        foreach (var kw in EntranceKeywords)
            if (rooms.FirstOrDefault(r => (r.Name ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase)) is { } k)
                return k;
        return null; // no obvious entrance hall → don't force a door in the wrong place
    }

    private static string? NormWall(string? w) => (w?.Trim().ToLowerInvariant()) switch
    {
        "north" => "north", "south" => "south", "east" => "east", "west" => "west", _ => null
    };

    /// <summary>The host room's wall nearest the building's outer edge (prefers the front/south on ties).</summary>
    private static string OuterWall(PlanRoom host, List<PlanRoom> rooms)
    {
        float minX = rooms.Min(r => r.CenterX - r.Width / 2), maxX = rooms.Max(r => r.CenterX + r.Width / 2);
        float minZ = rooms.Min(r => r.CenterZ - r.Depth / 2), maxZ = rooms.Max(r => r.CenterZ + r.Depth / 2);
        float dS = MathF.Abs((host.CenterZ - host.Depth / 2) - minZ);
        float dN = MathF.Abs((host.CenterZ + host.Depth / 2) - maxZ);
        float dE = MathF.Abs((host.CenterX + host.Width / 2) - maxX);
        float dW = MathF.Abs((host.CenterX - host.Width / 2) - minX);
        float m = MathF.Min(MathF.Min(dS, dN), MathF.Min(dE, dW));
        return m == dS ? "south" : m == dN ? "north" : m == dE ? "east" : "west";
    }

    private static (float x, float z) OutsideWallPoint(PlanRoom host, string wall, float gap) => wall switch
    {
        "north" => (host.CenterX, host.CenterZ + host.Depth / 2 + gap),
        "south" => (host.CenterX, host.CenterZ - host.Depth / 2 - gap),
        "east" => (host.CenterX + host.Width / 2 + gap, host.CenterZ),
        _ => (host.CenterX - host.Width / 2 - gap, host.CenterZ),
    };

    private static bool Contains(PlanRoom r, float x, float z)
        => x >= r.CenterX - r.Width / 2 && x <= r.CenterX + r.Width / 2
        && z >= r.CenterZ - r.Depth / 2 && z <= r.CenterZ + r.Depth / 2;

    /// <summary>Clamps (x,z) to inside the room, inset a little from the walls.</summary>
    private static (float x, float z) ClampInto(PlanRoom r, float x, float z)
    {
        var mx = MathF.Max(0.1f, r.Width / 2 - 0.3f);
        var mz = MathF.Max(0.1f, r.Depth / 2 - 0.3f);
        return (Math.Clamp(x, r.CenterX - mx, r.CenterX + mx), Math.Clamp(z, r.CenterZ - mz, r.CenterZ + mz));
    }

    /// <summary>Drops stairs that sit almost on top of each other (the model sometimes repeats them).</summary>
    private static List<PlanStair> DedupeStairs(List<PlanStair>? stairs)
    {
        var kept = new List<PlanStair>();
        foreach (var s in stairs ?? [])
            if (!kept.Any(k => MathF.Abs(k.X - s.X) < 0.8f && MathF.Abs(k.Z - s.Z) < 0.8f))
                kept.Add(s);
        return kept;
    }

    private static string Unique(string tag, string? roomName)
    {
        var n = string.IsNullOrWhiteSpace(roomName) ? "Room" : roomName!.Trim();
        return $"{tag}·{n}"; // e.g. "EG·Bad"
    }

    private static string ItemName(string? kind)
    {
        var k = string.IsNullOrWhiteSpace(kind) ? "Item" : kind!.Trim();
        return char.ToUpperInvariant(k[0]) + k[1..].Replace('_', ' ');
    }

    /// <summary>German storey tags so names are short + meaningful: KG/EG/OG/DG, else L{n}.</summary>
    private static string LevelTag(int level) => level switch
    {
        < -1 => $"UG{-level}",
        -1 => "KG",   // Kellergeschoss
        0 => "EG",    // Erdgeschoss
        1 => "OG",    // Obergeschoss
        2 => "DG",    // Dachgeschoss
        _ => $"L{level}",
    };
}
