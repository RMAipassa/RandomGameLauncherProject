using RandomGameLauncher.Models;

namespace RandomGameLauncher.Services;

public sealed class LaunchHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public string GameKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";

    public bool Launched { get; set; }
    public string Error { get; set; } = "";

    public string FilterTagsCsv { get; set; } = "";
    public bool MatchAllTags { get; set; }

    public long SessionSeconds { get; set; }
}

public static class HistoryService
{
    public const int MaxEntries = 1000;

    public static LaunchHistoryEntry AddLaunch(Config cfg, GameEntry g, IReadOnlyList<string> filterTags, bool matchAll, bool launched, string error)
    {
        var e = new LaunchHistoryEntry
        {
            TimestampUtc = DateTime.UtcNow,
            GameKey = g.Key,
            Name = g.Name,
            Platform = g.Platform,
            Launched = launched,
            Error = error ?? "",
            FilterTagsCsv = string.Join(",", filterTags ?? Array.Empty<string>()),
            MatchAllTags = matchAll,
            SessionSeconds = 0
        };

        cfg.LaunchHistory.Add(e);
        Trim(cfg);
        return e;
    }

    public static void Trim(Config cfg)
    {
        var overflow = cfg.LaunchHistory.Count - MaxEntries;
        if (overflow <= 0) return;
        cfg.LaunchHistory.RemoveRange(0, overflow);
    }

    public static string FormatLocal(DateTime utc)
    {
        try { return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); }
        catch { return utc.ToString("u"); }
    }
}
