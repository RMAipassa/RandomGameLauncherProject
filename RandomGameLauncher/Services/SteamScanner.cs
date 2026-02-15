using System.IO;
using System.Text.RegularExpressions;
using RandomGameLauncher.Models;

namespace RandomGameLauncher.Services;

public static class SteamScanner
{
    public static List<GameEntry> Scan()
    {
        var root = FindSteamRoot();
        if (root is null) return new List<GameEntry>();

        var libs = GetLibraryFolders(root);
        var games = new List<GameEntry>();

        foreach (var lib in libs)
        {
            var steamApps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamApps)) continue;

            foreach (var acf in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                var text = SafeRead(acf);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var appId = Match(text, "\"appid\"\\s*\"(\\d+)\"");
                var name = Match(text, "\"name\"\\s*\"([^\\\"]+)\"");
                var installDir = Match(text, "\"installdir\"\\s*\"([^\\\"]+)\"");

                if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name)) continue;

                var installPath = "";
                if (!string.IsNullOrWhiteSpace(installDir))
                    installPath = Path.Combine(lib, "steamapps", "common", installDir);

                games.Add(new GameEntry
                {
                    Platform = "steam",
                    Name = name,
                    Id = appId,
                    InstallPath = installPath,
                    SupportsPlaytime = true
                });
            }
        }

        return games
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Platform)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string? FindSteamRoot()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var c1 = Path.Combine(pf86, "Steam");
        if (Directory.Exists(c1)) return c1;

        var c2 = Path.Combine(pf, "Steam");
        if (Directory.Exists(c2)) return c2;

        var c3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam");
        if (Directory.Exists(c3)) return c3;

        return null;
    }

    static List<string> GetLibraryFolders(string steamRoot)
    {
        var libs = new List<string> { steamRoot };
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return libs;

        var text = SafeRead(vdf);
        if (string.IsNullOrWhiteSpace(text)) return libs;

        foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\\\"]+)\"", RegexOptions.IgnoreCase))
        {
            var p = m.Groups[1].Value.Replace("\\\\", "\\").Trim();
            if (Directory.Exists(p)) libs.Add(p);
        }

        return libs.Select(Path.GetFullPath)
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return ""; }
    }

    static string Match(string text, string pattern)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }
}
