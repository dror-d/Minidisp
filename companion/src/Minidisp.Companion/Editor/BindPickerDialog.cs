using Minidisp.Companion.Models;

namespace Minidisp.Companion.Editor;

/// <summary>
/// Friendly data-source picker: a categorized tree of everything a widget can
/// bind to — sensor fields with readable names plus the custom XML value ids
/// present in the current snapshot — with static text, format, and text-size
/// editing and a live preview of the rendered result.
/// </summary>
public sealed class BindPickerDialog : Form
{
    public sealed record Result(string? Bind, string? Fmt, string? StaticText, string? Size);

    private static readonly string[] SizeOptions =
        ["sm", "md", "lg", "xl", "12", "14", "16", "20", "24", "28", "36"];

    private readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false,
        BorderStyle = BorderStyle.FixedSingle,
    };
    private readonly TextBox _bind = new() { Dock = DockStyle.Fill };
    private readonly TextBox _static = new() { Dock = DockStyle.Fill };
    private readonly TextBox _fmt = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _size = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Label _preview = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Color.FromArgb(0, 130, 170),
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
    };
    private readonly IStatsProvider _previewStats;
    private readonly bool _isText;
    private int _fieldRow;
    private TableLayoutPanel _fields = null!;

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
        ClientSize = new Size(520, isText ? 680 : 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 12, 14, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // intro
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // tree
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // fields
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons

        root.Controls.Add(new Label
        {
            Text = isText
                ? "Pick a data field to show live values, or leave the bind empty for a static message:"
                : "Pick the data field this widget displays:",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        }, 0, 0);

        root.Controls.Add(_tree, 0, 1);

        _fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 10, 0, 0),
        };
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField("Bind path", _bind);
        if (isText)
        {
            AddHint("Filled from the tree — or type a custom XML value id. Empty = static text.");
            AddField("Static text", _static);
            AddField("Format", _fmt);
            AddHint("{v} inserts the value; {v:.1f} = 1 decimal. Around it is literal: \"CPU {v:.0f}%\"");
            _size.Items.AddRange(SizeOptions);
            AddField("Text size", _size);
            AddHint("sm / md / lg / xl, or a pixel size 12–36 (snapped to device fonts).");
        }
        AddField("Preview", _preview);
        root.Controls.Add(_fields, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 88 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 88 };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);

        PopulateTree(snapshot);
        _bind.Text = widget.Bind ?? "";
        _static.Text = widget.Text ?? "";
        _fmt.Text = widget.Fmt ?? "{v:.0f}";
        _size.Text = widget.Size ?? "md";

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

    private void AddField(string label, Control control)
    {
        _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _fields.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 6, 0),
        }, 0, _fieldRow);
        control.Margin = new Padding(0, 4, 0, 0);
        if (control is Label) control.Height = 30;
        _fields.Controls.Add(control, 1, _fieldRow);
        _fieldRow++;
    }

    private void AddHint(string text)
    {
        _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _fields.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.75f),
            Margin = new Padding(0, 1, 0, 3),
            MaximumSize = new Size(390, 0),
        }, 1, _fieldRow);
        _fieldRow++;
    }

    private void PopulateTree(StatsSnapshot? snap)
    {
        _tree.BeginUpdate();

        if (_isText)
        {
            _tree.Nodes.Add(new TreeNode("Static text (no data bind)") { Tag = "" });
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

        var xml = _tree.Nodes.Add("Custom values (from XML)");
        if (snap?.Custom is { Count: > 0 } custom)
        {
            foreach (var kv in custom)
                Add(xml, $"{kv.Key}   (now: {kv.Value})", kv.Key);
            xml.Expand();
        }
        else
        {
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
            ? (_isText ? _static.Text : "(no bind selected)")
            : WidgetRenderer.FormatBind(
                string.IsNullOrWhiteSpace(_fmt.Text) ? "{v:.0f}" : _fmt.Text, bind, _previewStats);
        _preview.Text = text.Length == 0 ? "(empty)" : text;
    }

    private Result BuildResult()
    {
        var bind = _bind.Text.Trim();
        var size = _size.Text.Trim();
        return new Result(
            bind.Length == 0 ? null : bind,
            string.IsNullOrWhiteSpace(_fmt.Text) || !_isText ? null : _fmt.Text,
            string.IsNullOrWhiteSpace(_static.Text) ? null : _static.Text,
            !_isText || size.Length == 0 || size == "md" ? null : size);
    }
}
