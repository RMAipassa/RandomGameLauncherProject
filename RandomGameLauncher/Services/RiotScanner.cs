using Microsoft.Win32;
using System.IO;
using RandomGameLauncher.Models;

namespace RandomGameLauncher.Services;

public static class RiotScanner
{
    const string PatchlineLive = "live";

    public static List<GameEntry> Scan()
    {
        // Riot installs are not standardized across all titles.
        // This scanner tries a few common registry locations for install paths.
        // It will at least find Valorant and League of Legends on most systems.

        var games = new List<GameEntry>();

        TryAddValorant(games);
        TryAddLeague(games);

        // Don't list Riot Client as a game entry.

        return games
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Platform)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryGetLaunchCommand(string riotGameId, out string fileName, out string arguments)
    {
        fileName = "";
        arguments = "";

        if (string.IsNullOrWhiteSpace(riotGameId)) return false;

        var exe = TryGetRiotClientServicesExe();
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return false;

        // RiotClientServices.exe supports these args for launching games.
        // Valorant typically uses patchline "live".
        // League typically uses patchline "live".
        var product = riotGameId switch
        {
            "valorant" => "valorant",
            "league_of_legends" => "league_of_legends",
            "riot_client" => "Riot_Client",
            _ => riotGameId
        };

        var patchline = riotGameId == "riot_client" ? "" : PatchlineLive;

        fileName = exe;
        arguments = $"--launch-product={product} --launch-patchline={patchline}";
        return true;
    }

    static void TryAddValorant(List<GameEntry> games)
    {
        // Best source: uninstall entry (works even if installed to non-C drives).
        var install = FindByUninstallDisplayName("VALORANT");
        if (!string.IsNullOrWhiteSpace(install) && Directory.Exists(install))
        {
            games.Add(new GameEntry
            {
                Platform = "riot",
                Id = "valorant",
                Name = "VALORANT",
                InstallPath = install!,
                SupportsPlaytime = false
            });
            return;
        }

        // Fallback: under Riot Games root.
        var baseDir = TryReadRiotGamesRoot();
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            var p = Path.Combine(baseDir!, "VALORANT", PatchlineLive);
            if (Directory.Exists(p))
            {
                games.Add(new GameEntry
                {
                    Platform = "riot",
                    Id = "valorant",
                    Name = "VALORANT",
                    InstallPath = p,
                    SupportsPlaytime = false
                });
            }
        }

        // Some installs put a direct InstallPath value.
        var installPathFromReg = TryReadStringAnyView(RegistryHive.LocalMachine, @"SOFTWARE\Riot Games\VALORANT", "InstallPath")
            ?? TryReadStringAnyView(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Riot Games\VALORANT", "InstallPath");
        if (!string.IsNullOrWhiteSpace(installPathFromReg) && Directory.Exists(installPathFromReg))
        {
            games.Add(new GameEntry
            {
                Platform = "riot",
                Id = "valorant",
                Name = "VALORANT",
                InstallPath = installPathFromReg!,
                SupportsPlaytime = false
            });
        }
    }

    static void TryAddLeague(List<GameEntry> games)
    {
        // League path is often stored under Uninstall.
        var leagueDir = FindByUninstallDisplayName("League of Legends");
        if (!string.IsNullOrWhiteSpace(leagueDir) && Directory.Exists(leagueDir))
        {
            games.Add(new GameEntry
            {
                Platform = "riot",
                Id = "league_of_legends",
                Name = "League of Legends",
                InstallPath = leagueDir!,
                SupportsPlaytime = false
            });
        }

        // Fallback: under Riot Games root.
        var baseDir = TryReadRiotGamesRoot();
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            var p = Path.Combine(baseDir!, "League of Legends");
            if (Directory.Exists(p))
            {
                games.Add(new GameEntry
                {
                    Platform = "riot",
                    Id = "league_of_legends",
                    Name = "League of Legends",
                    InstallPath = p,
                    SupportsPlaytime = false
                });
            }
        }
    }

    static void TryAddRiotClient(List<GameEntry> games)
    {
        // Optional entry: Riot Client itself (useful as a launcher).
        var exe = TryGetRiotClientServicesExe();
        if (string.IsNullOrWhiteSpace(exe)) return;

        try
        {
            var dir = Path.GetDirectoryName(exe);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                games.Add(new GameEntry
                {
                    Platform = "riot",
                    Id = "riot_client",
                    Name = "Riot Client",
                    InstallPath = dir!,
                    SupportsPlaytime = false
                });
            }
        }
        catch { }
    }

    static string? TryReadRiotGamesRoot()
    {
        // Prefer Riot Client install location.
        var p = FindByUninstallDisplayName("Riot Client");
        if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
        {
            try
            {
                // If p is "...\Riot Client", its parent is the Riot Games root.
                var parent = Directory.GetParent(p!)?.FullName;
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) return parent;
                return p;
            }
            catch { }
        }

        // Last resort default.
        var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Riot Games");
        return Directory.Exists(def) ? def : null;
    }

    static string? TryGetRiotClientServicesExe()
    {
        // Most reliable: uninstall entry includes full path to RiotClientServices.exe
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (root is null) continue;

                    foreach (var sub in root.GetSubKeyNames())
                    {
                        using var k = root.OpenSubKey(sub);
                        if (k is null) continue;

                        var dn = k.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(dn) || !dn.StartsWith("Riot Client", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var u = k.GetValue("UninstallString") as string;
                        var exe = !string.IsNullOrWhiteSpace(u) ? ExtractExePath(u!) : null;
                        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe)) return exe;

                        var loc = k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(loc))
                        {
                            var c = Path.Combine(loc!, "RiotClientServices.exe");
                            if (File.Exists(c)) return c;
                        }
                    }
                }
                catch { }
            }
        }

        // Registry keys fallback.
        var path = TryReadStringAnyView(RegistryHive.LocalMachine, @"SOFTWARE\Riot Games\Riot Client", "Path")
            ?? TryReadStringAnyView(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Riot Games\Riot Client", "Path")
            ?? TryReadStringAnyView(RegistryHive.CurrentUser, @"SOFTWARE\Riot Games\Riot Client", "Path")
            ?? TryReadStringAnyView(RegistryHive.CurrentUser, @"SOFTWARE\WOW6432Node\Riot Games\Riot Client", "Path");

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
        return null;
    }

    static string? FindByUninstallDisplayName(string displayName)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                var p = FindInUninstall(hive, view, displayName: displayName, publisher: null);
                if (!string.IsNullOrWhiteSpace(p)) return p;
            }
        }
        return null;
    }

    static string? FindByUninstallPublisher(string publisher)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                var p = FindInUninstall(hive, view, displayName: null, publisher: publisher);
                if (!string.IsNullOrWhiteSpace(p)) return p;
            }
        }
        return null;
    }

    static string? FindInUninstall(RegistryHive hive, RegistryView view, string? displayName, string? publisher)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (root is null) return null;

            foreach (var sub in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(sub);
                if (k is null) continue;

                var dn = k.GetValue("DisplayName") as string;
                var pub = k.GetValue("Publisher") as string;

                if (displayName is not null && !string.Equals(dn, displayName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (publisher is not null && (pub is null || !pub.Contains(publisher, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Prefer InstallLocation.
                var loc = k.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(loc) && Directory.Exists(loc)) return loc;

                // Fallback: parse UninstallString directory.
                var u = k.GetValue("UninstallString") as string;
                if (!string.IsNullOrWhiteSpace(u))
                {
                    var exe = ExtractExePath(u);
                    if (!string.IsNullOrWhiteSpace(exe))
                    {
                        var dir = Path.GetDirectoryName(exe);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) return dir;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    static string? TryReadStringAnyView(RegistryHive hive, string subKey, string valueName)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var k = baseKey.OpenSubKey(subKey);
                var v = k?.GetValue(valueName) as string;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            catch { }
        }
        return null;
    }

    static string? ExtractExePath(string command)
    {
        var s = command.Trim();
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            if (end > 1) return s.Substring(1, end - 1);
            return null;
        }

        var idx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return s.Substring(0, idx + 4);
    }
}
