using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SISRWatcher;

/// <summary>
/// Owns the tray icon, the watcher and the (hidden until needed) main window.
/// There is no window at startup: the app just lives in the system tray.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly ConfigStore _cfg = new();
    private readonly Watcher _watcher;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly Control _ui = new();   // invisible helper so other threads can hop onto the UI thread
    private readonly Dictionary<WatchState, Icon> _icons;
    private readonly Icon _appIcon = MakeIcon(Color.FromArgb(24, 95, 165));   // blue, for the window and taskbar
    private MainForm? _form;

    public TrayApp()
    {
        _ = _ui.Handle;   // forces the handle to exist so BeginInvoke works

        _cfg.Load();
        StartupRegistration.RepairIfStale();

        _icons = new Dictionary<WatchState, Icon>
        {
            [WatchState.Idle] = MakeIcon(Color.FromArgb(110, 110, 110)),
            [WatchState.Detected] = MakeIcon(Color.FromArgb(214, 150, 20)),
            [WatchState.Running] = MakeIcon(Color.FromArgb(40, 160, 70)),
            [WatchState.Paused] = MakeIcon(Color.FromArgb(170, 170, 170)),
            [WatchState.SisrMissing] = MakeIcon(Color.FromArgb(200, 60, 60)),
        };

        _watcher = new Watcher(_cfg);

        _pauseItem = new ToolStripMenuItem("Pause watching", null, (_, _) => TogglePause());
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open SISR watcher", null, (_, _) => ShowWindow())
        {
            Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold)
        });
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripMenuItem("Stop SISR now", null, (_, _) => _watcher.StopSisrNow()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Quit()));

        _tray = new NotifyIcon
        {
            Icon = _icons[WatchState.Idle],
            Text = "SISR watcher",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowWindow();
        };

        _watcher.StatusChanged += OnStatusChanged;
        _watcher.Start();

        if (_cfg.LoadWarning != null)
            _tray.ShowBalloonTip(6000, "SISR watcher", _cfg.LoadWarning, ToolTipIcon.Warning);

        bool sisrFound = _cfg.ResolveSisrPath() != null;
        if (!sisrFound) AutoFindSisr();

        // First run, or something needs attention: show the window right away.
        if (_cfg.Shared.Games.Count == 0 || !sisrFound) ShowWindow();
    }

    /// <summary>Called from another thread (a second launch of the app) to bring the window up.</summary>
    public void RequestShow()
    {
        if (_ui.IsDisposed || !_ui.IsHandleCreated) return;
        _ui.BeginInvoke((Action)ShowWindow);
    }

    public void ShowWindow()
    {
        _form ??= new MainForm(_cfg, _watcher, AutoFindSisr, _appIcon);
        if (!_form.Visible) _form.Show();
        if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    /// <summary>Searches near the app's folder for SISR.exe without freezing the UI.</summary>
    public void AutoFindSisr(Action<bool>? done = null)
    {
        Task.Run(() => SisrLocator.FindNear(AppPaths.AppDir)).ContinueWith(t =>
        {
            string? found = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
            if (_ui.IsDisposed || !_ui.IsHandleCreated) return;
            _ui.BeginInvoke((Action)(() =>
            {
                if (found != null)
                {
                    _cfg.SetSisrPath(found);
                    _watcher.AddLog("Found SISR.exe automatically");
                    _watcher.RefreshNow();
                }
                done?.Invoke(found != null);
            }));
        });
    }

    private void TogglePause()
    {
        _watcher.SetPaused(!_watcher.Paused);
        _pauseItem.Checked = _watcher.Paused;
    }

    private void OnStatusChanged(StatusInfo status)
    {
        _tray.Icon = _icons[status.State];
        var text = $"SISR watcher: {status.Title}";
        _tray.Text = text.Length > 63 ? text[..63] : text;
    }

    private void Quit()
    {
        _tray.Visible = false;
        _watcher.Dispose();
        _tray.Dispose();
        _form?.Dispose();
        _ui.Dispose();
        ExitThread();
    }

    // ---------- tray icon drawing (no image files needed) ----------

    // The icon is an eye (the "watcher") whose pupil is a d-pad cross, on a rounded square
    // in the given color. It's drawn in a 100 x 100 space and scaled down.
    private static Icon MakeIcon(Color color)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.ScaleTransform(size / 100f, size / 100f);

            using var colorBrush = new SolidBrush(color);

            using (var tile = RoundedRect(0, 0, 100, 100, 22))
                g.FillPath(colorBrush, tile);

            // The eye: an almond shape made of two curves.
            using (var eye = new GraphicsPath())
            {
                eye.AddBezier(8, 50, 36, 26, 64, 26, 92, 50);
                eye.AddBezier(92, 50, 64, 74, 36, 74, 8, 50);
                eye.CloseFigure();
                g.FillPath(Brushes.White, eye);
            }

            // The iris, in the tile color, with the d-pad cross as the pupil.
            g.FillEllipse(colorBrush, 30, 30, 40, 40);
            using (var vertical = RoundedRect(45, 37, 10, 26, 3))
                g.FillPath(Brushes.White, vertical);
            using (var horizontal = RoundedRect(37, 45, 26, 10, 3))
                g.FillPath(Brushes.White, horizontal);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);
}
