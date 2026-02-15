using System.IO;
using System.Text.Json;
using RandomGameLauncher.Models;

namespace RandomGameLauncher.Services;

public static class EpicScanner
{
    public static List<GameEntry> Scan()
    {
        var dir = GetManifestDir();
        if (dir is null || !Directory.Exists(dir)) return new List<GameEntry>();

        var games = new List<GameEntry>();

        foreach (var file in Directory.EnumerateFiles(dir, "*.item", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json);

                var root = doc.RootElement;

                var display = GetString(root, "DisplayName");
                var appName = GetString(root, "AppName");
                var install = GetString(root, "InstallLocation");
                var installed = GetBool(root, "bIsInstalled", true);

                if (!installed) continue;
                if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(appName)) continue;

                games.Add(new GameEntry
                {
                    Platform = "epic",
                    Name = display,
                    Id = appName,
                    InstallPath = install ?? "",
                    SupportsPlaytime = false
                });
            }
            catch { }
        }

        return games
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Platform)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string? GetManifestDir()
    {
        var pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(pd, "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    static string? GetString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    static bool GetBool(JsonElement el, string prop, bool fallback)
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
