using Microsoft.Win32;
using RandomGameLauncher.Models;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RandomGameLauncher.Services;

public static class UbisoftScanner
{
    public static List<GameEntry> Scan()
    {
        var games = new List<GameEntry>();

        var nameMap = TryLoadNameMap();

        // Installed games are reliably represented by HKLM entries with InstallDir.
        TryScanInstalls(games, nameMap, RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Ubisoft\Launcher\Installs");
        TryScanInstalls(games, nameMap, RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Ubisoft\Launcher\Installs");

        // Some systems store installs under HKCU; only include if an InstallDir exists.
        TryScanInstalls(games, nameMap, RegistryHive.CurrentUser, RegistryView.Registry32, @"SOFTWARE\Ubisoft\Launcher\Installs");
        TryScanInstalls(games, nameMap, RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Ubisoft\Launcher\Installs");

        return games
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Platform)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetLaunchUri(string gameId) => $"uplay://launch/{gameId}/0";

    static void TryScanInstalls(List<GameEntry> games, Dictionary<string, string> nameMap, RegistryHive hive, RegistryView view, string path)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(path);
            if (root is null) return;

            foreach (var sub in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(sub);
                if (k is null) continue;

                var installDir = k.GetValue("InstallDir") as string;
                if (string.IsNullOrWhiteSpace(sub)) continue;

                // Only include installed entries with an actual directory.
                if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) continue;

                var name = (k.GetValue("DisplayName") as string)
                    ?? (k.GetValue("InstallName") as string)
                    ?? (nameMap.TryGetValue(sub, out var mapped) ? mapped : null)
                    ?? sub;

                name = (name ?? sub).Trim();
                if (name.Length == 0) continue;

                games.Add(new GameEntry
                {
                    Platform = "ubisoft",
                    Id = sub,
                    Name = name,
                    InstallPath = installDir ?? "",
                    SupportsPlaytime = false
                });
            }
        }
        catch
        {
            // ignore
        }
    }

    static Dictionary<string, string> TryLoadNameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var baseDir = @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher";
        if (!Directory.Exists(baseDir)) return map;

        var cfgPath = Path.Combine(baseDir, "cache", "configuration", "configurations");
        if (!File.Exists(cfgPath)) return map;

        try
        {
            var bytes = File.ReadAllBytes(cfgPath);
            var text = Encoding.UTF8.GetString(bytes);

            // Each config references one or more registry install ids in lines like:
            // register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\420\InstallDir
            // Then a display name is typically in either:
            // - localizations: default: GAMENAME: "Far Cry® 4"
            // - root: name: l1  + localizations: default: l1: For Honor
            var blocks = text.Split(new[] { "version: 2.0" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var b in blocks)
            {
                var ids = Regex.Matches(b, @"Installs\\(?<id>\d+)\\InstallDir", RegexOptions.IgnoreCase)
                    .Select(m => m.Groups["id"].Value)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (ids.Count == 0) continue;

                var name = ExtractName(b);
                if (string.IsNullOrWhiteSpace(name)) continue;

                foreach (var id in ids)
                    map.TryAdd(id, name);
            }
        }
        catch
        {
            // ignore
        }

        return map;
    }

    static string? ExtractName(string block)
    {
        // Prefer explicit GAMENAME localization.
        var m = Regex.Match(block, "\\bGAMENAME\\s*:\\s*\"(?<n>[^\"]+)\"", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups["n"].Value.Trim();

        // root: name: <token>
        var tokenMatch = Regex.Match(block, @"\broot\s*:\s*(?:\r?\n)+\s*name\s*:\s*(?<t>[A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
        if (tokenMatch.Success)
        {
            var t = tokenMatch.Groups["t"].Value.Trim();
            if (t.Length > 0)
            {
                // localizations: default: <token>: <name>
                var rx = new Regex(@"\blocalizations\s*:\s*(?:\r?\n)+\s*default\s*:\s*(?:\r?\n)+(?:\s*" + Regex.Escape(t) + @"\s*:\s*(?<n>.+))", RegexOptions.IgnoreCase);
                var lm = rx.Match(block);
                if (lm.Success)
                {
                    var raw = lm.Groups["n"].Value.Trim();
                    raw = raw.Trim().Trim('"').Trim();
                    if (raw.Length > 0) return raw;
                }
            }
        }

        // Fallback: shortcut_name in executables.
        var sm = Regex.Match(block, @"\bshortcut_name\s*:\s*(?<n>.+)", RegexOptions.IgnoreCase);
        if (sm.Success)
        {
            var raw = sm.Groups["n"].Value.Trim();
            raw = raw.Trim().Trim('"').Trim();
            if (raw.Length > 0) return raw;
        }

        return null;
    }
}
