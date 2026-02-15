using System.ComponentModel;
using System.Runtime.CompilerServices;

using System.Collections.Generic;
using System.Linq;

namespace RandomGameLauncher.Models;

public sealed class GameEntry : INotifyPropertyChanged
{
    public string Platform { get; init; } = "";
    public string Name { get; init; } = "";
    public string Id { get; init; } = "";
    public string InstallPath { get; init; } = "";
    public bool SupportsPlaytime { get; init; }

    bool _included = true;
    public bool Included
    {
        get => _included;
        set { _included = value; OnPropertyChanged(); }
    }

    bool _favorite;
    public bool Favorite
    {
        get => _favorite;
        set { _favorite = value; OnPropertyChanged(); }
    }

    double? _playtimeHours;
    public double? PlaytimeHours
    {
        get => _playtimeHours;
        set { _playtimeHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayPlaytimeHours)); }
    }

    double? _trackedPlaytimeHours;
    public double? TrackedPlaytimeHours
    {
        get => _trackedPlaytimeHours;
        set { _trackedPlaytimeHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayPlaytimeHours)); }
    }

    public double DisplayPlaytimeHours => PlaytimeHours ?? TrackedPlaytimeHours ?? 0.0;

    IReadOnlyList<string> _tags = Array.Empty<string>();
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        set
        {
            _tags = value ?? Array.Empty<string>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TagsText));
            OnPropertyChanged(nameof(AllTags));
            OnPropertyChanged(nameof(AllTagsText));
        }
    }

    public string TagsText => _tags.Count == 0 ? "" : string.Join(", ", _tags);

    IReadOnlyList<string> _autoTags = Array.Empty<string>();
    public IReadOnlyList<string> AutoTags
    {
        get => _autoTags;
        set
        {
            _autoTags = value ?? Array.Empty<string>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AllTags));
            OnPropertyChanged(nameof(AllTagsText));
        }
    }

    public IReadOnlyList<string> AllTags => _tags.Concat(_autoTags)
        .Where(t => !string.IsNullOrWhiteSpace(t))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public string AllTagsText => AllTags.Count == 0 ? "" : string.Join(", ", AllTags);

    public string Key => $"{Platform}:{Id}";

    public string PlatformLabel => (Platform ?? "").ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
