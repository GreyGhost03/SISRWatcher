namespace SISRWatcher;

internal static class Program
{
    // Names used so a second launch can tell the first one "show your window".
    private const string MutexName = @"Local\SISRWatcher.SingleInstance";
    private const string ShowEventName = @"Local\SISRWatcher.Show";

    [STAThread]
    private static void Main()
    {
        // Only one copy of the watcher should ever run.
        using var mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch
            {
                // The first instance isn't ready yet; nothing to do.
            }
            return;
        }

        ApplicationConfiguration.Initialize();

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var app = new TrayApp();
        var registration = ThreadPool.RegisterWaitForSingleObject(
            showEvent,
            (_, _) => app.RequestShow(),
            null,
            Timeout.Infinite,
            false);

        Application.Run(app);

        registration.Unregister(null);
    }
}
