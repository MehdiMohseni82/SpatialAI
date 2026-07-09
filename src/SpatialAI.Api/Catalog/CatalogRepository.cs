using Microsoft.Data.Sqlite;
using SpatialAI.Core.Furniture;
using SpatialAI.Core.Model;

namespace SpatialAI.Api.Catalog;

/// <summary>
/// SQLite-backed store for the item catalog — the single source of truth for kinds, their default
/// size/color, aliases and LLM descriptions. The geometry for each kind stays in code
/// (<see cref="FurnitureFactory"/>); this persists only the metadata. The DB file is created and seeded
/// from <see cref="CatalogSeed"/> on first run, then <see cref="Load"/> hydrates a <see cref="Catalog"/>.
/// </summary>
public sealed class CatalogRepository
{
    private readonly string _connectionString;

    public CatalogRepository(string databasePath)
    {
        var full = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = full }.ToString();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>Creates the schema if missing and seeds it from <see cref="CatalogSeed"/> when empty.</summary>
    public void EnsureSeeded()
    {
        using var conn = Open();
        Exec(conn, """
            CREATE TABLE IF NOT EXISTS kinds (
                kind TEXT PRIMARY KEY, category TEXT NOT NULL,
                w REAL NOT NULL, h REAL NOT NULL, d REAL NOT NULL,
                color_r REAL NOT NULL, color_g REAL NOT NULL, color_b REAL NOT NULL,
                description TEXT NOT NULL DEFAULT '', sort INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS aliases (
                alias TEXT PRIMARY KEY,
                kind TEXT NOT NULL REFERENCES kinds(kind) ON DELETE CASCADE);
            """);

        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM kinds";
            if (Convert.ToInt64(check.ExecuteScalar()) > 0) return; // already seeded
        }

        using var tx = conn.BeginTransaction();
        var sort = 0;
        foreach (var e in CatalogSeed.Entries)
        {
            Exec(conn, tx,
                "INSERT INTO kinds (kind, category, w, h, d, color_r, color_g, color_b, description, sort) " +
                "VALUES ($k,$c,$w,$h,$d,$r,$g,$b,$desc,$sort)",
                ("$k", e.Kind), ("$c", e.Category), ("$w", e.W), ("$h", e.H), ("$d", e.D),
                ("$r", e.Color.R), ("$g", e.Color.G), ("$b", e.Color.B), ("$desc", e.Description), ("$sort", sort++));
            foreach (var a in e.Aliases)
                Exec(conn, tx, "INSERT OR IGNORE INTO aliases (alias, kind) VALUES ($a,$k)", ("$a", a), ("$k", e.Kind));
        }
        tx.Commit();
    }

    /// <summary>Reads all rows into a catalog.</summary>
    public SpatialAI.Core.Furniture.Catalog Load()
    {
        using var conn = Open();
        var aliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using (var ac = conn.CreateCommand())
        {
            ac.CommandText = "SELECT alias, kind FROM aliases";
            using var ar = ac.ExecuteReader();
            while (ar.Read())
            {
                var kind = ar.GetString(1);
                (aliases.TryGetValue(kind, out var list) ? list : aliases[kind] = []).Add(ar.GetString(0));
            }
        }

        var entries = new List<CatalogEntry>();
        using (var kc = conn.CreateCommand())
        {
            kc.CommandText = "SELECT kind, category, w, h, d, color_r, color_g, color_b, description FROM kinds ORDER BY sort";
            using var kr = kc.ExecuteReader();
            while (kr.Read())
            {
                var kind = kr.GetString(0);
                entries.Add(new CatalogEntry(
                    kind, kr.GetString(1),
                    kr.GetFloat(2), kr.GetFloat(3), kr.GetFloat(4),
                    new Rgba(kr.GetFloat(5), kr.GetFloat(6), kr.GetFloat(7)),
                    aliases.GetValueOrDefault(kind, []),
                    kr.GetString(8)));
            }
        }
        return new SpatialAI.Core.Furniture.Catalog(entries);
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
