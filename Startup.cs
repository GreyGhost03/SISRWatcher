using Microsoft.Win32;

namespace SISRWatcher;

/// <summary>"Start with Windows" for the current user. Per PC, not synced.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SISRWatcher";

    private static string Expected => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Expected);
        else
            key.DeleteValue(ValueName, false);
    }

    /// <summary>If you moved the app, the startup entry would point at the old spot. Fix it on launch.</summary>
    public static void RepairIfStale()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key?.GetValue(ValueName) is string current &&
                !string.Equals(current, Expected, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, Expected);
            }
        }
        catch
        {
            // Not critical.
        }
    }
}
