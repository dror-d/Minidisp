using Minidisp.Companion.Models;
using Minidisp.Companion.Services;

namespace Minidisp.Companion.Editor;

/// <summary>
/// Visual theme editor: WYSIWYG canvas at a selectable device resolution,
/// widget editing, theme file management, and live push to the device.
/// </summary>
public sealed class ThemeEditorForm : Form
{
    private static readonly (string Name, int W, int H)[] DeviceSizes =
    [
        ("CYD 2.8\"  320×240", 320, 240),
        ("CYD portrait  240×320", 240, 320),
        ("C6 1.47\"  172×320", 172, 320),
        ("C6 1.47\" landscape  320×172", 320, 172),
        ("1.9\"  320×170", 320, 170),
        ("1.9\" portrait  170×320", 170, 320),
    ];

    private readonly SerialSender? _sender;
    private readonly Func<StatsSnapshot?>? _liveSnapshot;

    private readonly EditorCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly PropertyPanel _props = new() { Dock = DockStyle.Fill };
    private readonly ToolStripComboBox _pageCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ToolStripComboBox _sizeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 750 };
    private ToolStripMenuItem _liveMenuItem = null!;
    private ToolStripMenuItem _snapMenuItem = null!;

    private ThemeDocument _doc = ThemeDocument.NewDefault();
    private string? _themeDir;
    private bool _dirty;
    private readonly List<string> _undo = [];

    public ThemeEditorForm(SerialSender? sender, Func<StatsSnapshot?>? liveSnapshot)
    {
        _sender = sender;
        _liveSnapshot = liveSnapshot;

        Text = "Minidisp Theme Editor";
        Size = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterDistance = 760,
        };
        split.Panel1.Controls.Add(_canvas);
        split.Panel2.Controls.Add(_props);

        Controls.Add(split);
        Controls.Add(BuildToolbar());
        Controls.Add(BuildMenu());
        Controls.Add(BuildStatusBar());

        _canvas.Document = _doc;
        _canvas.WidgetActivated += (_, w) => ActivateWidget(w);
        _canvas.ContextMenuStrip = BuildCanvasMenu();
        _canvas.SelectionChanged += (_, _) => _props.ShowWidget(_canvas.SelectedWidget, _doc);
        _canvas.BeforeEdit += (_, _) => PushUndo();
        _canvas.DocumentEdited += (_, _) =>
        {
            MarkDirty();
            _props.ShowWidget(_canvas.SelectedWidget, _doc);
        };
        _props.BeforeChange += (_, _) => PushUndo();
        _props.Changed += (_, _) => { MarkDirty(); _canvas.Invalidate(); };
        _props.ShowWidget(null, _doc);

        _liveTimer.Tick += async (_, _) => await LiveTick();
        KeyDown += OnKey;
        FormClosing += OnClosingConfirm;

        RefreshPages();
        UpdateTitle();
    }

    // ---- UI construction --------------------------------------------------

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("&New", null, (_, _) => NewTheme());
        file.DropDownItems.Add("&Open...", null, (_, _) => OpenTheme());
        file.DropDownItems.Add("&Save", null, (_, _) => SaveTheme(saveAs: false));
        file.DropDownItems.Add("Save &As...", null, (_, _) => SaveTheme(saveAs: true));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Import &Logo...", null, (_, _) => ImportLogo());
        file.DropDownItems.Add(new ToolStripSeparator());
        var push = new ToolStripMenuItem("&Push to Device", null, async (_, _) => await PushToDevice())
        {
            Enabled = _sender is not null,
        };
        file.DropDownItems.Add(push);

        var theme = new ToolStripMenuItem("&Theme");
        var palette = new ToolStripMenuItem("&Palette");
        foreach (var key in ThemeDocument.DefaultColors.Keys)
        {
            var item = new ToolStripMenuItem(key, null, (_, _) => EditPaletteColor(key));
            palette.DropDownItems.Add(item);
        }
        theme.DropDownItems.Add(palette);
        theme.DropDownItems.Add("&Warn thresholds...", null, (_, _) => EditWarnRules());
        theme.DropDownItems.Add("&Rename theme...", null, (_, _) => RenameTheme());

        var view = new ToolStripMenuItem("&View");
        _snapMenuItem = new ToolStripMenuItem("Snap to grid") { Checked = true, CheckOnClick = true };
        _snapMenuItem.CheckedChanged += (_, _) => _canvas.SnapToGrid = _snapMenuItem.Checked;
        _liveMenuItem = new ToolStripMenuItem("Live data") { CheckOnClick = true, Enabled = _liveSnapshot is not null };
        _liveMenuItem.CheckedChanged += (_, _) => ToggleLive(_liveMenuItem.Checked);
        view.DropDownItems.Add(_snapMenuItem);
        view.DropDownItems.Add(_liveMenuItem);

        menu.Items.Add(file);
        menu.Items.Add(theme);
        menu.Items.Add(view);
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

        foreach (var (name, _, _) in DeviceSizes) _sizeCombo.Items.Add(name);
        _sizeCombo.SelectedIndex = 0;
        _sizeCombo.SelectedIndexChanged += (_, _) =>
        {
            var (_, w, h) = DeviceSizes[_sizeCombo.SelectedIndex];
            _canvas.ScreenSize = new Size(w, h);
            _canvas.Invalidate();
        };
        bar.Items.Add(new ToolStripLabel("Device:"));
        bar.Items.Add(_sizeCombo);
        bar.Items.Add(new ToolStripSeparator());

        bar.Items.Add(new ToolStripLabel("Page:"));
        _pageCombo.SelectedIndexChanged += (_, _) =>
        {
            _canvas.PageIndex = Math.Max(0, _pageCombo.SelectedIndex);
            _canvas.Select(-1);
            _canvas.Invalidate();
        };
        bar.Items.Add(_pageCombo);
        bar.Items.Add(new ToolStripButton("+", null, (_, _) => AddPage()) { ToolTipText = "Add page" });
        bar.Items.Add(new ToolStripButton("−", null, (_, _) => RemovePage()) { ToolTipText = "Remove page" });
        bar.Items.Add(new ToolStripSeparator());

        bar.Items.Add(new ToolStripLabel("Add:"));
        foreach (var type in new[] { "text", "bar", "arc", "chart", "image", "rect" })
            bar.Items.Add(new ToolStripButton(type, null, (_, _) => AddWidget(type)));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(new ToolStripButton("Delete", null, (_, _) => DeleteSelected())
        {
            ToolTipText = "Delete selected widget (Del)",
        });
        bar.Items.Add(new ToolStripButton("Undo", null, (_, _) => Undo())
        {
            ToolTipText = "Undo (Ctrl+Z)",
        });
        return bar;
    }

    private StatusStrip BuildStatusBar()
    {
        var strip = new StatusStrip();
        strip.Items.Add(_status);
        strip.Items.Add(new ToolStripStatusLabel(
            "Preview fonts are approximate (Segoe UI vs device Montserrat)")
        {
            ForeColor = Color.Gray,
        });
        return strip;
    }

    // ---- document operations ----------------------------------------------

    private void MarkDirty()
    {
        _dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Text = $"Minidisp Theme Editor — {_doc.Name}{(_dirty ? " *" : "")}" +
               (_themeDir is null ? " (unsaved)" : $"  [{_themeDir}]");

    private void PushUndo()
    {
        _undo.Add(_doc.ToJson());
        if (_undo.Count > 50) _undo.RemoveAt(0);
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _doc = ThemeDocument.FromJson(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        _canvas.Document = _doc;
        _canvas.Select(-1);
        RefreshPages();
        MarkDirty();
        _canvas.Invalidate();
        _props.ShowWidget(null, _doc);
    }

    private void RefreshPages()
    {
        _pageCombo.Items.Clear();
        for (int i = 0; i < _doc.Pages.Count; i++)
            _pageCombo.Items.Add(_doc.Pages[i].Name ?? $"Page {i + 1}");
        if (_pageCombo.Items.Count > 0)
            _pageCombo.SelectedIndex = Math.Clamp(_canvas.PageIndex, 0, _pageCombo.Items.Count - 1);
    }

    private void NewTheme()
    {
        if (!ConfirmDiscard()) return;
        _doc = ThemeDocument.NewDefault();
        _themeDir = null;
        WidgetRenderer.ThemeDir = null;
        WidgetRenderer.ClearImageCache();
        _undo.Clear();
        _dirty = false;
        _canvas.Document = _doc;
        _canvas.PageIndex = 0;
        _canvas.Select(-1);
        RefreshPages();
        UpdateTitle();
        _canvas.Invalidate();
    }

    private void OpenTheme()
    {
        if (!ConfirmDiscard()) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Open theme.json",
            Filter = "Theme (theme.json)|theme.json|JSON files (*.json)|*.json",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            _doc = ThemeDocument.Load(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load theme: {ex.Message}", "Minidisp",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _themeDir = Path.GetDirectoryName(dialog.FileName);
        WidgetRenderer.ThemeDir = _themeDir;
        WidgetRenderer.ClearImageCache();
        _undo.Clear();
        _dirty = false;
        _canvas.Document = _doc;
        _canvas.PageIndex = 0;
        _canvas.Select(-1);
        RefreshPages();
        UpdateTitle();
        _canvas.Invalidate();
        _status.Text = $"Opened {dialog.FileName}";
    }

    private bool SaveTheme(bool saveAs)
    {
        if (_themeDir is null || saveAs)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Save theme.json (pick or create the theme's folder)",
                Filter = "Theme (theme.json)|theme.json",
                FileName = "theme.json",
            };
            if (dialog.ShowDialog() != DialogResult.OK) return false;
            _themeDir = Path.GetDirectoryName(dialog.FileName);
            WidgetRenderer.ThemeDir = _themeDir;
        }
        try
        {
            _doc.Save(Path.Combine(_themeDir!, "theme.json"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}", "Minidisp",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _dirty = false;
        UpdateTitle();
        _status.Text = $"Saved to {_themeDir}";
        return true;
    }

    private void ImportLogo()
    {
        if (_themeDir is null && !SaveTheme(saveAs: true)) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a PNG logo (≤120x120 recommended)",
            Filter = "PNG images (*.png)|*.png",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        File.Copy(dialog.FileName, Path.Combine(_themeDir!, "logo.png"), overwrite: true);
        WidgetRenderer.ClearImageCache();
        _canvas.Invalidate();
        _status.Text = "Logo imported as logo.png";
    }

    private bool ConfirmDiscard() =>
        !_dirty || MessageBox.Show("Discard unsaved changes?", "Minidisp",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void OnClosingConfirm(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
    }

    // ---- editing actions --------------------------------------------------

    private List<ThemeWidget>? CurrentWidgets =>
        _canvas.PageIndex < _doc.Pages.Count ? _doc.Pages[_canvas.PageIndex].Widgets : null;

    private void AddWidget(string type, Point? positionPm = null)
    {
        if (CurrentWidgets is not { } widgets) return;
        PushUndo();
        var widget = type switch
        {
            "bar" => new ThemeWidget { Type = "bar", X = 100, Y = 450, W = 800, H = 60, Bind = "cpu.load" },
            "arc" => new ThemeWidget { Type = "arc", X = 500, Y = 500, Anchor = "mc", R = 200, Thickness = 40, Bind = "cpu.load", Label = true },
            "chart" => new ThemeWidget { Type = "chart", X = 100, Y = 300, W = 800, H = 400, Bind = "cpu.load" },
            "image" => new ThemeWidget { Type = "image", X = 40, Y = 40, Src = "logo.png" },
            "rect" => new ThemeWidget { Type = "rect", X = 100, Y = 100, W = 300, H = 200 },
            _ => new ThemeWidget { Type = "text", X = 400, Y = 450, Bind = "cpu.load", Fmt = "{v:.0f}%" },
        };
        if (positionPm is { } p)
        {
            widget.X = p.X;
            widget.Y = p.Y;
        }
        widgets.Add(widget);
        MarkDirty();
        _canvas.Select(widgets.Count - 1);
        _canvas.Invalidate();
        // Adding a text/image is usually followed by editing it — open the editor.
        if (type is "text" or "image") ActivateWidget(widget);
    }

    // ---- in-place editing (double-click / context menu) -------------------

    private void ActivateWidget(ThemeWidget w)
    {
        switch (w.Type)
        {
            case "text": EditTextWidget(w); break;
            case "image": ChooseImage(w); break;
            default: EditBind(w); break;
        }
    }

    private void EditTextWidget(ThemeWidget w)
    {
        if (string.IsNullOrEmpty(w.Bind))
        {
            var text = Prompt("Text to display (static — or set a Bind in the panel for live values):",
                w.Text ?? "", multiline: false);
            if (text is null) return;
            PushUndo();
            w.Text = text;
        }
        else
        {
            var fmt = Prompt($"Format for '{w.Bind}' — {{v}} inserts the value, {{v:.1f}} with decimals:",
                w.Fmt ?? "{v:.0f}", multiline: false);
            if (fmt is null) return;
            PushUndo();
            w.Fmt = string.IsNullOrWhiteSpace(fmt) ? null : fmt;
        }
        MarkDirty();
        _canvas.Invalidate();
        _props.ShowWidget(_canvas.SelectedWidget, _doc);
    }

    private void EditBind(ThemeWidget w)
    {
        var bind = Prompt(
            "Data bind path (sensor path like cpu.load, or a custom XML value id):",
            w.Bind ?? "", multiline: false);
        if (bind is null) return;
        PushUndo();
        w.Bind = string.IsNullOrWhiteSpace(bind) ? null : bind.Trim();
        MarkDirty();
        _canvas.Invalidate();
        _props.ShowWidget(_canvas.SelectedWidget, _doc);
    }

    private void ChooseImage(ThemeWidget w)
    {
        if (_themeDir is null)
        {
            MessageBox.Show("Save the theme first — images are copied into the theme's folder.",
                "Minidisp", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (!SaveTheme(saveAs: true)) return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a PNG image",
            Filter = "PNG images (*.png)|*.png",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var fileName = Path.GetFileName(dialog.FileName);
        var target = Path.Combine(_themeDir!, fileName);
        if (!string.Equals(dialog.FileName, target, StringComparison.OrdinalIgnoreCase))
            File.Copy(dialog.FileName, target, overwrite: true);
        PushUndo();
        w.Src = fileName;
        WidgetRenderer.ClearImageCache();
        MarkDirty();
        _canvas.Invalidate();
        _props.ShowWidget(_canvas.SelectedWidget, _doc);
    }

    private void DuplicateSelected()
    {
        if (CurrentWidgets is not { } widgets || _canvas.SelectedWidget is not { } w) return;
        PushUndo();
        var copy = System.Text.Json.JsonSerializer.Deserialize<ThemeWidget>(
            System.Text.Json.JsonSerializer.Serialize(w, ThemeDocument.JsonOptions),
            ThemeDocument.JsonOptions)!;
        copy.X = Math.Min(1000, copy.X + 25);
        copy.Y = Math.Min(1000, copy.Y + 25);
        widgets.Add(copy);
        MarkDirty();
        _canvas.Select(widgets.Count - 1);
        _canvas.Invalidate();
    }

    private void Reorder(bool toFront)
    {
        if (CurrentWidgets is not { } widgets || _canvas.SelectedIndex < 0) return;
        PushUndo();
        var w = widgets[_canvas.SelectedIndex];
        widgets.RemoveAt(_canvas.SelectedIndex);
        if (toFront) widgets.Add(w);
        else widgets.Insert(0, w);
        MarkDirty();
        _canvas.Select(toFront ? widgets.Count - 1 : 0);
        _canvas.Invalidate();
    }

    private ContextMenuStrip BuildCanvasMenu()
    {
        var menu = new ContextMenuStrip();
        var editText = new ToolStripMenuItem("Edit text...", null, (_, _) =>
        { if (_canvas.SelectedWidget is { } w) EditTextWidget(w); });
        var chooseImage = new ToolStripMenuItem("Choose image...", null, (_, _) =>
        { if (_canvas.SelectedWidget is { } w) ChooseImage(w); });
        var editBind = new ToolStripMenuItem("Edit data bind...", null, (_, _) =>
        { if (_canvas.SelectedWidget is { } w) EditBind(w); });
        var duplicate = new ToolStripMenuItem("Duplicate", null, (_, _) => DuplicateSelected());
        var front = new ToolStripMenuItem("Bring to front", null, (_, _) => Reorder(toFront: true));
        var back = new ToolStripMenuItem("Send to back", null, (_, _) => Reorder(toFront: false));
        var delete = new ToolStripMenuItem("Delete", null, (_, _) => DeleteSelected());

        var addMenu = new ToolStripMenuItem("Add widget");
        Point addAt = default;
        foreach (var type in new[] { "text", "bar", "arc", "chart", "image", "rect" })
            addMenu.DropDownItems.Add(new ToolStripMenuItem(type, null,
                (_, _) => AddWidget(type, addAt)));

        var widgetSeparator = new ToolStripSeparator();
        menu.Items.AddRange([editText, chooseImage, editBind, widgetSeparator,
            duplicate, front, back, delete, addMenu]);

        menu.Opening += (_, _) =>
        {
            addAt = _canvas.PmAt(_canvas.PointToClient(Cursor.Position));
            var w = _canvas.SelectedWidget;
            editText.Visible = w?.Type == "text";
            chooseImage.Visible = w?.Type == "image";
            editBind.Visible = w is not null && w.Type is "bar" or "arc" or "chart" or "text";
            widgetSeparator.Visible = duplicate.Visible = front.Visible =
                back.Visible = delete.Visible = w is not null;
            addMenu.Visible = w is null;
        };
        return menu;
    }

    private void DeleteSelected()
    {
        if (CurrentWidgets is not { } widgets || _canvas.SelectedIndex < 0) return;
        PushUndo();
        widgets.RemoveAt(_canvas.SelectedIndex);
        MarkDirty();
        _canvas.Select(-1);
        _canvas.Invalidate();
    }

    private void AddPage()
    {
        PushUndo();
        _doc.Pages.Add(new ThemePage { Name = $"Page {_doc.Pages.Count + 1}" });
        MarkDirty();
        _canvas.PageIndex = _doc.Pages.Count - 1;
        RefreshPages();
        _canvas.Invalidate();
    }

    private void RemovePage()
    {
        if (_doc.Pages.Count <= 1) return;
        if (MessageBox.Show("Remove this page and its widgets?", "Minidisp",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        PushUndo();
        _doc.Pages.RemoveAt(_canvas.PageIndex);
        _canvas.PageIndex = Math.Max(0, _canvas.PageIndex - 1);
        MarkDirty();
        _canvas.Select(-1);
        RefreshPages();
        _canvas.Invalidate();
    }

    private void EditPaletteColor(string key)
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = WidgetRenderer.ResolveColor(_doc, null, key),
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        PushUndo();
        _doc.Colors[key] = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        MarkDirty();
        _canvas.Invalidate();
    }

    private void EditWarnRules()
    {
        var current = string.Join(Environment.NewLine,
            (_doc.WarnAbove ?? []).Select(kv => $"{kv.Key}={kv.Value}"));
        var text = Prompt("Warn thresholds (one per line, e.g. cpu.temp=85):", current, multiline: true);
        if (text is null) return;
        PushUndo();
        var rules = new Dictionary<string, float>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && float.TryParse(parts[1],
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                rules[parts[0]] = v;
        }
        _doc.WarnAbove = rules.Count > 0 ? rules : null;
        MarkDirty();
        _canvas.Invalidate();
    }

    private void RenameTheme()
    {
        var name = Prompt("Theme name:", _doc.Name, multiline: false);
        if (string.IsNullOrWhiteSpace(name)) return;
        PushUndo();
        _doc.Name = name.Trim();
        MarkDirty();
        UpdateTitle();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.Handled = true; }
        else if (e.KeyCode == Keys.Delete && !_props.ContainsFocus) { DeleteSelected(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.S) { SaveTheme(saveAs: false); e.Handled = true; }
    }

    // ---- live preview & push ----------------------------------------------

    private void ToggleLive(bool on)
    {
        if (on)
        {
            _liveTimer.Start();
        }
        else
        {
            _liveTimer.Stop();
            _canvas.Stats = new SampleStats();
            _canvas.History.Clear();
            _canvas.Invalidate();
        }
    }

    private bool _liveBusy;

    private async Task LiveTick()
    {
        if (_liveBusy || _liveSnapshot is null) return;
        _liveBusy = true;
        StatsSnapshot? snap;
        try
        {
            // Sensor polling takes 50-300ms — keep it off the UI thread.
            snap = await Task.Run(_liveSnapshot);
        }
        finally
        {
            _liveBusy = false;
        }
        if (snap is null || IsDisposed) return;
        var stats = new SnapshotStats(snap);
        _canvas.Stats = stats;
        foreach (var page in _doc.Pages)
            foreach (var w in page.Widgets)
                if (w.Type == "chart" && w.Bind is { Length: > 0 } &&
                    stats.TryGetNumber(w.Bind, out var v))
                    _canvas.History.Add(w.Bind, v);
        _canvas.Invalidate();
    }

    private async Task PushToDevice()
    {
        if (_sender is null) return;
        if (_dirty || _themeDir is null)
        {
            if (!SaveTheme(saveAs: _themeDir is null)) return;
        }
        var progress = new Progress<string>(s => _status.Text = s);
        var (ok, message) = await ThemeUploader.PushThemeAsync(_sender, _themeDir!, progress);
        _status.Text = message;
        if (!ok)
            MessageBox.Show(message, "Push failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static string? Prompt(string label, string initial, bool multiline)
    {
        using var form = new Form
        {
            Text = "Minidisp",
            Size = new Size(420, multiline ? 260 : 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
        };
        var labelCtl = new Label { Text = label, Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 6, 8, 0) };
        var box = new TextBox
        {
            Text = initial,
            Dock = DockStyle.Fill,
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 38, Padding = new Padding(6) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        content.Controls.Add(box);
        form.Controls.Add(content);
        form.Controls.Add(buttons);
        form.Controls.Add(labelCtl);
        form.AcceptButton = multiline ? null : ok;
        form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? box.Text : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _liveTimer.Dispose();
        base.Dispose(disposing);
    }
}
