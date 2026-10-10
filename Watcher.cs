using System.Diagnostics;

namespace SISRWatcher;

public enum WatchState { Idle, Detected, Running, Paused, SisrMissing }

public sealed record StatusInfo(WatchState State, string Title, string Detail);

/// <summary>
/// The brain of the app. Every couple of seconds it checks whether one of your enabled games is running,
/// starts SISR (after the SISR start delay) and closes it again when the game exits.
/// It only closes a SISR it started itself.
/// </summary>
public sealed class Watcher : IDisposable
{
    private readonly ConfigStore _cfg;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly List<string> _log = new();

    private DateTime? _pendingSince;   // when we first noticed a game with no SISR running
    private bool _startedByUs;         // true while the SISR process was launched by this app
    private bool _suppressed;          // true after SISR was stopped/closed on purpose: don't restart until the game closes

    public bool Paused { get; private set; }
    public StatusInfo Status { get; private set; } = new(WatchState.Idle, "Starting up", "");
    public IReadOnlyList<string> LogLines => _log;

    public event Action<StatusInfo>? StatusChanged;
    public event Action<string>? Logged;

    public Watcher(ConfigStore cfg)
    {
        _cfg = cfg;
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        AddLog("Watcher started");
        _timer.Start();
        Tick();
    }

    public void RefreshNow() => Tick();

    public void SetPaused(bool paused)
    {
        if (Paused == paused) return;
        Paused = paused;
        AddLog(paused ? "Paused watching" : "Resumed watching");
        Tick();
    }

    /// <summary>Closes SISR now. It won't be started again until the current game closes.</summary>
    public void StopSisrNow()
    {
        var name = SisrProcessName();
        if (KillSisr(name))
            AddLog("SISR stopped from the app");
        _startedByUs = false;
        _suppressed = true;
        Tick();
    }

    public void AddLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        _log.Add(line);
        if (_log.Count > 300) _log.RemoveRange(0, _log.Count - 300);
        Logged?.Invoke(line);
    }

    // ---------- the main loop ----------

    private void Tick()
    {
        _cfg.ReloadIfChanged();

        if (Paused)
        {
            Set(WatchState.Paused, "Paused", "Games aren't being watched. Resume from the tray menu.");
            return;
        }

        var running = RunningNames();
        var enabled = _cfg.Shared.Games.Where(g => g.Enabled).Select(g => g.Name).ToList();
        var game = enabled.FirstOrDefault(n => running.Contains(n));

        var sisrPath = _cfg.ResolveSisrPath();
        var sisrName = SisrProcessName(sisrPath);
        bool sisrUp = running.Contains(sisrName);

        if (game != null)
        {
            if (sisrUp)
            {
                _pendingSince = null;
                if (_startedByUs)
                {
                    Set(WatchState.Running, "SISR running", $"Started for {game}, closes when the game exits");
                }
                else
                {
                    Set(WatchState.Running, "SISR running",
                        $"It was already open when {game} started, so the watcher will leave it alone");
                }
                return;
            }

            // The game is running but SISR isn't.
            if (_startedByUs)
            {
                // We started it, and it's gone: someone closed it. Respect that until the game exits.
                _startedByUs = false;
                _suppressed = true;
                AddLog("SISR was closed while the game was running; it won't be restarted until the game exits");
            }

            if (_suppressed)
            {
                Set(WatchState.Detected, "SISR stopped", $"It won't restart until {game} closes");
                return;
            }

            if (sisrPath == null)
            {
                Set(WatchState.SisrMissing, "SISR not found",
                    $"{game} is running, but SISR.exe wasn't found. Open Settings to set its location.");
                return;
            }

            _pendingSince ??= DateTime.UtcNow;
            var delay = Math.Max(0, _cfg.Shared.SisrStartDelaySeconds);
            var remaining = delay - (DateTime.UtcNow - _pendingSince.Value).TotalSeconds;

            if (remaining > 0)
            {
                Set(WatchState.Detected, $"{game} detected", $"Starting SISR in {Math.Ceiling(remaining)} s");
            }
            else
            {
                StartSisr(sisrPath, game);
            }
            return;
        }

        // No watched game is running.
        _pendingSince = null;
        _suppressed = false;

        if (_startedByUs)
        {
            if (sisrUp && KillSisr(sisrName))
                AddLog("Game closed, so SISR was closed too");
            _startedByUs = false;
        }

        if (enabled.Count == 0)
            Set(WatchState.Idle, "Nothing to watch yet", "Click Add game to get started");
        else
            Set(WatchState.Idle, "Watching for games",
                enabled.Count == 1 ? "1 game enabled" : $"{enabled.Count} games enabled");
    }

    private void StartSisr(string path, string game)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path)!,
                UseShellExecute = true
            })?.Dispose();

            _startedByUs = true;
            _pendingSince = null;
            AddLog($"{game} detected, SISR started");
            Set(WatchState.Running, "SISR running", $"Started for {game}, closes when the game exits");
        }
        catch (Exception ex)
        {
            // Don't retry every two seconds.
            _suppressed = true;
            AddLog($"Couldn't start SISR: {ex.Message}");
            Set(WatchState.SisrMissing, "Couldn't start SISR", ex.Message);
        }
    }

    // ---------- helpers ----------

    private string SisrProcessName(string? resolvedPath = null)
    {
        resolvedPath ??= _cfg.ResolveSisrPath();
        return resolvedPath != null ? Path.GetFileNameWithoutExtension(resolvedPath) : "SISR";
    }

    private static HashSet<string> RunningNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { names.Add(p.ProcessName); }
            catch { /* process ended while we looked */ }
            finally { p.Dispose(); }
        }
        return names;
    }

    private bool KillSisr(string name)
    {
        bool killedAny = false;
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                p.Kill(true);
                killedAny = true;
            }
            catch (Exception ex)
            {
                AddLog($"Couldn't close SISR: {ex.Message}");
            }
            finally
            {
                p.Dispose();
            }
        }
        return killedAny;
    }

    private void Set(WatchState state, string title, string detail)
    {
        var next = new StatusInfo(state, title, detail);
        if (next == Status) return;
        Status = next;
        StatusChanged?.Invoke(next);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        // If we opened SISR, don't leave it behind when the app quits.
        if (_startedByUs) KillSisr(SisrProcessName());
    }
}
