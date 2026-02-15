using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RandomGameLauncher.Models;
using RandomGameLauncher.Services;

namespace RandomGameLauncher;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    readonly ObservableCollection<GameEntry> _games = new();

    readonly ICollectionView _viewAll;

    readonly Config _cfg;
    TrayService? _tray;
    readonly PlaytimeTracker _playtime;

    HistoryWindow? _history;

    string _searchText = "";
    public string SearchText { get => _searchText; set { _searchText = value; RefreshViews(); OnPropertyChanged(); } }

    string _tagFilterText = "";
    public string TagFilterText
    {
        get => _tagFilterText;
        set { _tagFilterText = value; RefreshViews(); OnPropertyChanged(); }
    }

    List<string> _selectedTagFilter = new();
    public string SelectedTagFilterText => _selectedTagFilter.Count == 0 ? "" : string.Join(", ", _selectedTagFilter);

    public bool HasTagFilter => _selectedTagFilter.Count > 0;

    bool _tagFilterMatchAll;
    public bool TagFilterMatchAll
    {
        get => _tagFilterMatchAll;
        set { _tagFilterMatchAll = value; RefreshViews(); OnPropertyChanged(); }
    }

    bool _isImportingSteamTags;
    public bool CanImportSteamTags => !_isImportingSteamTags;

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

    bool _includeGog = true;
    public bool IncludeGog
    {
        get => _includeGog;
        set { _includeGog = value; SaveConfig(); RefreshLibrary(); OnPropertyChanged(); }
    }

    bool _includeRiot = true;
    public bool IncludeRiot
    {
        get => _includeRiot;
        set { _includeRiot = value; SaveConfig(); RefreshLibrary(); OnPropertyChanged(); }
    }

    bool _includeAmazon = true;
    public bool IncludeAmazon
    {
        get => _includeAmazon;
        set { _includeAmazon = value; SaveConfig(); RefreshLibrary(); OnPropertyChanged(); }
    }

    bool _includeXbox = true;
    public bool IncludeXbox
    {
        get => _includeXbox;
        set { _includeXbox = value; SaveConfig(); RefreshLibrary(); OnPropertyChanged(); }
    }

    bool _includeUbisoft = true;
    public bool IncludeUbisoft
    {
        get => _includeUbisoft;
        set { _includeUbisoft = value; SaveConfig(); RefreshLibrary(); OnPropertyChanged(); }
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
        _playtime = new PlaytimeTracker(_cfg, SaveConfig, s => StatusText = s);
        _playtime.SessionCommitted += (id, sec) =>
        {
            var e = _cfg.LaunchHistory.FirstOrDefault(x => x.Id == id);
            if (e is null) return;
            e.SessionSeconds = Math.Max(e.SessionSeconds, sec);
            SaveConfig();
            Dispatcher.Invoke(() => _history?.ReloadFromExternal());
        };

        _includeSteam = _cfg.IncludeSteam;
        _includeEpic = _cfg.IncludeEpic;
        _includeGog = _cfg.IncludeGog;
        _includeRiot = _cfg.IncludeRiot;
        _includeAmazon = _cfg.IncludeAmazon;
        _includeXbox = _cfg.IncludeXbox;
        _includeUbisoft = _cfg.IncludeUbisoft;
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

        var tags = _selectedTagFilter.Count > 0 ? _selectedTagFilter : TagService.NormalizeTags(TagFilterText);
        if (tags.Count > 0)
        {
            var have = g.AllTags;
            if (TagFilterMatchAll)
            {
                foreach (var t in tags)
                    if (!have.Contains(t, StringComparer.OrdinalIgnoreCase))
                        return false;
            }
            else
            {
                var any = false;
                foreach (var t in tags)
                    if (have.Contains(t, StringComparer.OrdinalIgnoreCase)) { any = true; break; }
                if (!any) return false;
            }
        }

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
        if (IncludeGog) list.AddRange(GogScanner.Scan());
        if (IncludeRiot) list.AddRange(RiotScanner.Scan());
        if (IncludeAmazon) list.AddRange(AmazonScanner.Scan());
        if (IncludeXbox) list.AddRange(XboxScanner.Scan());
        if (IncludeUbisoft) list.AddRange(UbisoftScanner.Scan());

        list = list
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Platform, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var g in list)
        {
            ApplyConfigToGame(g);
            _playtime.SeedFromConfig(g);
            g.Tags = TagService.GetTags(_cfg, g);
            g.AutoTags = TagService.GetAutoTags(_cfg, g);

            if (g.Platform == "steam" && _cfg.SteamPlaytimeHoursByGameKey.TryGetValue(g.Key, out var h) && h > 0)
                g.PlaytimeHours = h;

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
            .Where(g => FilterCore(g))
            .ToList();

        var pick = WeightedPicker.Pick(pool, UsePlaytimeWeighting);
        if (pick is null)
        {
            StatusText = "No games available (check Included/Favorites settings)";
            return;
        }

        StatusText = $"Launching: {pick.Name} ({pick.Platform})";

        var filterTags = _selectedTagFilter.Count > 0 ? _selectedTagFilter : TagService.NormalizeTags(TagFilterText).ToList();

        try
        {
            if (pick.Platform == "steam")
                Process.Start(new ProcessStartInfo($"steam://rungameid/{pick.Id}") { UseShellExecute = true });
            else if (pick.Platform == "epic")
                Process.Start(new ProcessStartInfo($"com.epicgames.launcher://apps/{pick.Id}?action=launch&silent=true") { UseShellExecute = true });
            else if (pick.Platform == "gog")
            {
                var target = GogScanner.TryGetLaunchTarget(pick.Id);
                if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("Could not determine GOG launch target");

                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            else if (pick.Platform == "riot")
            {
                if (!RiotScanner.TryGetLaunchCommand(pick.Id, out var exe, out var args))
                    throw new InvalidOperationException("Could not determine Riot launch command");

                Process.Start(new ProcessStartInfo(exe) { Arguments = args, UseShellExecute = true });
            }
            else if (pick.Platform == "amazon")
            {
                var uri = AmazonScanner.TryGetLaunchTarget(pick.Id);
                if (string.IsNullOrWhiteSpace(uri)) throw new InvalidOperationException("Could not determine Amazon launch URI");
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            else if (pick.Platform == "xbox")
            {
                if (!XboxScanner.TryGetLaunchTarget(pick.Id, out var exe, out var args))
                    throw new InvalidOperationException("Could not determine Xbox launch target");
                Process.Start(new ProcessStartInfo(exe) { Arguments = args, UseShellExecute = true });
            }
            else if (pick.Platform == "ubisoft")
            {
                var uri = UbisoftScanner.GetLaunchUri(pick.Id);
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            else
                throw new InvalidOperationException($"Unknown platform: {pick.Platform}");

            var he = HistoryService.AddLaunch(_cfg, pick, filterTags, TagFilterMatchAll, launched: true, error: "");
            SaveConfig();
            _playtime.Start(pick, he.Id);
        }
        catch (Exception ex)
        {
            HistoryService.AddLaunch(_cfg, pick, filterTags, TagFilterMatchAll, launched: false, error: ex.Message);
            SaveConfig();
            StatusText = ex.Message;
        }
    }

    void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_history is not null)
        {
            _history.Activate();
            return;
        }

        _history = new HistoryWindow(_cfg, SaveConfig, Theme, Backdrop)
        {
            Owner = this
        };
        _history.Closed += (_, _) => _history = null;
        _history.Show();
    }

    async Task FetchPlaytimeAsync()
    {
        var apiKey = ConfigService.Unprotect(_cfg.SteamApiKeyProtected);

        if (string.IsNullOrWhiteSpace(SteamId64))
        {
            var detected = SteamUserService.TryGetSteamId64FromLocalClient();
            if (!string.IsNullOrWhiteSpace(detected))
            {
                SteamId64 = detected;
                OnPropertyChanged(nameof(SteamId64));
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(SteamId64))
        {
            StatusText = "Missing SteamID64 and/or API key (use Detect, then Save key)";
            return;
        }

        StatusText = "Fetching Steam playtime...";
        try
        {
            var map = await SteamPlaytimeService.FetchPlaytimeHoursAsync(apiKey, SteamId64);
            foreach (var g in _games.Where(x => x.Platform == "steam"))
            {
                if (map.TryGetValue(g.Id, out var hrs))
                {
                    g.PlaytimeHours = hrs;
                    _cfg.SteamPlaytimeHoursByGameKey[g.Key] = hrs;
                }
            }
            SaveConfig();
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
        _cfg.IncludeGog = IncludeGog;
        _cfg.IncludeRiot = IncludeRiot;
        _cfg.IncludeAmazon = IncludeAmazon;
        _cfg.IncludeXbox = IncludeXbox;
        _cfg.IncludeUbisoft = IncludeUbisoft;
        _cfg.Theme = Theme;
        _cfg.Backdrop = Backdrop;
        ConfigService.Save(_cfg);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _playtime.Stop(commit: true);
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

    void EditTags_Click(object sender, RoutedEventArgs e)
    {
        var g = GamesGrid.SelectedItem as GameEntry;
        if (g is null) return;

        var known = GetKnownTags();
        var dlg = new TagEditorWindow(g.Name, known, g.Tags, Theme, Backdrop)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true)
        {
            TagService.SetTags(_cfg, g, dlg.Tags);
            SaveConfig();
            RefreshViews();
        }
    }

    void ClearTags_Click(object sender, RoutedEventArgs e)
    {
        var g = GamesGrid.SelectedItem as GameEntry;
        if (g is null) return;

        TagService.SetTags(_cfg, g, Array.Empty<string>());
        SaveConfig();
        RefreshViews();
    }

    void ChooseTagFilter_Click(object sender, RoutedEventArgs e)
    {
        var known = GetKnownTags();
        var dlg = new TagPickerWindow(known, _selectedTagFilter, TagFilterMatchAll, Theme, Backdrop)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true)
        {
            _selectedTagFilter = dlg.Tags.ToList();
            _tagFilterMatchAll = dlg.MatchAll;
            RefreshViews();
            OnPropertyChanged(nameof(SelectedTagFilterText));
            OnPropertyChanged(nameof(HasTagFilter));
            OnPropertyChanged(nameof(TagFilterMatchAll));
        }
    }

    void ClearTagFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTagFilter.Count == 0) return;
        _selectedTagFilter.Clear();
        RefreshViews();
        OnPropertyChanged(nameof(SelectedTagFilterText));
        OnPropertyChanged(nameof(HasTagFilter));
    }

    IReadOnlyList<string> GetKnownTags()
    {
        var all = new List<string>();

        foreach (var kv in _cfg.TagsByGameKey)
            all.AddRange(kv.Value ?? new List<string>());
        foreach (var kv in _cfg.AutoTagsByGameKey)
            all.AddRange(kv.Value ?? new List<string>());

        foreach (var g in _games)
            all.AddRange(g.AllTags);

        return TagService.NormalizeTags(string.Join(',', all));
    }

    async void ImportSteamTags_Click(object sender, RoutedEventArgs e)
    {
        if (_isImportingSteamTags) return;

        _isImportingSteamTags = true;
        OnPropertyChanged(nameof(CanImportSteamTags));

        try
        {
            var steam = _games.Where(g => g.Platform == "steam").ToList();
            if (steam.Count == 0)
            {
                StatusText = "No Steam games loaded";
                return;
            }

            StatusText = "Importing Steam tags...";

            var sem = new SemaphoreSlim(6);
            int done = 0;

            var tasks = steam.Select(async g =>
            {
                await sem.WaitAsync();
                try
                {
                    var tags = await SteamStoreTagService.FetchStoreTagsAndGenresAsync(g.Id);
                    if (tags.Count > 0)
                        TagService.SetAutoTags(_cfg, g, tags);
                }
                catch
                {
                    // ignore per-game errors
                }
                finally
                {
                    var d = Interlocked.Increment(ref done);
                    if (d % 10 == 0 || d == steam.Count)
                        Dispatcher.Invoke(() => StatusText = $"Importing Steam tags... {d}/{steam.Count}");
                    sem.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
            SaveConfig();
            RefreshViews();
            StatusText = "Steam tags imported";
        }
        finally
        {
            _isImportingSteamTags = false;
            OnPropertyChanged(nameof(CanImportSteamTags));
        }
    }

    void GamesGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null)
        {
            if (dep is System.Windows.Controls.DataGridRow row)
            {
                row.IsSelected = true;
                break;
            }
            dep = VisualTreeHelper.GetParent(dep);
        }
    }

    void DetectSteamId_Click(object sender, RoutedEventArgs e)
    {
        var detected = SteamUserService.TryGetSteamId64FromLocalClient();
        if (string.IsNullOrWhiteSpace(detected))
        {
            StatusText = "Could not detect SteamID64 (is Steam installed and logged in?)";
            return;
        }

        SteamId64 = detected;
        StatusText = "Detected SteamID64";
    }

    void GetApiKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://steamcommunity.com/dev/apikey") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
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
