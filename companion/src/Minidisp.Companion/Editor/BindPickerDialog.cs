using Minidisp.Companion.Models;

namespace Minidisp.Companion.Editor;

/// <summary>
/// Friendly data-source picker: a categorized tree of everything a widget can
/// bind to — sensor fields with readable names plus the custom XML value ids
/// present in the current snapshot — with format editing and a live preview.
/// </summary>
public sealed class BindPickerDialog : Form
{
    public sealed record Result(string? Bind, string? Fmt, string? StaticText);

    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly TextBox _bind = new() { Dock = DockStyle.Fill };
    private readonly TextBox _static = new() { Dock = DockStyle.Fill };
    private readonly TextBox _fmt = new() { Dock = DockStyle.Fill };
    private readonly Label _preview = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Color.DarkCyan,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly IStatsProvider _previewStats;
    private readonly bool _isText;

    /// <summary>Opens the picker. Returns null when cancelled.</summary>
    public static Result? Pick(IWin32Window owner, ThemeWidget widget, bool isText,
        StatsSnapshot? snapshot, IStatsProvider previewStats)
    {
        using var dialog = new BindPickerDialog(widget, isText, snapshot, previewStats);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.BuildResult() : null;
    }

    private BindPickerDialog(ThemeWidget widget, bool isText, StatsSnapshot? snapshot,
        IStatsProvider previewStats)
    {
        _previewStats = previewStats;
        _isText = isText;

        Text = isText ? "Text content" : "Data source";
        Size = new Size(460, isText ? 620 : 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(10),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var treeLabel = new Label { Text = "Data", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        table.Controls.Add(treeLabel, 0, 0);
        table.Controls.Add(_tree, 1, 0);

        AddRow(table, "Bind path", _bind,
            "The raw path — pick from the tree above or type a custom XML value id.");
        if (isText)
        {
            AddRow(table, "Static text", _static,
                "Shown when no bind is set (plain message on the display).");
            AddRow(table, "Format", _fmt,
                "{v} inserts the value; {v:.1f} = 1 decimal. Text around it is literal, e.g. \"CPU {v:.0f}%\".");
        }
        AddRow(table, "Preview", _preview, null);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 42,
            Padding = new Padding(8),
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(table);
        Controls.Add(buttons);

        PopulateTree(snapshot);
        _bind.Text = widget.Bind ?? "";
        _static.Text = widget.Text ?? "";
        _fmt.Text = widget.Fmt ?? "{v:.0f}";

        _tree.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is string path)
            {
                _bind.Text = path;
                UpdatePreview();
            }
        };
        _bind.TextChanged += (_, _) => UpdatePreview();
        _static.TextChanged += (_, _) => UpdatePreview();
        _fmt.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control, string? hint)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        table.Controls.Add(control);
        if (hint is null) return;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var hintLabel = new Label
        {
            Text = hint,
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5f),
            Margin = new Padding(0, 0, 0, 4),
            MaximumSize = new Size(320, 0),
        };
        table.Controls.Add(new Label(), 0, table.RowCount);
        table.Controls.Add(hintLabel);
    }

    private void PopulateTree(StatsSnapshot? snap)
    {
        _tree.BeginUpdate();

        if (_isText)
        {
            var none = _tree.Nodes.Add("Static text (no data bind)");
            none.Tag = "";
        }

        var cpu = _tree.Nodes.Add("CPU");
        Add(cpu, "Load (%)", "cpu.load");
        Add(cpu, "Temperature (°C)", "cpu.temp");
        Add(cpu, "Frequency (GHz)", "cpu.freq");
        Add(cpu, "Name", "cpu.name");
        var coreCount = Math.Clamp(snap?.Cpu?.Cores?.Count ?? 8, 0, 16);
        for (int i = 0; i < coreCount; i++)
            Add(cpu, $"Core {i} load (%)", $"cpu.core{i}");

        var gpu = _tree.Nodes.Add("GPU");
        Add(gpu, "Load (%)", "gpu.load");
        Add(gpu, "Temperature (°C)", "gpu.temp");
        Add(gpu, "Name", "gpu.name");

        var mem = _tree.Nodes.Add("Memory");
        Add(mem, "Usage (%)", "mem.pct");
        Add(mem, "Used (GB)", "mem.used");
        Add(mem, "Total (GB)", "mem.total");

        var net = _tree.Nodes.Add("Network");
        if (snap?.Net is { Count: > 0 } nets)
        {
            for (int i = 0; i < nets.Count; i++)
            {
                var prefix = i == 0 ? "net" : $"net{i}";
                var name = nets[i].Interface ?? prefix;
                Add(net, $"{name}: IP address", $"{prefix}.ip");
                Add(net, $"{name}: download (Mbps)", $"{prefix}.down");
                Add(net, $"{name}: upload (Mbps)", $"{prefix}.up");
                Add(net, $"{name}: interface name", $"{prefix}.if");
            }
        }
        else
        {
            Add(net, "IP address", "net.ip");
            Add(net, "Download (Mbps)", "net.down");
            Add(net, "Upload (Mbps)", "net.up");
            Add(net, "Interface name", "net.if");
        }

        var disk = _tree.Nodes.Add("Disk");
        if (snap?.Disk is { Count: > 0 } disks)
        {
            for (int i = 0; i < disks.Count; i++)
            {
                var prefix = i == 0 ? "disk" : $"disk{i}";
                var name = disks[i].Name ?? prefix;
                Add(disk, $"{name} usage (%)", $"{prefix}.pct");
                Add(disk, $"{name} free (GB)", $"{prefix}.free");
                Add(disk, $"{name} label", $"{prefix}.n");
            }
        }
        else
        {
            Add(disk, "C: usage (%)", "disk.pct");
            Add(disk, "C: free (GB)", "disk.free");
        }

        var system = _tree.Nodes.Add("System");
        Add(system, "Host name", "host");
        Add(system, "Uptime", "uptime");

        if (snap?.Custom is { Count: > 0 } custom)
        {
            var xml = _tree.Nodes.Add("Custom values (from XML)");
            foreach (var kv in custom)
                Add(xml, $"{kv.Key}   (now: {kv.Value})", kv.Key);
            xml.Expand();
        }
        else
        {
            var xml = _tree.Nodes.Add("Custom values (from XML)");
            xml.Nodes.Add(new TreeNode(
                "None available — switch the source to an XML file with <value id=\"...\"> entries")
            { ForeColor = Color.Gray });
        }

        _tree.EndUpdate();
    }

    private static void Add(TreeNode parent, string label, string path) =>
        parent.Nodes.Add(new TreeNode(label) { Tag = path });

    private void UpdatePreview()
    {
        var bind = _bind.Text.Trim();
        var text = bind.Length == 0
            ? (_isText ? _static.Text : "(no bind)")
            : WidgetRenderer.FormatBind(
                string.IsNullOrWhiteSpace(_fmt.Text) ? "{v:.0f}" : _fmt.Text, bind, _previewStats);
        _preview.Text = text;
    }

    private Result BuildResult()
    {
        var bind = _bind.Text.Trim();
        return new Result(
            bind.Length == 0 ? null : bind,
            string.IsNullOrWhiteSpace(_fmt.Text) || !_isText ? null : _fmt.Text,
            string.IsNullOrWhiteSpace(_static.Text) ? null : _static.Text);
    }
}
