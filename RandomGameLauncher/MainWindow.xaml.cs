using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using RandomGameLauncher.Models;
using RandomGameLauncher.Services;

namespace RandomGameLauncher;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    readonly ObservableCollection<GameEntry> _games = new();

    readonly ICollectionView _viewAll;

    readonly Config _cfg;
    TrayService? _tray;

    string _searchText = "";
    public string SearchText { get => _searchText; set { _searchText = value; RefreshViews(); OnPropertyChanged(); } }

    AppTheme _theme = AppTheme.System;
    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme == value) return;
            _theme = value;
            SaveConfig();
            _ = ThemeManager.ApplyAsync(this, RootGrid, Theme, Backdrop, animate: true);
            OnPropertyChanged();
        }
    }

    BackdropKind _backdrop = BackdropKind.Mica;
    public BackdropKind Backdrop
    {
        get => _backdrop;
        set
        {
            if (_backdrop == value) return;
            _backdrop = value;
            SaveConfig();
            _ = ThemeManager.ApplyAsync(this, RootGrid, Theme, Backdrop, animate: true);
            OnPropertyChanged();
        }
    }

    bool _includeSteam = true;
    public bool IncludeSteam
    {
        get => _includeSteam;
        set { _includeSteam = value; SaveConfig(); RefreshLibrary(); OnPropertyChanged(); }
    }

    bool _includeEpic = true;
    public bool IncludeEpic
    {
        get => _includeEpic;
        set { _includeEpic = value; SaveConfig(); RefreshLibrary(); OnPropertyChanged(); }
    }

    bool _favoritesOnly;
    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set { _favoritesOnly = value; _tray?.UpdateFavoritesOnlyChecked(value); SaveConfig(); RefreshViews(); OnPropertyChanged(); }
    }

    bool _usePlaytimeWeighting;
    public bool UsePlaytimeWeighting
    {
        get => _usePlaytimeWeighting;
        set { _usePlaytimeWeighting = value; SaveConfig(); OnPropertyChanged(); }
    }

    bool _startMinimizedToTray;
    public bool StartMinimizedToTray
    {
        get => _startMinimizedToTray;
        set { _startMinimizedToTray = value; SaveConfig(); OnPropertyChanged(); }
    }

    bool _minimizeToTray = true;
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set { _minimizeToTray = value; SaveConfig(); OnPropertyChanged(); }
    }

    string _steamId64 = "";
    public string SteamId64
    {
        get => _steamId64;
        set { _steamId64 = value; SaveConfig(); OnPropertyChanged(); }
    }

    string _statusText = "Ready";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }


    public ICommand RefreshCommand { get; }
    public ICommand LaunchRandomCommand { get; }
    public ICommand FetchPlaytimeCommand { get; }
    public ICommand FocusSearchCommand { get; }

    public ICollectionView ViewAll => _viewAll;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _cfg = ConfigService.Load();

        _includeSteam = _cfg.IncludeSteam;
        _includeEpic = _cfg.IncludeEpic;
        _favoritesOnly = _cfg.FavoritesOnly;
        _usePlaytimeWeighting = _cfg.UsePlaytimeWeighting;
        _steamId64 = _cfg.SteamId64;
        _startMinimizedToTray = _cfg.StartMinimizedToTray;
        _minimizeToTray = _cfg.MinimizeToTray;
        _theme = _cfg.Theme;
        _backdrop = _cfg.Backdrop;

        ThemeCombo.ItemsSource = Enum.GetValues<AppTheme>();
        BackdropCombo.ItemsSource = Enum.GetValues<BackdropKind>();

        ThemeManager.Initialize();
        _ = ThemeManager.ApplyAsync(this, RootGrid, Theme, Backdrop, animate: false);
        SourceInitialized += async (_, _) => await ThemeManager.ApplyAsync(this, RootGrid, Theme, Backdrop, animate: false);

        RefreshCommand = new RelayCommand(_ => RefreshLibrary());
        LaunchRandomCommand = new RelayCommand(_ => LaunchRandom());
        FetchPlaytimeCommand = new RelayCommand(async _ => await FetchPlaytimeAsync());
        FocusSearchCommand = new RelayCommand(_ => SearchBox.Focus());

        _viewAll = CollectionViewSource.GetDefaultView(_games);
        _viewAll.Filter = _ => FilterAll(_ as GameEntry);

        RestoreWindowPlacement();

        var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "dice.ico");
        _tray = new TrayService(
            iconPath,
            open: () => Dispatcher.Invoke(ShowAndActivate),
            launchRandom: () => Dispatcher.Invoke(LaunchRandom),
            getFavOnly: () => FavoritesOnly,
            setFavOnly: v => Dispatcher.Invoke(() => FavoritesOnly = v)
        );

        RefreshLibrary();

        Loaded += (_, _) =>
        {
            if (StartMinimizedToTray)
            {
                Hide();
                WindowState = WindowState.Minimized;
            }
        };

        StateChanged += (_, _) =>
        {
            if (MinimizeToTray && WindowState == WindowState.Minimized)
                Hide();
        };
    }

    void RestoreWindowPlacement()
    {
        if (_cfg.WindowWidth > 200) Width = _cfg.WindowWidth;
        if (_cfg.WindowHeight > 200) Height = _cfg.WindowHeight;

        if (_cfg.WindowLeft.HasValue && _cfg.WindowTop.HasValue)
        {
            Left = _cfg.WindowLeft.Value;
            Top = _cfg.WindowTop.Value;
        }

        if (Enum.TryParse<WindowState>(_cfg.WindowState, out var ws))
            WindowState = ws;

        EnsureOnScreen();
    }

    void EnsureOnScreen()
    {
        var wa = SystemParameters.WorkArea;
        if (Left + 50 > wa.Right) Left = wa.Right - 50;
        if (Top + 50 > wa.Bottom) Top = wa.Bottom - 50;
        if (Left < wa.Left) Left = wa.Left;
        if (Top < wa.Top) Top = wa.Top;
    }

    void SaveWindowPlacement()
    {
        _cfg.WindowWidth = Width;
        _cfg.WindowHeight = Height;
        _cfg.WindowLeft = Left;
        _cfg.WindowTop = Top;
        _cfg.WindowState = WindowState.ToString();
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    bool FilterCore(GameEntry? g)
    {
        if (g is null) return false;
        if (FavoritesOnly && !g.Favorite) return false;
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !g.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    bool FilterAll(GameEntry? g) => FilterCore(g);

    void RefreshViews()
    {
        _viewAll.Refresh();
    }

    void ApplyConfigToGame(GameEntry g)
    {
        g.Included = !_cfg.Excluded.Contains(g.Key);
        g.Favorite = _cfg.Favorites.Contains(g.Key);

        g.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameEntry.Included))
            {
                if (g.Included) _cfg.Excluded.Remove(g.Key);
                else _cfg.Excluded.Add(g.Key);
                SaveConfig();
            }

            if (e.PropertyName == nameof(GameEntry.Favorite))
            {
                if (g.Favorite) _cfg.Favorites.Add(g.Key);
                else _cfg.Favorites.Remove(g.Key);
                SaveConfig();
                RefreshViews();
            }
        };
    }

    void RefreshLibrary()
    {
        StatusText = "Scanning installed games...";
        _games.Clear();

        var list = new List<GameEntry>();
        if (IncludeSteam) list.AddRange(SteamScanner.Scan());
        if (IncludeEpic) list.AddRange(EpicScanner.Scan());

        foreach (var g in list)
        {
            ApplyConfigToGame(g);
            _games.Add(g);
        }

        RefreshViews();
        StatusText = $"Loaded {_games.Count} games";
        SaveConfig();
    }

    void LaunchRandom()
    {
        var pool = _games
            .Where(g => g.Included)
            .Where(g => !FavoritesOnly || g.Favorite)
            .ToList();

        var pick = WeightedPicker.Pick(pool, UsePlaytimeWeighting);
        if (pick is null)
        {
            StatusText = "No games available (check Included/Favorites settings)";
            return;
        }

        StatusText = $"Launching: {pick.Name} ({pick.Platform})";

        try
        {
            if (pick.Platform == "steam")
                Process.Start(new ProcessStartInfo($"steam://rungameid/{pick.Id}") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo($"com.epicgames.launcher://apps/{pick.Id}?action=launch&silent=true") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    async Task FetchPlaytimeAsync()
    {
        var apiKey = ConfigService.Unprotect(_cfg.SteamApiKeyProtected);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(SteamId64))
        {
            StatusText = "Enter SteamID64 and API key, then Save";
            return;
        }

        StatusText = "Fetching Steam playtime...";
        try
        {
            var map = await SteamPlaytimeService.FetchPlaytimeHoursAsync(apiKey, SteamId64);
            foreach (var g in _games.Where(x => x.Platform == "steam"))
            {
                if (map.TryGetValue(g.Id, out var hrs)) g.PlaytimeHours = hrs;
            }
            StatusText = "Steam playtime updated";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    void SaveConfig()
    {
        _cfg.FavoritesOnly = FavoritesOnly;
        _cfg.UsePlaytimeWeighting = UsePlaytimeWeighting;
        _cfg.StartMinimizedToTray = StartMinimizedToTray;
        _cfg.MinimizeToTray = MinimizeToTray;
        _cfg.SteamId64 = SteamId64;
        _cfg.IncludeSteam = IncludeSteam;
        _cfg.IncludeEpic = IncludeEpic;
        _cfg.Theme = Theme;
        _cfg.Backdrop = Backdrop;
        ConfigService.Save(_cfg);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveWindowPlacement();
        SaveConfig();
        _tray?.Dispose();
        base.OnClosing(e);
    }

    void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Password ?? "";
        _cfg.SteamApiKeyProtected = ConfigService.Protect(apiKey);
        SaveConfig();
        StatusText = "API key saved";
        ApiKeyBox.Password = "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

}

public sealed class RelayCommand : ICommand
{
    readonly Action<object?> _exec;
    readonly Func<object?, bool>? _can;

    public RelayCommand(Action<object?> exec, Func<object?, bool>? can = null)
    {
        _exec = exec;
        _can = can;
    }

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _exec(parameter);
    public event EventHandler? CanExecuteChanged;
}
