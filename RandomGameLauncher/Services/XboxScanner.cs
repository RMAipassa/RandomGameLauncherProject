using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using RandomGameLauncher.Models;

namespace RandomGameLauncher.Services;

public static class XboxScanner
{
    public static List<GameEntry> Scan()
    {
        // Use GamingServices GameConfig as the source of truth.
        // This excludes random Microsoft Store apps (Netflix, etc.) and focuses on Xbox/GDK titles.
        var games = new List<GameEntry>();

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\GamingServices\GameConfig");
            if (root is null) return games;

            foreach (var sub in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(sub);
                if (k is null) continue;

                var displayName = ReadString(k, @"ShellVisuals\DefaultDisplayName")
                    ?? ReadString(k, @"ShellVisuals\OverrideDisplayName")
                    ?? ReadString(k, "Name");

                if (string.IsNullOrWhiteSpace(displayName)) continue;

                // Filter out DLC / launch trackers / stubs.
                if (LooksLikeNonGame(displayName)) continue;

                if (!HasExecutable(k)) continue;

                var pkgName = (k.GetValue("Name") as string)?.Trim();
                if (string.IsNullOrWhiteSpace(pkgName))
                {
                    pkgName = GuessPackageNameFromKey(sub);
                }

                var familySuffix = GetFamilySuffixFromKey(sub);
                if (string.IsNullOrWhiteSpace(pkgName) || string.IsNullOrWhiteSpace(familySuffix)) continue;

                var pfn = $"{pkgName}_{familySuffix}";

                games.Add(new GameEntry
                {
                    Platform = "xbox",
                    Id = pfn,
                    Name = displayName,
                    InstallPath = "", // install location is not needed for launching; often inaccessible anyway
                    SupportsPlaytime = false
                });
            }
        }
        catch
        {
            // ignore
        }

        return games
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Platform)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryGetLaunchTarget(string packageFamilyName, out string fileName, out string arguments)
    {
        fileName = "";
        arguments = "";
        if (string.IsNullOrWhiteSpace(packageFamilyName)) return false;

        // Resolve AppID from start menu and launch via AppsFolder.
        var script =
            "$pfn = '" + EscapePs(packageFamilyName) + "'; " +
            "$app = Get-StartApps | Where-Object { $_.AppID -like '*" + EscapeLike(packageFamilyName) + "*' } | Select-Object -First 1; " +
            "if($null -ne $app) { $app.AppID }";

        var appId = RunPwsh(script).Trim();
        if (string.IsNullOrWhiteSpace(appId)) return false;

        fileName = "explorer.exe";
        arguments = $"shell:AppsFolder\\{appId}";
        return true;
    }

    static bool LooksLikeNonGame(string name)
    {
        var n = name.Trim();
        if (n.Length == 0) return true;

        // Common non-game entries in GameConfig.
        var bad = new[]
        {
            "DLC",
            "Content Pack",
            "Launch Tracker",
            "Game Pass Launch Tracker",
            "Stub",
            "Early Access",
        };

        foreach (var b in bad)
            if (n.Contains(b, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    static bool HasExecutable(RegistryKey gameConfigKey)
    {
        try
        {
            using var exeRoot = gameConfigKey.OpenSubKey("Executable");
            if (exeRoot is null) return false;

            foreach (var sub in exeRoot.GetSubKeyNames())
            {
                using var ek = exeRoot.OpenSubKey(sub);
                var name = ek?.GetValue("Name") as string;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch { }

        return false;
    }

    static string? ReadString(RegistryKey root, string subPathAndValue)
    {
        // subPathAndValue: "ShellVisuals\\DefaultDisplayName" etc.
        var idx = subPathAndValue.LastIndexOf('\\');
        if (idx <= 0) return root.GetValue(subPathAndValue) as string;

        var sub = subPathAndValue.Substring(0, idx);
        var value = subPathAndValue.Substring(idx + 1);
        try
        {
            using var k = root.OpenSubKey(sub);
            return k?.GetValue(value) as string;
        }
        catch
        {
            return null;
        }
    }

    static string? GetFamilySuffixFromKey(string keyName)
    {
        // Key looks like: <PackageName>_<Version>_<Arch>__<FamilySuffix>
        var idx = keyName.IndexOf("__", StringComparison.Ordinal);
        if (idx < 0) return null;
        return keyName.Substring(idx + 2);
    }

    static string? GuessPackageNameFromKey(string keyName)
    {
        // Take prefix before first '_' (version separator).
        var idx = keyName.IndexOf('_');
        if (idx <= 0) return null;
        return keyName.Substring(0, idx);
    }

    static string RunPwsh(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = Process.Start(psi);
            if (p is null) return "";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return output;
        }
        catch
        {
            return "";
        }
    }

    static string EscapePs(string s) => s.Replace("'", "''");
    static string EscapeLike(string s) => s.Replace("[", "`[").Replace("]", "`]").Replace("*", "`*").Replace("?", "`?");
}
