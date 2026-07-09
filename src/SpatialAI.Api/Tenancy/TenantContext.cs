using SpatialAI.Api.Spaces;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;

namespace SpatialAI.Api.Tenancy;

/// <summary>
/// One user's fully-isolated world: its own <see cref="SceneStore"/>, <see cref="SceneTools"/>,
/// saved-spaces manager (persisting under <c>spaces/{userId}/</c>), and SSE <see cref="SceneHub"/>.
/// Built once per user and cached in the <see cref="TenantRegistry"/> — this is what makes every
/// attendee's scene completely separate from everyone else's.
/// </summary>
public sealed class TenantContext
{
    public string UserId { get; }
    public SceneStore Store { get; }
    public SceneTools Tools { get; }
    public SpaceManager Spaces { get; }
    public SceneHub Hub { get; }
    public DateTime LastSeenUtc { get; set; }

    public TenantContext(string userId, string spacesRoot)
    {
        UserId = userId;
        Store = new SceneStore();
        Tools = new SceneTools(Store);
        var repo = new SpaceRepository(Path.Combine(spacesRoot, Sanitize(userId)));
        Spaces = new SpaceManager(Store, repo);
        Hub = new SceneHub(Store);
        LastSeenUtc = DateTime.UtcNow;
    }

    // Keep the on-disk folder name safe regardless of the id source (anon GUID or, later, a user id).
    private static string Sanitize(string userId)
    {
        var safe = new string(userId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return string.IsNullOrEmpty(safe) ? "anon" : safe;
    }
}
