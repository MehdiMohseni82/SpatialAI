using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SpatialAI.Api.Auth;

public sealed record AppUser(string Id, string Email, string Name, DateTime? VerifiedAtUtc);

/// <summary>
/// SQLite-backed users + one-time magic-link tokens. Registration captures the email (the lead-gen
/// goal); a time-limited magic link verifies it and creates/confirms the account. The user id (a GUID)
/// becomes the tenant + budget key once signed in.
/// </summary>
public sealed class AuthRepository
{
    private readonly string _cs;

    public AuthRepository(string databasePath)
    {
        var full = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        _cs = new SqliteConnectionStringBuilder { DataSource = full }.ToString();
        EnsureSchema();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    private void EnsureSchema()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY, email TEXT NOT NULL UNIQUE, name TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL, verified_at TEXT, used_messages INTEGER NOT NULL DEFAULT 0,
                api_token TEXT);
            CREATE TABLE IF NOT EXISTS magic_links (
                token TEXT PRIMARY KEY, email TEXT NOT NULL, name TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL, expires_at TEXT NOT NULL, used_at TEXT);
            """;
        cmd.ExecuteNonQuery();
        // Migrate DBs created before these columns existed (ADD COLUMN throws if already present).
        AddColumnIfMissing(c, "ALTER TABLE users ADD COLUMN used_messages INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "ALTER TABLE users ADD COLUMN api_token TEXT");
    }

    private static void AddColumnIfMissing(SqliteConnection c, string alterSql)
    {
        try
        {
            using var alter = c.CreateCommand();
            alter.CommandText = alterSql;
            alter.ExecuteNonQuery();
        }
        catch (SqliteException) { /* column already present */ }
    }

    // ── Per-email message budget (persistent, so it survives restarts AND re-logins) ────────────
    /// <summary>Messages this account may still send (perUser − used); a missing row means "unused".</summary>
    public int RemainingMessages(string userId, int perUser)
    {
        using var c = Open();
        using var sel = c.CreateCommand();
        sel.CommandText = "SELECT used_messages FROM users WHERE id=$id";
        sel.Parameters.AddWithValue("$id", userId);
        var used = sel.ExecuteScalar();
        var u = used is long l ? (int)l : 0;
        return Math.Max(0, perUser - u);
    }

    /// <summary>
    /// Atomically charges <paramref name="cost"/> messages to the account if it stays within
    /// <paramref name="perUser"/> — a single guarded UPDATE, so re-login/restart can't reset it.
    /// Returns false if the account is over cap (or the row doesn't exist).
    /// </summary>
    public bool TryConsumeMessages(string userId, int cost, int perUser, out int remaining)
    {
        if (cost < 1) cost = 1;
        using var c = Open();
        // Open/dev mode issues an anonymous per-browser uid that has no account row yet; without one the
        // guarded UPDATE below matches zero rows and every message reads as "over budget". Create a
        // zero-usage row on first use so anonymous users get their normal per-user allowance. Registered
        // accounts already have a row, so INSERT OR IGNORE is a no-op for them.
        using var ins = c.CreateCommand();
        ins.CommandText = "INSERT OR IGNORE INTO users (id, email, name, created_at, used_messages) " +
                          "VALUES ($id, $email, 'Guest', $now, 0)";
        ins.Parameters.AddWithValue("$id", userId);
        ins.Parameters.AddWithValue("$email", "anon:" + userId);
        ins.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        ins.ExecuteNonQuery();

        using var upd = c.CreateCommand();
        upd.CommandText = "UPDATE users SET used_messages = used_messages + $cost " +
                          "WHERE id=$id AND used_messages + $cost <= $cap";
        upd.Parameters.AddWithValue("$cost", cost);
        upd.Parameters.AddWithValue("$id", userId);
        upd.Parameters.AddWithValue("$cap", perUser);
        var ok = upd.ExecuteNonQuery() == 1;
        remaining = RemainingMessages(userId, perUser);
        return ok;
    }

    /// <summary>Sum of all accounts' used messages — seeds the in-memory global counter after a restart.</summary>
    public int TotalMessagesUsed()
    {
        using var c = Open();
        using var sel = c.CreateCommand();
        sel.CommandText = "SELECT COALESCE(SUM(used_messages),0) FROM users";
        return sel.ExecuteScalar() is long l ? (int)l : 0;
    }

    // ── Personal API token (a headless twin of the sid cookie, for MCP clients) ─────────────────
    /// <summary>The account's API token, minting one on first use. Lets an MCP client authenticate as
    /// this user against the remote API (Authorization: Bearer) and edit their own scene. Idempotent.</summary>
    public string GetOrCreateApiToken(string userId)
    {
        using var c = Open();
        using (var sel = c.CreateCommand())
        {
            sel.CommandText = "SELECT api_token FROM users WHERE id=$id";
            sel.Parameters.AddWithValue("$id", userId);
            if (sel.ExecuteScalar() is string existing && existing.Length > 0) return existing;
        }
        var token = "mcp_" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        using var upd = c.CreateCommand();
        upd.CommandText = "UPDATE users SET api_token=$t WHERE id=$id";
        upd.Parameters.AddWithValue("$t", token);
        upd.Parameters.AddWithValue("$id", userId);
        upd.ExecuteNonQuery();
        return token;
    }

    /// <summary>Resolves an API token to its owner, or null if the token is blank/unknown.</summary>
    public AppUser? FindByApiToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        using var c = Open();
        using var sel = c.CreateCommand();
        sel.CommandText = "SELECT id,email,name,verified_at FROM users WHERE api_token=$t";
        sel.Parameters.AddWithValue("$t", token);
        using var r = sel.ExecuteReader();
        if (!r.Read()) return null;
        var verified = r.IsDBNull(3) ? (DateTime?)null : Parse(r.GetString(3));
        return new AppUser(r.GetString(0), r.GetString(1), r.GetString(2), verified);
    }

    /// <summary>Records a registration intent and returns a fresh one-time magic-link token.</summary>
    public string CreateMagicLink(string email, string name, TimeSpan ttl)
    {
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO magic_links(token,email,name,created_at,expires_at) VALUES($t,$e,$n,$c,$x)";
        cmd.Parameters.AddWithValue("$t", token);
        cmd.Parameters.AddWithValue("$e", email.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$c", Iso(DateTime.UtcNow));
        cmd.Parameters.AddWithValue("$x", Iso(DateTime.UtcNow + ttl));
        cmd.ExecuteNonQuery();
        return token;
    }

    /// <summary>
    /// Validates a magic link and returns (email, name) if it's still within its TTL, else null.
    /// The link is <b>reusable until it expires</b> — deliberately NOT one-time: email security
    /// scanners (Microsoft SafeLinks, etc.) pre-fetch every link to scan it, which would burn a
    /// one-time token seconds after send and lock the real user out. We still record the first use
    /// for auditing, but reuse within the TTL is allowed.
    /// </summary>
    public (string email, string name)? ConsumeMagicLink(string token)
    {
        using var c = Open();
        string email, name;
        DateTime expires;
        using (var sel = c.CreateCommand())
        {
            sel.CommandText = "SELECT email,name,expires_at FROM magic_links WHERE token=$t";
            sel.Parameters.AddWithValue("$t", token);
            using var r = sel.ExecuteReader();
            if (!r.Read()) return null;
            email = r.GetString(0);
            name = r.GetString(1);
            expires = Parse(r.GetString(2));
        }
        if (expires < DateTime.UtcNow) return null;   // only the TTL gates validity now

        using var upd = c.CreateCommand();
        upd.CommandText = "UPDATE magic_links SET used_at=COALESCE(used_at,$u) WHERE token=$t"; // record first use only
        upd.Parameters.AddWithValue("$u", Iso(DateTime.UtcNow));
        upd.Parameters.AddWithValue("$t", token);
        upd.ExecuteNonQuery();
        return (email, name);
    }

    /// <summary>
    /// Gets or creates a verified user for the email (after a link is consumed, or when verification is
    /// bypassed via Auth:RequireVerification=false). Idempotent by email.
    /// </summary>
    public AppUser GetOrCreateVerifiedUser(string email, string name)
    {
        email = email.Trim().ToLowerInvariant();
        using var c = Open();
        string? existingId = null;
        using (var sel = c.CreateCommand())
        {
            sel.CommandText = "SELECT id FROM users WHERE email=$e";
            sel.Parameters.AddWithValue("$e", email);
            using var r = sel.ExecuteReader();
            if (r.Read()) existingId = r.GetString(0);
        }

        if (existingId is not null)
        {
            using var upd = c.CreateCommand();
            upd.CommandText = "UPDATE users SET verified_at=COALESCE(verified_at,$now), name=$n WHERE id=$id";
            upd.Parameters.AddWithValue("$now", Iso(DateTime.UtcNow));
            upd.Parameters.AddWithValue("$n", name);
            upd.Parameters.AddWithValue("$id", existingId);
            upd.ExecuteNonQuery();
            return new AppUser(existingId, email, name, DateTime.UtcNow);
        }

        var newId = Guid.NewGuid().ToString("N");
        using var ins = c.CreateCommand();
        ins.CommandText = "INSERT INTO users(id,email,name,created_at,verified_at) VALUES($id,$e,$n,$c,$c)";
        ins.Parameters.AddWithValue("$id", newId);
        ins.Parameters.AddWithValue("$e", email);
        ins.Parameters.AddWithValue("$n", name);
        ins.Parameters.AddWithValue("$c", Iso(DateTime.UtcNow));
        ins.ExecuteNonQuery();
        return new AppUser(newId, email, name, DateTime.UtcNow);
    }

    public AppUser? GetUser(string id)
    {
        using var c = Open();
        using var sel = c.CreateCommand();
        sel.CommandText = "SELECT id,email,name,verified_at FROM users WHERE id=$id";
        sel.Parameters.AddWithValue("$id", id);
        using var r = sel.ExecuteReader();
        if (!r.Read()) return null;
        var verified = r.IsDBNull(3) ? (DateTime?)null : Parse(r.GetString(3));
        return new AppUser(r.GetString(0), r.GetString(1), r.GetString(2), verified);
    }

    public int UserCount()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string Iso(DateTime dt) => dt.ToUniversalTime().ToString("O");
    private static DateTime Parse(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
