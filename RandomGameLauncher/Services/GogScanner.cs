using Microsoft.Win32;
using System.IO;
using RandomGameLauncher.Models;

namespace RandomGameLauncher.Services;

public static class GogScanner
{
    public static List<GameEntry> Scan()
    {
        var games = new List<GameEntry>();

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            // Most GOG installers write to WOW6432Node on 64-bit Windows.
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                TryScan(games, hive, view, @"SOFTWARE\GOG.com\Games");
                TryScan(games, hive, view, @"SOFTWARE\WOW6432Node\GOG.com\Games");
            }
        }

        return games
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Platform)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static void TryScan(List<GameEntry> games, RegistryHive hive, RegistryView view, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(subKey);
            if (root is null) return;

            foreach (var name in root.GetSubKeyNames())
            {
                using var gk = root.OpenSubKey(name);
                if (gk is null) continue;

                var installPath = ReadString(gk, "path") ?? ReadString(gk, "installPath") ?? "";
                var gameName = ReadString(gk, "gameName") ?? ReadString(gk, "Name") ?? ReadString(gk, "name") ?? name;
                var exe = ReadString(gk, "exe") ?? ReadString(gk, "Exe") ?? ReadString(gk, "launchCommand") ?? "";

                games.Add(new GameEntry
                {
                    Platform = "gog",
                    Id = name,
                    Name = gameName,
                    InstallPath = installPath,
                    SupportsPlaytime = false,
                });
            }
        }
        catch
        {
            // ignore
        }
    }

    static string? ReadString(RegistryKey key, string valueName)
    {
        try
        {
            return key.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    public static string? TryGetLaunchTarget(string gogGameId)
    {
        // Prefer an exe path if present; fallback to GOG Galaxy URI.
        var exe = TryReadValueAcrossViews(gogGameId, "exe") ?? TryReadValueAcrossViews(gogGameId, "Exe") ?? TryReadValueAcrossViews(gogGameId, "launchCommand");
        var path = TryReadValueAcrossViews(gogGameId, "path") ?? TryReadValueAcrossViews(gogGameId, "installPath");

        if (!string.IsNullOrWhiteSpace(exe))
        {
            var s = exe.Trim();

            // launchCommand sometimes includes args; keep only the executable part if it's quoted.
            if (s.StartsWith('"'))
            {
                var end = s.IndexOf('"', 1);
                if (end > 1) s = s.Substring(1, end - 1);
            }
            else
            {
                // Split on first space if it looks like "C:\path\game.exe -arg".
                var idx = s.IndexOf(' ');
                if (idx > 0 && s.Contains(".exe", StringComparison.OrdinalIgnoreCase))
                    s = s.Substring(0, idx);
            }

            if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(s))
            {
                try { s = Path.Combine(path!, s); } catch { }
            }

            return s;
        }

        // This URI is handled by GOG Galaxy if installed.
        return $"goggalaxy://openGameView/{gogGameId}";
    }

    static string? TryReadValueAcrossViews(string gogGameId, string valueName)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var sub in new[] { @"SOFTWARE\GOG.com\Games", @"SOFTWARE\WOW6432Node\GOG.com\Games" })
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                        using var root = baseKey.OpenSubKey(sub);
                        using var gk = root?.OpenSubKey(gogGameId);
                        var v = gk?.GetValue(valueName) as string;
                        if (!string.IsNullOrWhiteSpace(v)) return v;
                    }
                    catch { }
                }
            }
        }

        return null;
    }
}
