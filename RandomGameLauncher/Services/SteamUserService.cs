using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RandomGameLauncher.Services;

public static class SteamUserService
{
    public static string? TryGetSteamId64FromLocalClient()
    {
        var steamPath = TryGetSteamPath();
        if (string.IsNullOrWhiteSpace(steamPath)) return null;

        var loginUsers = Path.Combine(steamPath, "config", "loginusers.vdf");
        if (!File.Exists(loginUsers)) return null;

        string text;
        try
        {
            // Valve files are generally UTF-8 without BOM, but be tolerant.
            text = File.ReadAllText(loginUsers, Encoding.UTF8);
        }
        catch
        {
            return null;
        }

        // Very small, pragmatic VDF parse: find each SteamID block and its "MostRecent" / "Timestamp".
        // SteamID64 is a 17-digit number starting with 7656...
        var rx = new Regex("\"(?<id>7656\\d{13})\"\\s*\\{(?<body>.*?)\\}", RegexOptions.Singleline);
        var matches = rx.Matches(text);
        if (matches.Count == 0) return null;

        string? bestId = null;
        bool bestMostRecent = false;
        long bestTimestamp = -1;

        foreach (Match m in matches)
        {
            var id = m.Groups["id"].Value;
            var body = m.Groups["body"].Value;

            var mostRecent = TryGetVdfInt(body, "MostRecent") == 1;
            var timestamp = TryGetVdfLong(body, "Timestamp");

            if (bestId is null)
            {
                bestId = id;
                bestMostRecent = mostRecent;
                bestTimestamp = timestamp;
                continue;
            }

            if (mostRecent && !bestMostRecent)
            {
                bestId = id;
                bestMostRecent = true;
                bestTimestamp = timestamp;
                continue;
            }

            if (mostRecent == bestMostRecent && timestamp > bestTimestamp)
            {
                bestId = id;
                bestTimestamp = timestamp;
            }
        }

        return bestId;
    }

    static string? TryGetSteamPath()
    {
        // Prefer HKCU, fallback to common defaults.
        try
        {
            var v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (!string.IsNullOrWhiteSpace(v) && Directory.Exists(v)) return v;
        }
        catch { }

        var p1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(p1)) return p1;

        var p2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam");
        if (Directory.Exists(p2)) return p2;

        return null;
    }

    static int? TryGetVdfInt(string body, string key)
    {
        var s = TryGetVdfString(body, key);
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        return null;
    }

    static long TryGetVdfLong(string body, string key)
    {
        var s = TryGetVdfString(body, key);
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        return -1;
    }

    static string? TryGetVdfString(string body, string key)
    {
        var rx = new Regex("\\\"" + Regex.Escape(key) + "\\\"\\s*\\\"(?<v>[^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        var m = rx.Match(body);
        return m.Success ? m.Groups["v"].Value : null;
    }
}
