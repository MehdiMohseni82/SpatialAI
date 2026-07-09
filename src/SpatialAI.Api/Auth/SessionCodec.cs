using Microsoft.AspNetCore.DataProtection;

namespace SpatialAI.Api.Auth;

/// <summary>
/// Signs/verifies the session cookie payload (a user id + expiry) with ASP.NET Data Protection so it
/// can't be forged. Stateless — no server-side session table needed.
/// </summary>
public sealed class SessionCodec
{
    private readonly IDataProtector _protector;

    public SessionCodec(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("SpatialAI.Session.v1");

    public string Issue(string userId, TimeSpan lifetime)
        => _protector.Protect($"{userId}|{DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds()}");

    public bool TryValidate(string token, out string userId)
    {
        userId = "";
        try
        {
            var raw = _protector.Unprotect(token);
            var parts = raw.Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[1], out var exp)) return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return false;
            userId = parts[0];
            return userId.Length > 0;
        }
        catch
        {
            return false;   // tampered / wrong key / not a session token
        }
    }
}
