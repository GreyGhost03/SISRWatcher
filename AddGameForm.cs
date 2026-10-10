namespace SISRWatcher;

/// <summary>
/// The "Add game" dialog with three ways to find a game:
/// running programs, browse/drop an exe, or scan the XboxGames folders.
/// </summary>
public sealed class AddGameForm : Form
{
    private enum Mode { Running, Browse, Scan, Type }

    private readonly HashSet<string> _existing;
    private Mode _mode = Mode.Running;

    private readonly RadioButton _runningBtn = MakeModeButton("Running now");
    private readonly RadioButton _browseBtn = MakeModeButton("Browse or drop exe");
    private readonly RadioButton _scanBtn = MakeModeButton("Scan Xbox games");
    private readonly RadioButton _typeBtn = MakeModeButton("Type a name");
    private readonly FlowLayoutPanel _typePanel = new();
    private readonly TextBox _typeBox = new();
    private readonly Button _typeAddBtn = new() { Text = "Add to list", AutoSize = true, Margin = new Padding(0) };
    private readonly Button _okBtn = new() { Text = "Add selected", AutoSize = true };
    private readonly Label _dropBox = new();
    private readonly Label _hint = new();
    private readonly TextBox _filterBox = new() { Width = 220, PlaceholderText = "Filter by name", Margin = new Padding(0, 1, 8, 0) };
    private readonly Button _refreshBtn = new() { Text = "Refresh", AutoSize = true, Margin = new Padding(0) };
    private readonly FlowLayoutPanel _options = new();
    private readonly ListView _list = new();

    // "Running now" keeps its full result so filtering doesn't have to look at the processes again,
    // and remembers what was ticked while the filter changes the visible rows.
    private List<Candidate> _runningCache = new();
    private readonly HashSet<string> _tickedNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _rendering;

    /// <summary>Process names the user ticked. Only filled in when the dialog returns OK.</summary>
    public List<string> SelectedNames { get; } = new();

    public AddGameForm(HashSet<string> existing)
    {
        _existing = existing;

        Text = "Add game";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 520);

        BuildLayout();
        SwitchMode(Mode.Running);
    }

    private static RadioButton MakeModeButton(string text) => new()
    {
        Text = text,
        Appearance = Appearance.Button,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleCenter,
        Padding = new Padding(8, 3, 8, 3)
    };

    // ---------- layout ----------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++)
            root.RowStyles.Add(i == 4 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));

        // Row 0: the three mode buttons
        var modes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(12, 12, 12, 6)
        };
        modes.Controls.Add(_runningBtn);
        modes.Controls.Add(_browseBtn);
        modes.Controls.Add(_scanBtn);
        modes.Controls.Add(_typeBtn);
        _runningBtn.Checked = true;
        _runningBtn.CheckedChanged += (_, _) => { if (_runningBtn.Checked) SwitchMode(Mode.Running); };
        _browseBtn.CheckedChanged += (_, _) => { if (_browseBtn.Checked) SwitchMode(Mode.Browse); };
        _scanBtn.CheckedChanged += (_, _) => { if (_scanBtn.Checked) SwitchMode(Mode.Scan); };
        _typeBtn.CheckedChanged += (_, _) => { if (_typeBtn.Checked) SwitchMode(Mode.Type); };
        root.Controls.Add(modes, 0, 0);

        // Row 1: drop box (Browse mode only)
        _dropBox.Text = "Drop a game .exe or shortcut here, or click to browse";
        _dropBox.TextAlign = ContentAlignment.MiddleCenter;
        _dropBox.BorderStyle = BorderStyle.FixedSingle;
        _dropBox.Cursor = Cursors.Hand;
        _dropBox.AllowDrop = true;
        _dropBox.Height = 80;
        _dropBox.Dock = DockStyle.Fill;
        _dropBox.Margin = new Padding(12, 0, 12, 6);
        _dropBox.Visible = false;
        _dropBox.Click += (_, _) => BrowseForFiles();
        _dropBox.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        _dropBox.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                foreach (var file in files) AddFile(file);
        };
        root.Controls.Add(_dropBox, 0, 1);

        // Row 2: type a process name (Type mode only)
        _typePanel.AutoSize = true;
        _typePanel.Dock = DockStyle.Fill;
        _typePanel.WrapContents = false;
        _typePanel.Padding = new Padding(12, 0, 12, 6);
        _typePanel.Visible = false;
        _typeBox.Width = 320;
        _typeBox.Margin = new Padding(0, 0, 8, 0);
        _typeBox.PlaceholderText = "MyGame.exe";
        _typeAddBtn.Click += (_, _) => AddTyped();
        // The dialog's default button (Enter key) is "Add selected". While the cursor is in the
        // type box, make Enter press "Add to list" instead. KeyDown can't do this, because the
        // form handles the default button before the text box sees the key.
        _typeBox.Enter += (_, _) => AcceptButton = _typeAddBtn;
        _typeBox.Leave += (_, _) => AcceptButton = _okBtn;
        _typePanel.Controls.Add(_typeBox);
        _typePanel.Controls.Add(_typeAddBtn);
        root.Controls.Add(_typePanel, 0, 2);

        // Row 3: hint text and options
        var hintPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12, 0, 12, 4)
        };
        _hint.AutoSize = true;
        _hint.MaximumSize = new Size(610, 0);
        _hint.ForeColor = SystemColors.GrayText;
        _options.AutoSize = true;
        _options.WrapContents = false;
        _options.Margin = new Padding(0, 6, 0, 0);
        _options.Controls.Add(_filterBox);
        _options.Controls.Add(_refreshBtn);
        hintPanel.Controls.Add(_hint);
        hintPanel.Controls.Add(_options);
        root.Controls.Add(hintPanel, 0, 3);
        _filterBox.TextChanged += (_, _) =>
        {
            if (_mode == Mode.Running) RenderRunning();
        };
        _refreshBtn.Click += (_, _) => Reload();

        // Row 4: results list
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.Margin = new Padding(12, 0, 12, 6);
        _list.Columns.Add("Process name", 230);
        _list.Columns.Add("Details", 360);
        _list.ItemCheck += (_, e) =>
        {
            // Games already in the list can't be added twice.
            if (e.Index >= 0 && e.Index < _list.Items.Count && _existing.Contains(_list.Items[e.Index].Text))
                e.NewValue = CheckState.Unchecked;
        };
        _list.ItemChecked += (_, e) =>
        {
            if (_rendering || _mode != Mode.Running) return;
            if (e.Item.Checked) _tickedNames.Add(e.Item.Text);
            else _tickedNames.Remove(e.Item.Text);
        };
        _list.KeyDown += (_, e) =>
        {
            // Ctrl+C copies the highlighted names, one per line.
            if (e.Control && e.KeyCode == Keys.C)
            {
                ClipboardHelper.CopyLines(_list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text));
                e.SuppressKeyPress = true;
            }
        };
        root.Controls.Add(_list, 0, 4);

        // Row 5: buttons
        var cancelBtn = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        _okBtn.Click += (_, _) => Confirm();
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 6, 12, 12)
        };
        buttons.Controls.Add(cancelBtn);
        buttons.Controls.Add(_okBtn);
        root.Controls.Add(buttons, 0, 5);

        AcceptButton = _okBtn;
        CancelButton = cancelBtn;
        Controls.Add(root);
    }

    // ---------- modes ----------

    private void SwitchMode(Mode mode)
    {
        _filterBox.Clear();
        _tickedNames.Clear();
        _mode = mode;
        _list.Items.Clear();
        _list.Groups.Clear();
        _list.ShowGroups = mode == Mode.Running;

        _dropBox.Visible = mode == Mode.Browse;
        _typePanel.Visible = mode == Mode.Type;
        _filterBox.Visible = mode == Mode.Running;
        _refreshBtn.Visible = mode == Mode.Running || mode == Mode.Scan;
        _options.Visible = mode == Mode.Running || mode == Mode.Scan;

        switch (mode)
        {
            case Mode.Running:
                _hint.Text = "Launch the game first, then tick it here. Everything running is listed: likely games first " +
                             "(programs running from a games folder), then programs with a window, then background " +
                             "processes. Use the filter to find your game, and Refresh after it has started.";
                break;
            case Mode.Browse:
                _hint.Text = "Drop a game .exe or shortcut onto the box above, or click it to browse. " +
                             "Dropped games appear below, ticked.";
                break;
            case Mode.Scan:
                _hint.Text = "Scanning your XboxGames folders...";
                break;
            case Mode.Type:
                _hint.Text = "Type the game's exe name, with or without .exe (for example MyGame.exe). " +
                             "The game doesn't need to be installed. Names aren't case sensitive. " +
                             "Typed games appear below, ticked.";
                break;
        }

        Reload();
        if (mode == Mode.Type) _typeBox.Focus();
    }

    // ---------- type a name ----------

    private void AddTyped()
    {
        // Pasted text often has quotes (Explorer's "Copy as path") or a whole path. Keep just the file name.
        var name = _typeBox.Text.Trim().Trim('"').Trim();
        if (name.Contains('\\') || name.Contains('/'))
            name = Path.GetFileName(name);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4].TrimEnd();

        if (name.Length == 0)
        {
            MessageBox.Show(this, "Type the game's exe name first.", "Add game",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this,
                "That doesn't look like an exe name. Type or paste a file name such as MyGame.exe.",
                "Add game", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_existing.Contains(name))
        {
            MessageBox.Show(this, $"{name} is already in your list.", "Add game",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (ListViewItem item in _list.Items)
        {
            if (string.Equals(item.Text, name, StringComparison.OrdinalIgnoreCase))
            {
                item.Checked = true;
                _typeBox.Clear();
                return;
            }
        }

        SetItems(new[] { new Candidate(name, "Typed by you", null) }, true);
        _typeBox.Clear();
        _typeBox.Focus();
    }

    private void Reload()
    {
        switch (_mode)
        {
            case Mode.Running:
                try
                {
                    _runningCache = GameDiscovery.RunningProcesses();
                    RenderRunning();
                }
                catch (Exception ex)
                {
                    _hint.Text = $"Couldn't read the running programs: {ex.Message}";
                }
                break;

            case Mode.Scan:
                _list.Items.Clear();
                _ = ScanAsync();
                break;
        }
    }

    /// <summary>Shows the cached running programs, narrowed by the filter box, keeping ticks.</summary>
    private void RenderRunning()
    {
        var filter = _filterBox.Text.Trim();
        var visible = _runningCache.Where(c =>
            filter.Length == 0 ||
            c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            c.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase));

        _rendering = true;
        try
        {
            _list.Items.Clear();
            _list.Groups.Clear();
            SetItems(visible, false);
            foreach (ListViewItem item in _list.Items)
            {
                if (_tickedNames.Contains(item.Text) && !_existing.Contains(item.Text))
                    item.Checked = true;
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private async Task ScanAsync()
    {
        _hint.Text = "Scanning your XboxGames folders...";
        List<Candidate> found;
        try
        {
            found = await Task.Run(GameDiscovery.ScanXboxGames);
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _hint.Text = $"The scan failed: {ex.Message}";
            return;
        }

        if (IsDisposed || _mode != Mode.Scan) return;

        SetItems(found, false);
        _hint.Text = found.Count == 0
            ? "No XboxGames folder with games was found on your drives. Try Running now or Browse instead."
            : "Executables found in XboxGames folders on your drives. Helper programs are hidden. " +
              "Some games need a different exe than the first one listed, so check the details column.";
    }

    private void SetItems(IEnumerable<Candidate> candidates, bool ticked)
    {
        _list.BeginUpdate();
        try
        {
            foreach (var c in candidates)
            {
                bool already = _existing.Contains(c.Name);
                var item = new ListViewItem(c.Name) { Tag = c };
                item.SubItems.Add(already ? "Already in your list" : c.Detail);
                if (already) item.ForeColor = SystemColors.GrayText;
                item.Checked = ticked && !already;

                if (!string.IsNullOrEmpty(c.Group))
                {
                    var group = _list.Groups.Cast<ListViewGroup>().FirstOrDefault(g => g.Header == c.Group);
                    if (group == null)
                    {
                        group = new ListViewGroup(c.Group);
                        _list.Groups.Add(group);
                    }
                    item.Group = group;
                }

                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    // ---------- browse / drop ----------

    private void BrowseForFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select game executables",
            Filter = "Programs and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var file in dialog.FileNames) AddFile(file);
    }

    private void AddFile(string path)
    {
        var target = path;
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            target = ResolveShortcut(path) ?? path;

        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
        {
            MessageBox.Show(this,
                $"{Path.GetFileName(path)} doesn't point to a game .exe file. " +
                "For Xbox games, try Running now or Scan Xbox games.",
                "Add game", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var name = Path.GetFileNameWithoutExtension(target);

        foreach (ListViewItem existingItem in _list.Items)
        {
            if (string.Equals(existingItem.Text, name, StringComparison.OrdinalIgnoreCase))
            {
                existingItem.Checked = true;
                return;
            }
        }

        SetItems(new[] { new Candidate(name, target, target) }, true);
    }

    private static string? ResolveShortcut(string shortcutPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            string target = shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }

    // ---------- finishing ----------

    private void Confirm()
    {
        SelectedNames.Clear();
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Checked && !_existing.Contains(item.Text))
                SelectedNames.Add(item.Text);
        }

        if (SelectedNames.Count == 0)
        {
            MessageBox.Show(this, "Tick at least one game first.", "Add game",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
