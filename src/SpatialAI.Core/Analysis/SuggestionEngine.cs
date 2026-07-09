using SceneModel = SpatialAI.Core.Model.Scene;   // 'Scene' alone clashes with the SpatialAI.Core.Scene namespace

namespace SpatialAI.Core.Analysis;

/// <summary>
/// Deterministic, server-side "what next?" engine. Reads the current <see cref="Scene"/> and returns a
/// short list of curated next-step prompts tailored to what already exists — the always-on floor behind
/// the chat's follow-up suggestion chips (an optional LLM pass may refine these when budget allows).
/// Pure and dependency-light so it's instant, free, and unit-testable.
/// </summary>
public static class SuggestionEngine
{
    private static readonly string[] OutdoorWords =
        { "yard", "garden", "outdoor", "patio", "lawn", "court", "field", "park" };

    /// <summary>Returns up to <paramref name="max"/> distinct next-step prompts for the current scene.</summary>
    public static List<string> Suggest(SceneModel scene, int max = 3)
    {
        // Empty scene → openers.
        if (scene.Rooms.Count == 0 && scene.Items.Count == 0)
            return Finish(["Create a 6x5 office", "Build a 4x4 bedroom", "Make a 12x10 yard"], max);

        var outdoor = scene.Rooms.Where(r => ContainsAny(r.Name, OutdoorWords)).ToList();
        var indoor = scene.Rooms.Where(r => !ContainsAny(r.Name, OutdoorWords)).ToList();
        var hasItems = scene.Items.Count > 0;
        var hasFence = HasTerm(scene, "fence");

        var s = new List<string>();

        // Outdoor room with no fence → the enclose_room capstone tie-in.
        if (outdoor.Count > 0 && !hasFence)
            s.Add($"Put a fence around the {outdoor[^1].Name.ToLowerInvariant()}");

        // Rooms exist but nothing in them → furnish (indoors vs outdoors differ).
        if (!hasItems)
        {
            if (indoor.Count > 0) { s.Add("Add a desk and a chair"); s.Add("Put a sofa in the corner"); }
            else if (outdoor.Count > 0) { s.Add("Add a tree"); s.Add("Add a bench"); }
        }

        // Newest indoor room has no windows/doors → open it up.
        var newestIndoor = indoor.Count > 0 ? indoor[^1] : null;
        if (newestIndoor is { Openings.Count: 0 })
            s.Add("Add a window on the north wall");

        // Desks but nothing to sit on → pair them up.
        if (HasKind(scene, "desk") && !HasKind(scene, "chair", "stool", "seat"))
            s.Add("Add a chair at each desk");

        // More than one indoor room → connect them.
        if (indoor.Count >= 2)
            s.Add("Add a door between the rooms");

        // Generic fallbacks (only used if the above didn't fill the quota).
        s.Add("Where can I put a couch?");
        s.Add("Add a plant in the corner");

        return Finish(s, max);
    }

    private static bool HasKind(SceneModel scene, params string[] kinds) =>
        scene.Items.Any(i => i.Kind is not null &&
            kinds.Any(k => string.Equals(i.Kind, k, StringComparison.OrdinalIgnoreCase)));

    private static bool HasTerm(SceneModel scene, string term) =>
        scene.Items.Any(i =>
            (i.Kind?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            i.Name.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string text, string[] words) =>
        words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static List<string> Finish(IEnumerable<string> items, int max) =>
        items.Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList();
}
