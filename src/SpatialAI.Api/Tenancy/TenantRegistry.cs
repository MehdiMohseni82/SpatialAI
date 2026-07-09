using System.Collections.Concurrent;

namespace SpatialAI.Api.Tenancy;

/// <summary>
/// Holds one <see cref="TenantContext"/> per user id, created on first use. Every request resolves the
/// caller's context from here (keyed by the <c>uid</c> cookie), so handlers only ever touch that one
/// user's scene — the basis of complete per-user isolation.
/// </summary>
public sealed class TenantRegistry
{
    private readonly ConcurrentDictionary<string, TenantContext> _tenants = new();
    private readonly string _spacesRoot;

    public TenantRegistry(string spacesRoot) => _spacesRoot = spacesRoot;

    public TenantContext For(string userId)
    {
        var ctx = _tenants.GetOrAdd(userId, id => new TenantContext(id, _spacesRoot));
        ctx.LastSeenUtc = DateTime.UtcNow;
        return ctx;
    }

    public int Count => _tenants.Count;
}
