using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RandomGameLauncher.Services;

public sealed class Config
{
    public HashSet<string> Excluded { get; set; } = new();
    public HashSet<string> Favorites { get; set; } = new();

    public bool FavoritesOnly { get; set; }
    public bool UsePlaytimeWeighting { get; set; }

    public bool IncludeSteam { get; set; } = true;
    public bool IncludeEpic { get; set; } = true;

    public bool StartMinimizedToTray { get; set; }
    public bool MinimizeToTray { get; set; } = true;

    public int LastTabIndex { get; set; }

    public string SteamId64 { get; set; } = "";
    public string SteamApiKeyProtected { get; set; } = "";

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 980;
    public double WindowHeight { get; set; } = 650;
    public string WindowState { get; set; } = "Normal";

    public AppTheme Theme { get; set; } = AppTheme.System;
    public BackdropKind Backdrop { get; set; } = BackdropKind.Mica;

    public Dictionary<string, long> TrackedPlaytimeSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> TagsByGameKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> AutoTagsByGameKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> SteamPlaytimeHoursByGameKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class ConfigService
{
    static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RandomGameLauncher");

    static string ConfigPath => Path.Combine(AppDataDir, "config.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Config Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new Config();
            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            var cfg = JsonSerializer.Deserialize<Config>(json, Options) ?? new Config();
            Normalize(cfg);
            return cfg;
        }
        catch
        {
            return new Config();
        }
    }

    static void Normalize(Config cfg)
    {
        cfg.Excluded ??= new HashSet<string>();
        cfg.Favorites ??= new HashSet<string>();

        cfg.TrackedPlaytimeSeconds ??= new Dictionary<string, long>();
        cfg.TrackedPlaytimeSeconds = new Dictionary<string, long>(cfg.TrackedPlaytimeSeconds, StringComparer.OrdinalIgnoreCase);

        cfg.TagsByGameKey ??= new Dictionary<string, List<string>>();
        cfg.TagsByGameKey = new Dictionary<string, List<string>>(cfg.TagsByGameKey, StringComparer.OrdinalIgnoreCase);

        cfg.AutoTagsByGameKey ??= new Dictionary<string, List<string>>();
        cfg.AutoTagsByGameKey = new Dictionary<string, List<string>>(cfg.AutoTagsByGameKey, StringComparer.OrdinalIgnoreCase);

        cfg.SteamPlaytimeHoursByGameKey ??= new Dictionary<string, double>();
        cfg.SteamPlaytimeHoursByGameKey = new Dictionary<string, double>(cfg.SteamPlaytimeHoursByGameKey, StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(Config cfg)
    {
        Directory.CreateDirectory(AppDataDir);
        var json = JsonSerializer.Serialize(cfg, Options);
        File.WriteAllText(ConfigPath, json, Encoding.UTF8);
    }

    public static string Protect(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
        var prot = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(prot);
    }

    public static string Unprotect(string protectedB64)
    {
        if (string.IsNullOrWhiteSpace(protectedB64)) return "";
        try
        {
            var prot = Convert.FromBase64String(protectedB64);
            var bytes = ProtectedData.Unprotect(prot, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return "";
        }
    }
}
