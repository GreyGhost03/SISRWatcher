namespace SISRWatcher;

/// <summary>
/// The window you open from the tray: status, game list, and (behind the Settings button) the SISR settings.
/// Closing it only hides it; the watcher keeps running in the tray.
/// </summary>
public sealed class MainForm : Form
{
    private readonly ConfigStore _cfg;
    private readonly Watcher _watcher;
    private readonly Action<Action<bool>?> _autoFind;

    private readonly Button _settingsBtn = new() { Text = "Settings", AutoSize = true };
    private readonly Button _logBtn = new() { Text = "Activity log", AutoSize = true };

    private readonly TableLayoutPanel _statusPanel = new();
    private readonly Label _statusTitle = new();
    private readonly Label _statusDetail = new();
    private readonly Button _stopBtn = new() { Text = "Stop SISR now", AutoSize = true };

    private readonly TableLayoutPanel _settingsPanel = new();
    private readonly TextBox _sisrBox = new();
    private readonly Button _browseBtn = new() { Text = "Browse", AutoSize = true };
    private readonly Button _findBtn = new() { Text = "Auto-detect", AutoSize = true };
    private readonly Label _sisrMsg = new();
    private readonly NumericUpDown _delay = new();
    private readonly CheckBox _startup = new();

    private readonly Label _count = new();
    private readonly Button _addBtn = new() { Text = "Add game", AutoSize = true };
    private readonly Button _removeBtn = new() { Text = "Remove selected", AutoSize = true };
    private readonly ListView _list = new();
    private readonly TextBox _logBox = new();

    private bool _loading;

    public MainForm(ConfigStore cfg, Watcher watcher, Action<Action<bool>?> autoFind, Icon icon)
    {
        _cfg = cfg;
        _watcher = watcher;
        _autoFind = autoFind;

        Text = "SISR watcher";
        Icon = icon;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 640);
        MinimumSize = new Size(500, 520);

        BuildLayout();
        WireEvents();

        RefreshSettings();
        PopulateList();
        ApplyStatus(_watcher.Status);
        foreach (var line in _watcher.LogLines) _logBox.AppendText(line + Environment.NewLine);
    }

    // ---------- layout ----------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++)
            root.RowStyles.Add(i == 5 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));

        // Row 0: title and the Settings / Activity log buttons
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(12, 8, 12, 8)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label
        {
            Text = "SISR watcher", AutoSize = true, Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold)
        }, 0, 0);
        var topButtons = new FlowLayoutPanel
        {
            AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = Padding.Empty
        };
        topButtons.Controls.Add(_logBtn);
        topButtons.Controls.Add(_settingsBtn);
        top.Controls.Add(topButtons, 1, 0);
        root.Controls.Add(top, 0, 0);

        // Row 1: settings (hidden until the Settings button is clicked)
        BuildSettingsPanel();
        root.Controls.Add(_settingsPanel, 0, 1);

        // Row 2: status banner
        _statusPanel.Dock = DockStyle.Fill;
        _statusPanel.AutoSize = true;
        _statusPanel.ColumnCount = 2;
        _statusPanel.RowCount = 2;
        _statusPanel.Padding = new Padding(12, 10, 12, 10);
        _statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        _statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        _statusTitle.Dock = DockStyle.Fill;
        _statusTitle.AutoEllipsis = true;
        _statusTitle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _statusTitle.TextAlign = ContentAlignment.MiddleLeft;
        _statusDetail.Dock = DockStyle.Fill;
        _statusDetail.AutoEllipsis = true;
        _statusDetail.TextAlign = ContentAlignment.MiddleLeft;
        _stopBtn.Anchor = AnchorStyles.Right;
        _statusPanel.Controls.Add(_statusTitle, 0, 0);
        _statusPanel.Controls.Add(_statusDetail, 0, 1);
        _statusPanel.Controls.Add(_stopBtn, 1, 0);
        _statusPanel.SetRowSpan(_stopBtn, 2);
        root.Controls.Add(_statusPanel, 0, 2);

        // Row 3: "Your games" header with Add / Remove
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(12, 12, 12, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _count.AutoSize = true;
        _count.Anchor = AnchorStyles.Left;
        _count.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        header.Controls.Add(_count, 0, 0);
        var headerButtons = new FlowLayoutPanel
        {
            AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = Padding.Empty
        };
        headerButtons.Controls.Add(_removeBtn);
        headerButtons.Controls.Add(_addBtn);
        header.Controls.Add(headerButtons, 1, 0);
        root.Controls.Add(header, 0, 3);

        // Row 4: hint
        root.Controls.Add(new Label
        {
            Text = "Ticked games are watched. Untick one to pause it without removing it.",
            AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(12, 0, 12, 6)
        }, 0, 4);

        // Row 5: game list
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.Margin = new Padding(12, 0, 12, 12);
        _list.Columns.Add("Process name", 330);
        _list.Columns.Add("State", 110);
        root.Controls.Add(_list, 0, 5);

        // Row 6: activity log (hidden until the Activity log button is clicked)
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Font = new Font("Consolas", 9f);
        _logBox.Height = 130;
        _logBox.Dock = DockStyle.Fill;
        _logBox.Margin = new Padding(12, 0, 12, 12);
        _logBox.Visible = false;
        root.Controls.Add(_logBox, 0, 6);

        Controls.Add(root);
    }

    private void BuildSettingsPanel()
    {
        _settingsPanel.Dock = DockStyle.Fill;
        _settingsPanel.AutoSize = true;
        _settingsPanel.ColumnCount = 3;
        _settingsPanel.RowCount = 4;
        _settingsPanel.Padding = new Padding(12);
        _settingsPanel.BackColor = Color.FromArgb(245, 245, 245);
        _settingsPanel.Visible = false;
        _settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int i = 0; i < 4; i++) _settingsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // SISR location
        _settingsPanel.Controls.Add(MakeLabel("SISR location"), 0, 0);
        _sisrBox.PlaceholderText = "Path to SISR.exe";
        _sisrBox.Dock = DockStyle.Fill;
        _sisrBox.Margin = new Padding(0, 4, 8, 4);
        _settingsPanel.Controls.Add(_sisrBox, 1, 0);
        _browseBtn.Anchor = AnchorStyles.Right;
        _settingsPanel.Controls.Add(_browseBtn, 2, 0);

        // SISR start delay
        _settingsPanel.Controls.Add(MakeLabel("SISR start delay"), 0, 1);
        var delayRow = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty
        };
        _delay.Minimum = 0;
        _delay.Maximum = 600;
        _delay.Width = 70;
        _delay.Margin = new Padding(0, 4, 8, 4);
        delayRow.Controls.Add(_delay);
        delayRow.Controls.Add(MakeLabel("seconds after a game is detected"));
        _settingsPanel.Controls.Add(delayRow, 1, 1);
        _settingsPanel.SetColumnSpan(delayRow, 2);

        // Start with Windows
        _startup.Text = "Start with Windows";
        _startup.AutoSize = true;
        _startup.Margin = new Padding(0, 6, 0, 6);
        _settingsPanel.Controls.Add(_startup, 0, 2);
        _settingsPanel.SetColumnSpan(_startup, 3);

        // Message about where SISR was found, plus the auto-find button
        _sisrMsg.AutoSize = true;
        _sisrMsg.ForeColor = SystemColors.GrayText;
        _sisrMsg.Anchor = AnchorStyles.Left;
        _sisrMsg.MaximumSize = new Size(380, 0);
        _settingsPanel.Controls.Add(_sisrMsg, 0, 3);
        _settingsPanel.SetColumnSpan(_sisrMsg, 2);
        _findBtn.Anchor = AnchorStyles.Right;
        _settingsPanel.Controls.Add(_findBtn, 2, 3);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6)
    };

    // ---------- events ----------

    private void WireEvents()
    {
        _settingsBtn.Click += (_, _) =>
        {
            _settingsPanel.Visible = !_settingsPanel.Visible;
            if (_settingsPanel.Visible) RefreshSettings();
        };

        _logBtn.Click += (_, _) =>
        {
            _logBox.Visible = !_logBox.Visible;
            if (_logBox.Visible)
            {
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            }
        };

        _addBtn.Click += (_, _) => OpenAddDialog();
        _removeBtn.Click += (_, _) => RemoveSelected();
        _stopBtn.Click += (_, _) => _watcher.StopSisrNow();

        _list.ItemChecked += OnItemChecked;
        _list.Resize += (_, _) => AdjustColumns();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelected();
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                // Ctrl+C copies the highlighted process names, one per line.
                ClipboardHelper.CopyLines(_list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text));
                e.SuppressKeyPress = true;
            }
        };

        // The SISR location can be typed or pasted. It's applied on Enter or when you click away.
        _sisrBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ApplySisrText();
            }
        };
        _sisrBox.Leave += (_, _) => ApplySisrText();

        _browseBtn.Click += (_, _) => BrowseForSisr();
        _findBtn.Click += (_, _) =>
        {
            _sisrMsg.Text = "Looking near this app's folder...";
            _autoFind(found =>
            {
                if (!found) _sisrMsg.Text = "Couldn't find SISR.exe near this app. Click Browse instead.";
            });
        };

        _delay.ValueChanged += (_, _) => SaveDelay();
        _delay.Leave += (_, _) => SaveDelay();
        _startup.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            try
            {
                StartupRegistration.Set(_startup.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Couldn't change the startup setting: {ex.Message}", "SISR watcher");
            }
        };

        _watcher.StatusChanged += ApplyStatus;
        _watcher.Logged += line =>
        {
            _logBox.AppendText(line + Environment.NewLine);
        };
        _cfg.Changed += RefreshAll;
        _cfg.SaveFailed += message => MessageBox.Show(this, message, "SISR watcher");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The X button hides the window; use Quit in the tray menu to exit.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    // ---------- showing state ----------

    private void ApplyStatus(StatusInfo s)
    {
        _statusTitle.Text = s.Title;
        _statusDetail.Text = s.Detail;
        _statusPanel.BackColor = s.State switch
        {
            WatchState.Running => Color.FromArgb(219, 242, 223),
            WatchState.Detected => Color.FromArgb(255, 244, 214),
            WatchState.SisrMissing => Color.FromArgb(252, 228, 228),
            _ => Color.FromArgb(238, 238, 238)
        };
        _statusTitle.ForeColor = Color.FromArgb(30, 30, 30);
        _statusDetail.ForeColor = Color.FromArgb(70, 70, 70);
        _stopBtn.Visible = s.State == WatchState.Running;
    }

    private void RefreshAll()
    {
        RefreshSettings();
        PopulateList();
    }

    private void RefreshSettings()
    {
        _loading = true;
        try
        {
            _delay.Value = Math.Clamp(_cfg.Shared.SisrStartDelaySeconds, 0, 600);
            try { _startup.Checked = StartupRegistration.IsEnabled(); } catch { }

            var path = _cfg.ResolveSisrPath();
            // Don't overwrite what's being typed if the settings refresh while the box has focus.
            if (!_sisrBox.Focused) _sisrBox.Text = path ?? "";
            if (path == null)
            {
                _sisrMsg.Text = "SISR.exe wasn't found. Click Browse, or Auto-detect to look near this app's folder.";
                _settingsPanel.Visible = true;
            }
            else
            {
                _sisrMsg.Text = "";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void PopulateList()
    {
        _loading = true;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var game in _cfg.Shared.Games)
            {
                var item = new ListViewItem(game.Name) { Checked = game.Enabled, Tag = game };
                item.SubItems.Add(game.Enabled ? "watching" : "paused");
                item.ForeColor = game.Enabled ? SystemColors.WindowText : SystemColors.GrayText;
                _list.Items.Add(item);
            }
            _count.Text = $"Your games ({_cfg.Shared.Games.Count})";
        }
        finally
        {
            _list.EndUpdate();
            _loading = false;
        }
        AdjustColumns();
    }

    private void AdjustColumns()
    {
        if (_list.Columns.Count < 2) return;
        _list.Columns[1].Width = 110;
        _list.Columns[0].Width = Math.Max(150, _list.ClientSize.Width - 110);
    }

    // ---------- actions ----------

    private void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_loading || e.Item.Tag is not GameEntry game) return;
        game.Enabled = e.Item.Checked;
        e.Item.SubItems[1].Text = game.Enabled ? "watching" : "paused";
        e.Item.ForeColor = game.Enabled ? SystemColors.WindowText : SystemColors.GrayText;
        _cfg.SaveShared();
        _watcher.RefreshNow();
    }

    private void OpenAddDialog()
    {
        var existing = new HashSet<string>(_cfg.Shared.Games.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        using var dialog = new AddGameForm(existing);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        int added = _cfg.AddGames(dialog.SelectedNames);
        if (added > 0) _watcher.AddLog(added == 1 ? "Added 1 game" : $"Added {added} games");
        PopulateList();
        _watcher.RefreshNow();
    }

    private void RemoveSelected()
    {
        var names = _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text).ToList();
        if (names.Count == 0) return;

        foreach (var name in names) _cfg.RemoveGame(name);
        _watcher.AddLog(names.Count == 1 ? $"Removed {names[0]}" : $"Removed {names.Count} games");
        PopulateList();
        _watcher.RefreshNow();
    }

    private void SaveDelay()
    {
        if (_loading) return;
        int value = (int)_delay.Value;
        if (_cfg.Shared.SisrStartDelaySeconds == value) return;
        _cfg.Shared.SisrStartDelaySeconds = value;
        _cfg.SaveShared();
    }

    /// <summary>
    /// Uses the SISR path typed or pasted into the box. Accepts a path with quotes (Explorer's "Copy as path")
    /// or a folder that contains SISR.exe. Does nothing if the box is unchanged.
    /// </summary>
    private void ApplySisrText()
    {
        if (_loading) return;

        var text = _sisrBox.Text.Trim().Trim('"').Trim();
        var current = _cfg.ResolveSisrPath() ?? "";
        if (text.Length == 0 || string.Equals(text, current, StringComparison.OrdinalIgnoreCase)) return;

        string? file = null;
        try
        {
            if (File.Exists(text))
                file = Path.GetFullPath(text);
            else if (Directory.Exists(text) && File.Exists(Path.Combine(text, "SISR.exe")))
                file = Path.GetFullPath(Path.Combine(text, "SISR.exe"));
        }
        catch (Exception)
        {
            // An invalid path. Treated the same as a missing file below.
        }

        if (file == null)
        {
            _sisrMsg.Text = "That file wasn't found. Check the path, or click Browse.";
            return;
        }

        _sisrMsg.Text = "";
        _cfg.SetSisrPath(file);
        _watcher.AddLog($"SISR location set to {file}");
        _watcher.RefreshNow();
    }

    private void BrowseForSisr()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select SISR.exe",
            Filter = "SISR (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };

        var current = _cfg.ResolveSisrPath();
        if (current != null) dialog.InitialDirectory = Path.GetDirectoryName(current);

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _cfg.SetSisrPath(dialog.FileName);
        _watcher.AddLog($"SISR location set to {dialog.FileName}");
        _watcher.RefreshNow();
    }
}
