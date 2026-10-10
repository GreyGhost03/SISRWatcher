using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SISRWatcher;

/// <summary>A game (or program) the user could add. Name is the process name. Group is an optional list heading.</summary>
public sealed record Candidate(string Name, string Detail, string? Path, string Group = "");

public static class GameDiscovery
{
    // Processes that are never games. They're hidden from the "Running now" list.
    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost",
        "SearchHost", "StartMenuExperienceHost", "LockApp", "msedge", "chrome", "firefox",
        "SISR", "SISRWatcher", "steam", "steamwebhelper", "Code",
        "WindowsTerminal", "cmd", "powershell", "pwsh", "Taskmgr", "notepad", "Discord", "Spotify",
        "XboxPcApp", "GameBar", "GameBarFTServer", "devenv"
    };

    // Names containing these are usually helpers, not the game itself.
    private static readonly string[] HelperHints =
    {
        "gamelaunchhelper", "easyanticheat", "unitycrashhandler", "crashpad", "crashreport",
        "vc_redist", "vcredist", "setup", "install", "uninstall", "dxsetup", "redist",
        "beservice", "cefsubprocess", "dotnet"
    };

    public const string LikelyGamesGroup = "Likely games";
    public const string WindowGroup = "Programs with a window";
    public const string BackgroundGroup = "Background processes";

    // If a program runs from a folder like these, it's probably a game.
    private static readonly string[] GameFolderHints =
    {
        @"\XboxGames\", @"\steamapps\common\", @"\Epic Games\", @"\GOG Games\", @"\GOG Galaxy\Games\", @"\Games\"
    };

    /// <summary>
    /// Everything running right now, sorted into three groups so nothing is hidden:
    /// likely games (they run from a games folder), programs that have a window, and background processes.
    /// Many games, especially Microsoft Store ones, don't expose a normal window to Windows, so they land
    /// in the background group; the path-based group catches those.
    /// </summary>
    public static List<Candidate> RunningProcesses()
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var selfId = Environment.ProcessId;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Candidate>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == selfId || p.Id == 0) continue;

                var name = p.ProcessName;
                if (IgnoredProcesses.Contains(name) || seen.Contains(name)) continue;

                bool hasWindow = p.MainWindowHandle != IntPtr.Zero &&
                                 !string.IsNullOrWhiteSpace(p.MainWindowTitle);

                // The limited-rights lookup works for many processes that Process.MainModule can't read.
                string? path = NativeProcess.GetPath(p.Id);
                if (path == null)
                {
                    try { path = p.MainModule?.FileName; } catch { /* protected process */ }
                }

                if (path != null && path.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase)) continue;

                string group;
                string detail;
                if (path != null && GameFolderHints.Any(h => path.Contains(h, StringComparison.OrdinalIgnoreCase)))
                {
                    group = LikelyGamesGroup;
                    detail = path;
                }
                else if (hasWindow)
                {
                    group = WindowGroup;
                    detail = p.MainWindowTitle;
                }
                else
                {
                    group = BackgroundGroup;
                    detail = path ?? "Path unavailable";
                }

                seen.Add(name);
                result.Add(new Candidate(name, detail, path, group));
            }
            catch
            {
                // The process ended while we were looking at it.
            }
            finally
            {
                p.Dispose();
            }
        }

        result.Sort((a, b) =>
        {
            int byGroup = GroupRank(a.Group).CompareTo(GroupRank(b.Group));
            return byGroup != 0 ? byGroup : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    private static int GroupRank(string group) => group switch
    {
        LikelyGamesGroup => 0,
        WindowGroup => 1,
        _ => 2
    };

    /// <summary>Looks for game executables in an XboxGames folder on every fixed drive.</summary>
    public static List<Candidate> ScanXboxGames()
    {
        var results = new List<Candidate>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 3,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                var xboxRoot = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
                if (!Directory.Exists(xboxRoot)) continue;

                foreach (var gameDir in Directory.GetDirectories(xboxRoot))
                {
                    var content = Path.Combine(gameDir, "Content");
                    var searchRoot = Directory.Exists(content) ? content : gameDir;
                    var title = Path.GetFileName(gameDir);

                    try
                    {
                        foreach (var exe in Directory.EnumerateFiles(searchRoot, "*.exe", options))
                        {
                            var name = Path.GetFileNameWithoutExtension(exe);
                            if (LooksLikeHelper(name)) continue;
                            var relative = Path.GetRelativePath(searchRoot, exe);
                            results.Add(new Candidate(name, $"{title}  -  {relative}", exe));
                        }
                    }
                    catch
                    {
                        // Skip folders we can't read.
                    }
                }
            }
            catch
            {
                // Skip drives we can't read.
            }
        }

        results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    private static bool LooksLikeHelper(string name)
    {
        foreach (var hint in HelperHints)
            if (name.Contains(hint, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

internal static class NativeProcess
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    /// <summary>The full path of a running process's exe, or null if Windows won't tell us.</summary>
    public static string? GetPath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;

        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}

public static class SisrLocator
{
    /// <summary>
    /// Looks for SISR.exe in the app's folder and a few folders above it (searching a few levels down in each).
    /// That covers the common layout where the app and SISR sit in the same parent folder.
    /// </summary>
    public static string? FindNear(string startDir)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 3,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        var dir = new DirectoryInfo(startDir);
        for (int level = 0; dir != null && level <= 4; level++, dir = dir.Parent)
        {
            try
            {
                var hit = Directory.EnumerateFiles(dir.FullName, "SISR.exe", options).FirstOrDefault();
                if (hit != null) return hit;
            }
            catch
            {
                // Keep looking one level up.
            }
        }
        return null;
    }
}
