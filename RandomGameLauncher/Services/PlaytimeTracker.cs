using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using RandomGameLauncher.Models;

namespace RandomGameLauncher.Services;

public sealed class PlaytimeTracker
{
    readonly Config _cfg;
    readonly DispatcherTimer _timer;
    readonly Action _save;
    readonly Action<string> _status;

    GameEntry? _current;
    Guid _historyId;
    DateTime _startedUtc;
    DateTime _lastSeenUtc;
    bool _confirmed;

    static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);
    static readonly TimeSpan StopAfterGone = TimeSpan.FromSeconds(20);
    static readonly TimeSpan GiveUpIfNeverSeen = TimeSpan.FromMinutes(2);

    public event Action<Guid, long>? SessionCommitted;

    public PlaytimeTracker(Config cfg, Action saveConfig, Action<string> setStatus)
    {
        _cfg = cfg;
        _save = saveConfig;
        _status = setStatus;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Tick
        };
        _timer.Tick += (_, _) => OnTick();
    }

    public void SeedFromConfig(GameEntry g)
    {
        if (_cfg.TrackedPlaytimeSeconds.TryGetValue(g.Key, out var sec) && sec > 0)
            g.TrackedPlaytimeHours = Math.Round(sec / 3600.0, 1);
    }

    public void Start(GameEntry g, Guid historyId)
    {
        Stop(commit: false);

        _current = g;
        _historyId = historyId;
        _startedUtc = DateTime.UtcNow;
        _lastSeenUtc = _startedUtc;
        _confirmed = false;
        _timer.Start();
    }

    public void Stop(bool commit = true)
    {
        if (_current is null) return;

        var g = _current;
        var started = _startedUtc;

        _current = null;
        _timer.Stop();

        var historyId = _historyId;
        if (!commit) return;

        var elapsed = DateTime.UtcNow - started;
        if (elapsed.TotalSeconds < 5) return;

        var add = (long)Math.Round(elapsed.TotalSeconds);
        if (add <= 0) return;

        _cfg.TrackedPlaytimeSeconds.TryGetValue(g.Key, out var existing);
        _cfg.TrackedPlaytimeSeconds[g.Key] = Math.Max(0, existing + add);
        _save();

        g.TrackedPlaytimeHours = Math.Round(_cfg.TrackedPlaytimeSeconds[g.Key] / 3600.0, 1);
        SessionCommitted?.Invoke(historyId, add);
    }

    void OnTick()
    {
        if (_current is null) { _timer.Stop(); return; }

        var now = DateTime.UtcNow;
        var g = _current;

        var install = g.InstallPath ?? "";
        if (string.IsNullOrWhiteSpace(install) || !Directory.Exists(install))
        {
            if (!_confirmed && now - _startedUtc > GiveUpIfNeverSeen)
            {
                _status("Playtime tracking: install path missing; stopped");
                Stop(commit: false);
            }
            return;
        }

        if (IsAnyProcessUnderPath(install))
        {
            _lastSeenUtc = now;
            if (!_confirmed)
            {
                _confirmed = true;
                _status($"Playtime tracking started: {g.Name}");
            }
            return;
        }

        if (!_confirmed)
        {
            if (now - _startedUtc > GiveUpIfNeverSeen)
            {
                _status("Playtime tracking: game process not detected; stopped");
                Stop(commit: false);
            }
            return;
        }

        if (now - _lastSeenUtc > StopAfterGone)
        {
            _status($"Playtime tracked: {g.Name}");
            Stop(commit: true);
        }
    }

    static bool IsAnyProcessUnderPath(string installPath)
    {
        var root = EnsureTrailingSeparator(Path.GetFullPath(installPath));

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var file = p.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(file)) continue;
                var full = Path.GetFullPath(file);

                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // Access denied / 32-64 bit mismatch / protected process; ignore.
            }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }

        return false;
    }

    static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var c = path[^1];
        if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar) return path;
        return path + Path.DirectorySeparatorChar;
    }
}
