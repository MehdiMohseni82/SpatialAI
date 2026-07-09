using System.Text;
using SpatialAI.Core.Model;

namespace SpatialAI.Core.Furniture;

/// <summary>
/// Metadata for one catalog kind: its canonical name, category, default bounding size and color, the
/// synonyms that route to it, and a short note shown to the LLM. The 3D <i>geometry</i> for the kind is a
/// parametric builder in <see cref="FurnitureFactory"/> (keyed by <see cref="Kind"/>); this record holds
/// only the data — which is what the catalog database stores.
/// </summary>
public sealed record CatalogEntry(
    string Kind,
    string Category,
    float W, float H, float D,
    Rgba Color,
    IReadOnlyList<string> Aliases,
    string Description = "");

/// <summary>
/// The set of known item kinds and their metadata — the single source of truth for what the model can
/// create and the text it sees. Built from <see cref="CatalogSeed"/> by default, or loaded from the
/// catalog database at runtime (see SpatialAI.Api's CatalogRepository).
/// </summary>
public sealed class Catalog
{
    private readonly Dictionary<string, CatalogEntry> _kinds;  // canonical kind -> entry
    private readonly Dictionary<string, string> _aliases;      // alias -> canonical kind
    private readonly List<string> _categoryOrder;              // first-seen category order

    public Catalog(IEnumerable<CatalogEntry> entries)
    {
        _kinds = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _categoryOrder = [];
        foreach (var e in entries)
        {
            _kinds[e.Kind] = e;
            if (!_categoryOrder.Contains(e.Category)) _categoryOrder.Add(e.Category);
            foreach (var a in e.Aliases) _aliases[Key(a)] = e.Kind;
        }
    }

    private static string Key(string s) =>
        (s ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    /// <summary>Maps any spelling/synonym to its canonical kind (unknown input is returned normalized).</summary>
    public string Normalize(string kind)
    {
        var k = Key(kind);
        if (_kinds.ContainsKey(k)) return k;
        return _aliases.TryGetValue(k, out var canon) ? canon : k;
    }

    public bool IsKnown(string kind) => _kinds.ContainsKey(Normalize(kind));

    public bool TryGet(string kind, out CatalogEntry entry) => _kinds.TryGetValue(Normalize(kind), out entry!);

    public IReadOnlyCollection<CatalogEntry> AllKinds => _kinds.Values;

    private IEnumerable<IGrouping<string, CatalogEntry>> ByCategory() =>
        _kinds.Values.GroupBy(e => e.Category)
                     .OrderBy(g => _categoryOrder.IndexOf(g.Key));

    /// <summary>Multi-line, category-grouped kind list for the system prompt.</summary>
    public string DescribeKinds()
    {
        var sb = new StringBuilder();
        foreach (var grp in ByCategory())
            sb.Append("  ").Append(grp.Key).Append(": ")
              .Append(string.Join(", ", grp.Select(Label))).AppendLine(".");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Single-line, category-grouped kind list for a tool's schema description.</summary>
    public string DescribeKindsInline()
    {
        var groups = ByCategory().Select(g => $"{g.Key}: {string.Join(", ", g.Select(e => e.Kind))}");
        return "Pick the most specific kind (variants are distinct models). "
               + string.Join(". ", groups)
               + ". Or box/cylinder/sphere for a plain block.";
    }

    private static string Label(CatalogEntry e) =>
        e.Description.Length > 0 ? $"{e.Kind} ({e.Description})" : e.Kind;
}
