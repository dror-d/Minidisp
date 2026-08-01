using Microsoft.Extensions.Logging;
using Minidisp.Companion.Services;

namespace Minidisp.Companion;

/// <summary>
/// Tray-only application context: NotifyIcon with mode switching (live
/// sensors / XML file), device theme + brightness commands, and status.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;
    private readonly Settings _settings;
    private readonly NotifyIcon _icon;
    private readonly SerialSender _sender;

    private ISensorSource _source;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _liveItem;
    private readonly ToolStripMenuItem _xmlItem;
    private readonly ToolStripMenuItem _themesMenu;

    public TrayApp(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<TrayApp>();
        _settings = Settings.Load();
        _source = CreateSource(_settings.Mode);

        _statusItem = new ToolStripMenuItem("Searching for device...") { Enabled = false };
        _liveItem = new ToolStripMenuItem("Source: Live sensors", null, (_, _) => SwitchMode(SourceMode.Live));
        _xmlItem = new ToolStripMenuItem("Source: XML file", null, (_, _) => SwitchMode(SourceMode.XmlFile));
        _themesMenu = new ToolStripMenuItem("Device theme") { Enabled = false };

        var brightnessMenu = new ToolStripMenuItem("Brightness");
        foreach (int pct in new[] { 25, 50, 75, 100 })
        {
            // _sender is assigned later in this constructor, before any click can fire.
            brightnessMenu.DropDownItems.Add(new ToolStripMenuItem($"{pct}%", null,
                (_, _) => _sender!.SendCommand(new { cmd = "brightness", v = pct })));
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_liveItem);
        menu.Items.Add(_xmlItem);
        menu.Items.Add(new ToolStripMenuItem("Choose XML file...", null, (_, _) => ChooseXmlFile()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_themesMenu);
        menu.Items.Add(brightnessMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

        _icon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Minidisp Companion",
            ContextMenuStrip = menu,
            Visible = true,
        };

        UpdateModeChecks();

        _sender = new SerialSender(
            loggerFactory.CreateLogger<SerialSender>(),
            () => _source,
            _settings.UpdateHz);
        _sender.StatusChanged += status => RunOnUi(() =>
        {
            _statusItem.Text = status;
            _icon.Text = Truncate($"Minidisp — {status}", 63);
        });
        _sender.DeviceConnected += device => RunOnUi(() => PopulateThemes(device));
    }

    private void RunOnUi(Action action)
    {
        var strip = _icon.ContextMenuStrip;
        if (strip is not null && strip.IsHandleCreated) strip.BeginInvoke(action);
        else action();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private ISensorSource CreateSource(SourceMode mode)
    {
        if (mode == SourceMode.XmlFile && !string.IsNullOrWhiteSpace(_settings.XmlPath))
        {
            return new XmlFileSource(
                _loggerFactory.CreateLogger<XmlFileSource>(), _settings.XmlPath);
        }
        return new LiveSensorSource(_loggerFactory.CreateLogger<LiveSensorSource>());
    }

    private void SwitchMode(SourceMode mode)
    {
        if (mode == SourceMode.XmlFile && string.IsNullOrWhiteSpace(_settings.XmlPath))
        {
            ChooseXmlFile();
            if (string.IsNullOrWhiteSpace(_settings.XmlPath)) return;
        }
        _settings.Mode = mode;
        _settings.Save();

        var old = _source;
        _source = CreateSource(mode);
        old.Dispose();
        _log.LogInformation("Switched source to {Source}", _source.Name);
        UpdateModeChecks();
    }

    private void ChooseXmlFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the auto-updating XML file",
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        _settings.XmlPath = dialog.FileName;
        _settings.Save();
        if (_settings.Mode == SourceMode.XmlFile) SwitchMode(SourceMode.XmlFile);
    }

    private void UpdateModeChecks()
    {
        _liveItem.Checked = _settings.Mode == SourceMode.Live;
        _xmlItem.Checked = _settings.Mode == SourceMode.XmlFile;
        _xmlItem.Text = _settings.XmlPath is null
            ? "Source: XML file"
            : $"Source: XML file ({Path.GetFileName(_settings.XmlPath)})";
    }

    private void PopulateThemes(SerialSender.DeviceInfo device)
    {
        _themesMenu.DropDownItems.Clear();
        foreach (var theme in device.Themes)
        {
            var item = new ToolStripMenuItem(theme, null,
                (_, _) => _sender.SendCommand(new { cmd = "theme", name = theme }))
            {
                Checked = theme == device.CurrentTheme,
            };
            _themesMenu.DropDownItems.Add(item);
        }
        _themesMenu.Enabled = _themesMenu.DropDownItems.Count > 0;
    }

    /// <summary>Simple generated 16x16 icon: dark tile with a cyan bar chart.</summary>
    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(16, 20, 24));
            using var accent = new SolidBrush(Color.FromArgb(0, 200, 255));
            g.FillRectangle(accent, 2, 9, 3, 5);
            g.FillRectangle(accent, 6, 5, 3, 9);
            g.FillRectangle(accent, 10, 7, 3, 7);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _sender.Dispose();
        _source.Dispose();
        base.ExitThreadCore();
    }
}
