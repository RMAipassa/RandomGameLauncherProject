using System.Text.Json;
using System.IO;
using Microsoft.Data.Sqlite;
using System.Linq;
using RandomGameLauncher.Models;

namespace RandomGameLauncher.Services;

public static class AmazonScanner
{
    public static List<GameEntry> Scan()
    {
        var games = new List<GameEntry>();

        foreach (var dir in GetManifestDirs())
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                TryParseManifest(file, games);
            }
        }

        // Newer Amazon Games installs store data in sqlite instead of plain JSON manifests.
        // If manifest scan found nothing, fall back to sqlite discovery.
        if (games.Count == 0)
        {
            TryScanSqlite(games);
        }

        return games
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Platform)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? TryGetLaunchTarget(string amazonId)
    {
        if (string.IsNullOrWhiteSpace(amazonId)) return null;
        // Amazon Games URI scheme.
        return $"amazon-games://play/{amazonId}";
    }

    static IEnumerable<string> GetManifestDirs()
    {
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var p1 = Path.Combine(lad, "Amazon Games", "Data", "Manifests");
        yield return p1;

        var p2 = Path.Combine(lad, "Amazon Games", "GameLibrary", "Manifests");
        yield return p2;

        var pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var p3 = Path.Combine(pd, "Amazon", "Amazon Games", "Data", "Manifests");
        yield return p3;
    }

    static void TryScanSqlite(List<GameEntry> games)
    {
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var sqlDir = Path.Combine(lad, "Amazon Games", "Data", "Games", "Sql");
        if (!Directory.Exists(sqlDir)) return;

        var installs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var installedFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var db in Directory.EnumerateFiles(sqlDir, "*.sqlite", SearchOption.TopDirectoryOnly))
        {
            try
            {
                TryReadDb(db, installs, names, installedFlags);
            }
            catch
            {
                // ignore per-db
            }
        }

        foreach (var id in installs.Keys.Concat(installedFlags).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : id;
            installs.TryGetValue(id, out var path);

            games.Add(new GameEntry
            {
                Platform = "amazon",
                Id = id,
                Name = name,
                InstallPath = path ?? "",
                SupportsPlaytime = false
            });
        }
    }

    static void TryReadDb(string dbPath, Dictionary<string, string> installs, Dictionary<string, string> names, HashSet<string> installedFlags)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        using var con = new SqliteConnection(cs);
        con.Open();

        var tables = new List<string>();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                tables.Add(r.GetString(0));
        }

        foreach (var t in tables)
        {
            var cols = GetColumns(con, t);
            if (cols.Count == 0) continue;

            var idCol = Pick(cols, "productid", "product_id", "asin", "gameid", "id", "product");
            var nameCol = Pick(cols, "title", "name", "displayname", "producttitle");
            var installCol = Pick(cols, "installlocation", "installpath", "install_dir", "installpath", "path", "location");
            var installedCol = Pick(cols, "installed", "isinstalled", "bisinstalled", "is_installed", "installationstate");

            if (idCol is null) continue;

            // Install/path table.
            if (installCol is not null || installedCol is not null)
            {
                var sql = $"SELECT {Q(idCol)}" +
                          (installCol is not null ? $", {Q(installCol)}" : ", NULL") +
                          (installedCol is not null ? $", {Q(installedCol)}" : ", NULL") +
                          $" FROM {Q(t)}";

                using var cmd = con.CreateCommand();
                cmd.CommandText = sql;
                using var r = cmd.ExecuteReader();

                int seen = 0;
                while (r.Read() && seen < 1200)
                {
                    seen++;

                    var id = SafeGetString(r, 0);
                    var install = SafeGetString(r, 1);
                    var installed = SafeGetBoolish(r, 2);

                    if (!IsPlausibleAmazonId(id)) continue;

                    if (!string.IsNullOrWhiteSpace(install) && Directory.Exists(install))
                        installs[id!] = install!;
                    else if (installed == true)
                        installedFlags.Add(id!);
                }
            }

            // Name/title table.
            if (nameCol is not null)
            {
                var sql = $"SELECT {Q(idCol)}, {Q(nameCol)} FROM {Q(t)}";
                using var cmd = con.CreateCommand();
                cmd.CommandText = sql;
                using var r = cmd.ExecuteReader();

                int seen = 0;
                while (r.Read() && seen < 4000)
                {
                    seen++;
                    var id = SafeGetString(r, 0);
                    var name = SafeGetString(r, 1);
                    if (!IsPlausibleAmazonId(id)) continue;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    names[id!] = name!;
                }
            }
        }
    }

    static bool IsPlausibleAmazonId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.StartsWith("amzn1.", StringComparison.OrdinalIgnoreCase)) return true;
        // Some rows use UUID-like ids.
        if (Guid.TryParse(id, out _)) return true;
        // ASIN-like.
        if (id.Length == 10 && id.All(ch => char.IsLetterOrDigit(ch))) return true;
        return false;
    }

    static List<string> GetColumns(SqliteConnection con, string table)
    {
        var cols = new List<string>();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({Q(table)})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            // PRAGMA table_info: cid, name, type, notnull, dflt_value, pk
            if (!r.IsDBNull(1)) cols.Add(r.GetString(1));
        }
        return cols;
    }

    static string? Pick(List<string> cols, params string[] wants)
    {
        foreach (var w in wants)
        {
            var c = cols.FirstOrDefault(x => x.Equals(w, StringComparison.OrdinalIgnoreCase));
            if (c is not null) return c;
        }

        foreach (var w in wants)
        {
            var c = cols.FirstOrDefault(x => x.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (c is not null) return c;
        }

        return null;
    }

    static string Q(string ident) => '"' + ident.Replace("\"", "\"\"") + '"';

    static string? SafeGetString(SqliteDataReader r, int i)
    {
        try
        {
            if (r.IsDBNull(i)) return null;
            return r.GetValue(i)?.ToString();
        }
        catch { return null; }
    }

    static bool? SafeGetBoolish(SqliteDataReader r, int i)
    {
        try
        {
            if (r.IsDBNull(i)) return null;
            var v = r.GetValue(i);
            if (v is null) return null;

            if (v is long l) return l != 0;
            if (v is int ii) return ii != 0;
            if (v is bool b) return b;

            var s = v.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (bool.TryParse(s, out var bb)) return bb;
            if (long.TryParse(s, out var ll)) return ll != 0;
            if (s.Contains("installed", StringComparison.OrdinalIgnoreCase)) return true;
            return null;
        }
        catch { return null; }
    }

    static void TryParseManifest(string file, List<GameEntry> games)
    {
        try
        {
            var json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // The Amazon Games app has had multiple manifest shapes.
            // We try a few common properties.
            var installed = GetBool(root, "installed", true) ?? GetBool(root, "isInstalled", true) ?? true;
            if (!installed) return;

            var id = GetString(root, "id")
                ?? GetString(root, "productId")
                ?? GetString(root, "asin")
                ?? GetString(root, "gameId")
                ?? Path.GetFileNameWithoutExtension(file);

            var name = GetString(root, "title")
                ?? GetString(root, "name")
                ?? GetString(root, "productTitle")
                ?? GetString(root, "displayName")
                ?? id;

            var installPath = GetString(root, "installPath")
                ?? GetString(root, "installLocation")
                ?? GetString(root, "path")
                ?? "";

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return;

            games.Add(new GameEntry
            {
                Platform = "amazon",
                Id = id,
                Name = name,
                InstallPath = installPath,
                SupportsPlaytime = false
            });
        }
        catch
        {
            // ignore
        }
    }

    static string? GetString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    static bool? GetBool(JsonElement el, string prop, bool? fallback)
    {
        if (!el.TryGetProperty(prop, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }
}
