using System.ComponentModel;
using System.Runtime.CompilerServices;

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
        set { _playtimeHours = value; OnPropertyChanged(); }
    }

    public string Key => $"{Platform}:{Id}";

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
