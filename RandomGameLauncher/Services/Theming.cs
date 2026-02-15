using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Media;

using MediaColor = System.Windows.Media.Color;

namespace RandomGameLauncher.Services;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public enum BackdropKind
{
    None,
    Mica,
    Acrylic
}

public static class ThemeManager
{
    static bool _initialized;
    static DispatcherTimer? _debounce;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce!.Stop();
            UpdateAccentResources();
        };

        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (System.Windows.Application.Current?.Dispatcher is null) return;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _debounce!.Stop();
                _debounce!.Start();
            });
        };

        UpdateAccentResources();
    }

    public static async Task ApplyAsync(Window window, FrameworkElement transitionHost, AppTheme requestedTheme, BackdropKind backdrop, bool animate)
    {
        Initialize();

        var effective = GetEffectiveTheme(requestedTheme);

        if (animate)
        {
            await ThemeTransition.FadeSwapAsync(transitionHost, () => SetThemeDictionary(effective));
        }
        else
        {
            SetThemeDictionary(effective);
        }

        UpdateAccentResources();
        UpdateBackdropResources(backdrop);
        WindowBackdrop.Apply(window, effective == AppTheme.Dark, backdrop);
    }

    static void UpdateBackdropResources(BackdropKind backdrop)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var baseObj = app.TryFindResource("App.WindowBackgroundColor");
        var baseColor = baseObj is MediaColor c ? c : MediaColor.FromArgb(0xCC, 0xF6, 0xF7, 0xF9);

        var alpha = backdrop switch
        {
            BackdropKind.None => baseColor.A,
            BackdropKind.Mica => (byte)0x66,
            BackdropKind.Acrylic => (byte)0x44,
            _ => baseColor.A
        };

        var tuned = MediaColor.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
        SetBrush(app, "App.WindowBackgroundBrush", tuned);
    }

    public static AppTheme GetEffectiveTheme(AppTheme requested)
    {
        if (requested != AppTheme.System) return requested;

        try
        {
            var v = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            var isLight = v is int i ? i != 0 : true;
            return isLight ? AppTheme.Light : AppTheme.Dark;
        }
        catch
        {
            return AppTheme.Light;
        }
    }

    static void SetThemeDictionary(AppTheme effective)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var wanted = effective == AppTheme.Dark ? "Themes/Theme.Dark.xaml" : "Themes/Theme.Light.xaml";

        var dicts = app.Resources.MergedDictionaries;
        ResourceDictionary? existing = null;

        foreach (var d in dicts)
        {
            var src = d.Source?.ToString() ?? "";
            if (src.EndsWith("Themes/Theme.Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                src.EndsWith("Themes/Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                existing = d;
                break;
            }
        }

        var next = new ResourceDictionary { Source = new Uri(wanted, UriKind.Relative) };

        if (existing is null) dicts.Add(next);
        else
        {
            var idx = dicts.IndexOf(existing);
            dicts.RemoveAt(idx);
            dicts.Insert(idx, next);
        }
    }

    static void UpdateAccentResources()
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var accent = WindowsAccent.TryGetColor() ?? SystemParameters.WindowGlassColor;
        var strong = Darken(accent, 0.15);

        SetBrush(app, "App.AccentBrush", accent);
        SetBrush(app, "App.AccentStrongBrush", strong);
        SetBrush(app, "App.AccentSoftBrush", MediaColor.FromArgb(0x33, accent.R, accent.G, accent.B));
    }

    static void SetBrush(System.Windows.Application app, string key, MediaColor color)
    {
        if (app.Resources[key] is SolidColorBrush b)
        {
            if (b.IsFrozen)
            {
                app.Resources[key] = new SolidColorBrush(color);
                return;
            }

            b.Color = color;
            return;
        }

        app.Resources[key] = new SolidColorBrush(color);
    }

    static MediaColor Darken(MediaColor c, double amount)
    {
        byte D(byte v) => (byte)Math.Clamp((int)(v * (1.0 - amount)), 0, 255);
        return MediaColor.FromArgb(c.A, D(c.R), D(c.G), D(c.B));
    }
}

static class ThemeTransition
{
    public static Task FadeSwapAsync(FrameworkElement host, Action apply)
    {
        if (host is null) { apply(); return Task.CompletedTask; }

        var overlay = FindOverlayImage(host);
        if (overlay is null) { apply(); return Task.CompletedTask; }

        var bmp = Capture(host);
        if (bmp is null) { apply(); return Task.CompletedTask; }

        overlay.Source = bmp;
        overlay.Visibility = Visibility.Visible;
        overlay.Opacity = 1;

        apply();

        var tcs = new TaskCompletionSource<bool>();
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = GetDuration("App.Anim.Medium", TimeSpan.FromMilliseconds(220)),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        anim.Completed += (_, _) =>
        {
            overlay.Visibility = Visibility.Collapsed;
            overlay.Source = null;
            tcs.TrySetResult(true);
        };
        overlay.BeginAnimation(UIElement.OpacityProperty, anim);
        return tcs.Task;
    }

    static Duration GetDuration(string key, TimeSpan fallback)
    {
        try
        {
            var v = System.Windows.Application.Current?.Resources[key];
            if (v is Duration d) return d;
        }
        catch { }
        return new Duration(fallback);
    }

    static System.Windows.Controls.Image? FindOverlayImage(FrameworkElement host)
    {
        if (host is FrameworkElement fe)
        {
            var w = Window.GetWindow(fe);
            if (w is not null)
            {
                return w.FindName("ThemeTransitionOverlay") as System.Windows.Controls.Image;
            }
        }
        return null;
    }

    static BitmapSource? Capture(FrameworkElement element)
    {
        var w = (int)Math.Max(1, element.ActualWidth);
        var h = (int)Math.Max(1, element.ActualHeight);
        if (w <= 1 || h <= 1) return null;

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);
        rtb.Freeze();
        return rtb;
    }
}

static class WindowsAccent
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

    public static MediaColor? TryGetColor()
    {
        try
        {
            if (DwmGetColorizationColor(out var c, out _) != 0) return null;
            var a = (byte)((c >> 24) & 0xFF);
            var r = (byte)((c >> 16) & 0xFF);
            var g = (byte)((c >> 8) & 0xFF);
            var b = (byte)(c & 0xFF);

            // Some systems report low alpha; treat as fully opaque accent.
            if (a < 0x80) a = 0xFF;

            return MediaColor.FromArgb(a, r, g, b);
        }
        catch
        {
            return null;
        }
    }
}

static class WindowBackdrop
{
    // Windows 11+ only. Safe no-op on older versions.
    enum DwmWindowAttribute
    {
        DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
        DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19,
        DWMWA_SYSTEMBACKDROP_TYPE = 38
    }

    enum DwmSystemBackdropType
    {
        Auto = 0,
        None = 1,
        Mica = 2,
        Acrylic = 3,
        Tabbed = 4
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, DwmWindowAttribute attr, ref int attrValue, int attrSize);

    public static void Apply(Window window, bool isDark, BackdropKind backdrop)
    {
        if (window is null) return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        TrySetDarkMode(hwnd, isDark);

        var type = backdrop switch
        {
            BackdropKind.Mica => (int)DwmSystemBackdropType.Mica,
            BackdropKind.Acrylic => (int)DwmSystemBackdropType.Acrylic,
            _ => (int)DwmSystemBackdropType.None
        };

        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttribute.DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
    }

    static void TrySetDarkMode(IntPtr hwnd, bool isDark)
    {
        var v = isDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(hwnd, DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref v, sizeof(int));
    }
}
