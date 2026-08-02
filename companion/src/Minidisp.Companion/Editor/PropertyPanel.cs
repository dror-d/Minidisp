namespace Minidisp.Companion.Editor;

/// <summary>
/// Property editor for the selected widget: builds type-specific rows and
/// writes changes straight back into the ThemeWidget.
/// </summary>
public sealed class PropertyPanel : UserControl
{
    private static readonly string[] BindPaths =
    [
        "", "cpu.load", "cpu.temp", "cpu.freq", "cpu.name",
        "cpu.core0", "cpu.core1", "cpu.core2", "cpu.core3",
        "cpu.core4", "cpu.core5", "cpu.core6", "cpu.core7",
        "mem.pct", "mem.used", "mem.total",
        "gpu.load", "gpu.temp", "gpu.name",
        "net.ip", "net.if", "net.up", "net.down",
        "net1.ip", "net1.if", "net1.up", "net1.down",
        "disk.pct", "disk.free", "disk.n", "disk1.pct", "disk1.free", "disk1.n",
        "host", "uptime",
    ];

    private static readonly string[] Anchors = ["tl", "tc", "tr", "ml", "mc", "mr", "bl", "bc", "br"];
    private static readonly string[] Sizes =
        ["sm", "md", "lg", "xl", "12", "14", "16", "20", "24", "28", "36"];
    private static readonly string[] ColorNames = ["", "fg", "bg", "accent", "accent2", "muted", "warn"];

    private readonly TableLayoutPanel _table;
    private ThemeWidget? _widget;
    private ThemeDocument? _doc;
    private bool _loading;

    /// <summary>Raised after any committed property change.</summary>
    public event EventHandler? Changed;
    /// <summary>Raised right before a change is applied (undo snapshot hook).</summary>
    public event EventHandler? BeforeChange;

    /// <summary>Supplies the current stats snapshot for the bind picker tree.</summary>
    public Func<Models.StatsSnapshot?>? SnapshotProvider { get; set; }

    public PropertyPanel()
    {
        _table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(6),
        };
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_table);
    }

    public void ShowWidget(ThemeWidget? widget, ThemeDocument? doc)
    {
        _widget = widget;
        _doc = doc;
        _loading = true;
        _table.SuspendLayout();
        _table.Controls.Clear();
        _table.RowCount = 0;

        if (widget is null)
        {
            AddHeader("No widget selected");
            AddHeader("Click a widget on the canvas,");
            AddHeader("or add one from the toolbar.");
        }
        else
        {
            AddHeader($"{widget.Type} widget");
            AddNumeric("X (‰)", widget.X, v => _widget!.X = v);
            AddNumeric("Y (‰)", widget.Y, v => _widget!.Y = v);
            AddCombo("Anchor", widget.Anchor ?? "tl", Anchors,
                v => _widget!.Anchor = v == "tl" ? null : v);

            switch (widget.Type)
            {
                case "text":
                    AddBind(widget);
                    AddText("Format", widget.Fmt, v => _widget!.Fmt = Blank(v));
                    AddText("Static text", widget.Text, v => _widget!.Text = Blank(v));
                    AddCombo("Size", widget.Size ?? "md", Sizes,
                        v => _widget!.Size = v == "md" ? null : v);
                    AddColor("Color", widget.Color, v => _widget!.Color = v);
                    break;
                case "bar":
                    AddBind(widget);
                    AddNumeric("W (‰)", widget.W ?? 300, v => _widget!.W = v);
                    AddNumeric("H (‰)", widget.H ?? 40, v => _widget!.H = v);
                    AddMinMax(widget);
                    AddColor("Color", widget.Color, v => _widget!.Color = v);
                    AddColor("Track", widget.Bg, v => _widget!.Bg = v);
                    break;
                case "arc":
                    AddBind(widget);
                    AddNumeric("Radius (‰)", widget.R ?? 200, v => _widget!.R = v);
                    AddNumeric("Thickness", widget.Thickness ?? 40, v => _widget!.Thickness = v);
                    AddMinMax(widget);
                    AddCheck("Center label", widget.Label ?? false, v => _widget!.Label = v ? true : null);
                    AddCombo("Size", widget.Size ?? "md", Sizes,
                        v => _widget!.Size = v == "md" ? null : v);
                    AddColor("Color", widget.Color, v => _widget!.Color = v);
                    AddColor("Track", widget.Bg, v => _widget!.Bg = v);
                    break;
                case "chart":
                    AddBind(widget);
                    AddNumeric("W (‰)", widget.W ?? 400, v => _widget!.W = v);
                    AddNumeric("H (‰)", widget.H ?? 250, v => _widget!.H = v);
                    AddMinMax(widget);
                    AddNumeric("Points", widget.Points ?? 60, v => _widget!.Points = v, 2, 240);
                    AddCheck("Autoscale", widget.Autoscale ?? false, v => _widget!.Autoscale = v ? true : null);
                    AddColor("Color", widget.Color, v => _widget!.Color = v);
                    break;
                case "image":
                    AddText("File", widget.Src ?? "logo.png", v => _widget!.Src = Blank(v));
                    AddNumeric("W (‰, 0=auto)", widget.W ?? 0, v => _widget!.W = v == 0 ? null : v, 0);
                    break;
                case "rect":
                    AddNumeric("W (‰)", widget.W ?? 100, v => _widget!.W = v);
                    AddNumeric("H (‰)", widget.H ?? 100, v => _widget!.H = v);
                    AddNumeric("Corner radius", widget.Radius ?? 4, v => _widget!.Radius = v, 0, 50);
                    AddColor("Color", widget.Color, v => _widget!.Color = v);
                    break;
            }
        }

        _table.ResumeLayout();
        _loading = false;
    }

    private static string? Blank(string v) => string.IsNullOrWhiteSpace(v) ? null : v;

    private void Commit(Action apply)
    {
        if (_loading || _widget is null) return;
        BeforeChange?.Invoke(this, EventArgs.Empty);
        apply();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- row builders -----------------------------------------------------

    private void AddHeader(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(0, 6, 0, 6),
        };
        _table.Controls.Add(label);
        _table.SetColumnSpan(label, 2);
    }

    private void AddRow(string label, Control control)
    {
        _table.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 0, 0),
        });
        control.Dock = DockStyle.Fill;
        _table.Controls.Add(control);
    }

    private void AddNumeric(string label, int value, Action<int> setter,
        int min = 0, int max = 1000)
    {
        var num = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max) };
        num.ValueChanged += (_, _) => Commit(() => setter((int)num.Value));
        AddRow(label, num);
    }

    private void AddText(string label, string? value, Action<string> setter)
    {
        var box = new TextBox { Text = value ?? "" };
        box.Leave += (_, _) => Commit(() => setter(box.Text));
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Commit(() => setter(box.Text)); e.SuppressKeyPress = true; }
        };
        AddRow(label, box);
    }

    private void AddCombo(string label, string value, string[] options, Action<string> setter)
    {
        // Editable so numeric sizes ("18") can be typed; snapped by the renderer.
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        combo.Items.AddRange(options);
        combo.Text = value;
        void CommitCombo()
        {
            if (combo.Text.Length > 0) Commit(() => setter(combo.Text));
        }
        combo.SelectedIndexChanged += (_, _) => CommitCombo();
        combo.Leave += (_, _) => CommitCombo();
        AddRow(label, combo);
    }

    private void AddCheck(string label, bool value, Action<bool> setter)
    {
        var check = new CheckBox { Checked = value };
        check.CheckedChanged += (_, _) => Commit(() => setter(check.Checked));
        AddRow(label, check);
    }

    private void AddBind(ThemeWidget widget)
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, Height = 26 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));

        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        combo.Items.AddRange(BindPaths);
        combo.Text = widget.Bind ?? "";
        void CommitBind() => Commit(() => _widget!.Bind = Blank(combo.Text));
        combo.SelectedIndexChanged += (_, _) => CommitBind();
        combo.Leave += (_, _) => CommitBind();

        var pick = new Button { Text = "…", Dock = DockStyle.Fill, Margin = new Padding(1) };
        pick.Click += (_, _) =>
        {
            if (_widget is null) return;
            var result = BindPickerDialog.Pick(FindForm()!, _widget, isText: false,
                SnapshotProvider?.Invoke(), new SampleStats());
            if (result is null) return;
            combo.Text = result.Bind ?? "";
            Commit(() => _widget.Bind = result.Bind);
        };

        panel.Controls.Add(combo);
        panel.Controls.Add(pick);
        AddRow("Bind", panel);
    }

    private void AddMinMax(ThemeWidget widget)
    {
        AddNumeric("Min", widget.Min ?? 0, v => _widget!.Min = v == 0 ? null : v, 0, 100000);
        AddNumeric("Max", widget.Max ?? 100, v => _widget!.Max = v == 100 ? null : v, 1, 100000);
    }

    private void AddColor(string label, string? value, Action<string?> setter)
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, Height = 26 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));

        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        combo.Items.AddRange(ColorNames);
        combo.Text = value ?? "";

        var pick = new Button { Text = "…", Dock = DockStyle.Fill, Margin = new Padding(1) };
        pick.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { FullOpen = true };
            if (_doc is not null && WidgetRenderer.ResolveColor(_doc, combo.Text, "fg") is { } c)
                dialog.Color = c;
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                combo.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                Commit(() => setter(combo.Text));
            }
        };

        void CommitColor() => Commit(() => setter(Blank(combo.Text)));
        combo.SelectedIndexChanged += (_, _) => CommitColor();
        combo.Leave += (_, _) => CommitColor();

        panel.Controls.Add(combo);
        panel.Controls.Add(pick);
        AddRow(label, panel);
    }
}
