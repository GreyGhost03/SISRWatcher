using System.Text.Json;

namespace SISRWatcher;

/// <summary>One game the watcher looks for. Name is the process name (the exe name without ".exe").</summary>
public sealed class GameEntry
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Your game list and the SISR start delay. Lives in config.json next to SISRWatcher.exe,
/// so it's easy to back up, or to share between PCs with a sync tool.
/// </summary>
public sealed class SharedConfig
{
    /// <summary>How many seconds to wait after a game is detected before starting SISR.</summary>
    public int SisrStartDelaySeconds { get; set; } = 10;

    public List<GameEntry> Games { get; set; } = new();
}

/// <summary>Settings for this PC only. Lives in %APPDATA%\SISRWatcher\local.json.</summary>
public sealed class LocalConfig
{
    /// <summary>Where SISR.exe is on this PC.</summary>
    public string SisrPath { get; set; } = "";
}

public static class AppPaths
{
    public static readonly string AppDir =
        Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    public static readonly string SharedConfigFile = Path.Combine(AppDir, "config.json");

    public static readonly string LocalDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SISRWatcher");

    public static readonly string LocalConfigFile = Path.Combine(LocalDir, "local.json");
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private DateTime _sharedStamp = DateTime.MinValue;

    public SharedConfig Shared { get; private set; } = new();
    public LocalConfig Local { get; private set; } = new();

    /// <summary>Set if a config file was unreadable at startup and had to be set aside.</summary>
    public string? LoadWarning { get; private set; }

    /// <summary>Raised when the config changed from outside the current click (file edited elsewhere, SISR path changed).</summary>
    public event Action? Changed;

    public event Action<string>? SaveFailed;

    // ---------- loading and saving ----------

    public void Load()
    {
        Local = ReadJson<LocalConfig>(AppPaths.LocalConfigFile) ?? new LocalConfig();
        Shared = ReadJson<SharedConfig>(AppPaths.SharedConfigFile) ?? new SharedConfig();
        Shared.Games ??= new List<GameEntry>();
        Shared.Games.RemoveAll(g => string.IsNullOrWhiteSpace(g.Name));
        _sharedStamp = StampOf(AppPaths.SharedConfigFile);
    }

    /// <summary>
    /// Picks up edits made by another copy of the app (for example on a PC that syncs this folder). If the file looks half-written,
    /// we keep what we have and try again next time.
    /// </summary>
    public bool ReloadIfChanged()
    {
        var stamp = StampOf(AppPaths.SharedConfigFile);
        if (stamp == _sharedStamp) return false;

        try
        {
            var text = File.ReadAllText(AppPaths.SharedConfigFile);
            var cfg = JsonSerializer.Deserialize<SharedConfig>(text);
            if (cfg == null) return false;
            cfg.Games ??= new List<GameEntry>();
            cfg.Games.RemoveAll(g => string.IsNullOrWhiteSpace(g.Name));
            Shared = cfg;
            _sharedStamp = stamp;
        }
        catch
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    public void SaveShared()
    {
        if (WriteJson(AppPaths.SharedConfigFile, Shared))
            _sharedStamp = StampOf(AppPaths.SharedConfigFile);
    }

    public void SaveLocal() => WriteJson(AppPaths.LocalConfigFile, Local);

    private T? ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Keep a copy of the unreadable file instead of silently overwriting it.
            try { File.Copy(path, path + ".bad", true); } catch { }
            LoadWarning = $"{Path.GetFileName(path)} couldn't be read, so a copy was saved as {Path.GetFileName(path)}.bad and a fresh one will be used.";
            return null;
        }
    }

    private bool WriteJson<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Write to a ".part" file first (sync tools generally ignore those), then swap it in.
            var temp = path + ".part";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOpts));
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke($"Couldn't save {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private static DateTime StampOf(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    // ---------- games ----------

    public int AddGames(IEnumerable<string> names)
    {
        int added = 0;
        foreach (var raw in names)
        {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            if (Shared.Games.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
            Shared.Games.Add(new GameEntry { Name = name, Enabled = true });
            added++;
        }

        if (added > 0)
        {
            Shared.Games.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            SaveShared();
        }
        return added;
    }

    public void RemoveGame(string name)
    {
        Shared.Games.RemoveAll(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        SaveShared();
    }

    // ---------- SISR location ----------

    /// <summary>The saved SISR.exe location, or null if none is saved or the file no longer exists.</summary>
    public string? ResolveSisrPath()
    {
        var path = Local.SisrPath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    /// <summary>Remembers where SISR.exe is on this PC.</summary>
    public void SetSisrPath(string fullPath)
    {
        Local.SisrPath = fullPath;
        SaveLocal();
        Changed?.Invoke();
    }
}
