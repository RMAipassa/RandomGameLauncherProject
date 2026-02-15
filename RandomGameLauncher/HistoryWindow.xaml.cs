using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using RandomGameLauncher.Services;

namespace RandomGameLauncher;

public partial class HistoryWindow : Window, INotifyPropertyChanged
{
    public sealed class Row
    {
        public string TimeLocal { get; init; } = "";
        public string Name { get; init; } = "";
        public string Platform { get; init; } = "";
        public bool Launched { get; init; }
        public double SessionMinutes { get; init; }
        public string Tags { get; init; } = "";
        public string Error { get; init; } = "";
        public Guid Id { get; init; }
    }

    readonly Config _cfg;
    readonly Action _save;
    readonly AppTheme _theme;
    readonly BackdropKind _backdrop;

    public ObservableCollection<Row> Entries { get; } = new();

    string _statsText = "";
    public string StatsText { get => _statsText; set { _statsText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatsText))); } }

    public HistoryWindow(Config cfg, Action save, AppTheme theme, BackdropKind backdrop)
    {
        InitializeComponent();
        DataContext = this;

        _cfg = cfg;
        _save = save;
        _theme = theme;
        _backdrop = backdrop;

        SourceInitialized += async (_, _) => await ThemeManager.ApplyAsync(this, RootHost, _theme, _backdrop, animate: false);

        Reload();
    }

    void Reload()
    {
        Entries.Clear();

        foreach (var e in _cfg.LaunchHistory.OrderByDescending(x => x.TimestampUtc))
        {
            Entries.Add(new Row
            {
                Id = e.Id,
                TimeLocal = HistoryService.FormatLocal(e.TimestampUtc),
                Name = e.Name,
                Platform = e.Platform,
                Launched = e.Launched,
                SessionMinutes = Math.Round(e.SessionSeconds / 60.0, 1),
                Tags = e.FilterTagsCsv,
                Error = e.Error
            });
        }

        StatsText = ComputeStatsText();
    }

    public void ReloadFromExternal() => Reload();

    string ComputeStatsText()
    {
        var sb = new StringBuilder();
        var list = _cfg.LaunchHistory;

        sb.AppendLine($"Total launches: {list.Count}");
        sb.AppendLine($"Successful launches: {list.Count(x => x.Launched)}");

        var since7 = DateTime.UtcNow.AddDays(-7);
        sb.AppendLine($"Last 7 days: {list.Count(x => x.TimestampUtc >= since7)}");

        sb.AppendLine();

        var byPlatform = list.GroupBy(x => x.Platform).OrderByDescending(g => g.Count());
        sb.AppendLine("By platform:");
        foreach (var g in byPlatform)
            sb.AppendLine($"- {g.Key}: {g.Count()}");

        sb.AppendLine();

        var topGames = list
            .GroupBy(x => x.GameKey)
            .Select(g => new { GameKey = g.Key, Count = g.Count(), Name = g.OrderByDescending(x => x.TimestampUtc).First().Name })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        sb.AppendLine("Top games (by random picks):");
        foreach (var g in topGames)
            sb.AppendLine($"- {g.Name}: {g.Count}");

        sb.AppendLine();

        var totalTrackedSec = _cfg.TrackedPlaytimeSeconds.Values.Sum();
        sb.AppendLine($"Tracked playtime (launched via this app): {Math.Round(totalTrackedSec / 3600.0, 1)} hrs");

        return sb.ToString().TrimEnd();
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_cfg.LaunchHistory.Count == 0) return;
        _cfg.LaunchHistory.Clear();
        _save();
        Reload();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
