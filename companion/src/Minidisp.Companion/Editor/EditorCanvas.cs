using System.Drawing.Drawing2D;

namespace Minidisp.Companion.Editor;

/// <summary>
/// Design canvas: renders the theme at native device resolution into a bitmap,
/// scales it up for editing, and handles selection / drag-move / resize with
/// per-mille coordinates and optional grid snapping.
/// </summary>
public sealed class EditorCanvas : Control
{
    private const int GridPm = 10; // snap grid in per-mille

    public ThemeDocument? Document { get; set; }
    public int PageIndex { get; set; }
    public Size ScreenSize { get; set; } = new(320, 240);
    public bool SnapToGrid { get; set; } = true;
    public IStatsProvider Stats { get; set; } = new SampleStats();
    public ChartHistory History { get; } = new();

    public int SelectedIndex { get; private set; } = -1;

    public event EventHandler? SelectionChanged;
    /// <summary>Raised when a drag/resize/nudge finished changing the document.</summary>
    public event EventHandler? DocumentEdited;
    /// <summary>Raised before an interactive mutation starts (undo snapshot hook).</summary>
    public event EventHandler? BeforeEdit;

    private float _zoom = 1;
    private Point _origin;
    private bool _dragging, _resizing, _moved;
    private Point _dragStartPx;
    private int _startX, _startY, _startW, _startH, _startR;

    public ThemeWidget? SelectedWidget =>
        Document is { } d && PageIndex < d.Pages.Count && SelectedIndex >= 0 &&
        SelectedIndex < d.Pages[PageIndex].Widgets.Count
            ? d.Pages[PageIndex].Widgets[SelectedIndex]
            : null;

    public EditorCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(28, 30, 34);
    }

    public void Select(int index)
    {
        if (index == SelectedIndex) return;
        SelectedIndex = index;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        if (Document is null || Document.Pages.Count == 0)
        {
            TextRenderer.DrawText(g, "No theme loaded", Font, ClientRectangle,
                Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        _zoom = Math.Max(0.5f, Math.Min(
            (ClientSize.Width - 48f) / ScreenSize.Width,
            (ClientSize.Height - 48f) / ScreenSize.Height));
        var w = (int)(ScreenSize.Width * _zoom);
        var h = (int)(ScreenSize.Height * _zoom);
        _origin = new Point((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2);

        // Device frame
        g.FillRectangle(Brushes.Black, _origin.X - 8, _origin.Y - 8, w + 16, h + 16);
        g.DrawRectangle(Pens.DimGray, _origin.X - 8, _origin.Y - 8, w + 16, h + 16);

        // Native-resolution render, scaled up
        using var bmp = new Bitmap(ScreenSize.Width, ScreenSize.Height);
        using (var bg = Graphics.FromImage(bmp))
            WidgetRenderer.DrawPage(bg, Document, PageIndex, ScreenSize, Stats, History);
        g.InterpolationMode = _zoom >= 2 ? InterpolationMode.NearestNeighbor
                                         : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bmp, new Rectangle(_origin, new Size(w, h)));

        DrawSelection(g);
    }

    private void DrawSelection(Graphics g)
    {
        if (SelectedWidget is not { } widget || Document is null) return;
        var bounds = WidgetBoundsPx(widget);
        using var pen = new Pen(Color.OrangeRed, 1.5f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4);
        if (IsResizable(widget))
        {
            var handle = ResizeHandlePx(bounds);
            g.FillRectangle(Brushes.OrangeRed, handle);
        }
    }

    private static bool IsResizable(ThemeWidget w) =>
        w.Type is "bar" or "chart" or "rect" or "arc" or "image";

    private Rectangle WidgetBoundsPx(ThemeWidget w)
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        var native = WidgetRenderer.GetBounds(w, Document!, ScreenSize, g, Stats);
        return new Rectangle(
            _origin.X + (int)(native.X * _zoom),
            _origin.Y + (int)(native.Y * _zoom),
            (int)(native.Width * _zoom),
            (int)(native.Height * _zoom));
    }

    private static Rectangle ResizeHandlePx(Rectangle bounds) =>
        new(bounds.Right - 4, bounds.Bottom - 4, 9, 9);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Document is null || PageIndex >= Document.Pages.Count) return;
        var widgets = Document.Pages[PageIndex].Widgets;

        if (SelectedWidget is { } sel && IsResizable(sel) &&
            ResizeHandlePx(WidgetBoundsPx(sel)).Contains(e.Location))
        {
            StartDrag(e.Location, resizing: true);
            return;
        }

        for (int i = widgets.Count - 1; i >= 0; i--)
        {
            var bounds = WidgetBoundsPx(widgets[i]);
            bounds.Inflate(3, 3);
            if (bounds.Contains(e.Location))
            {
                Select(i);
                StartDrag(e.Location, resizing: false);
                return;
            }
        }
        Select(-1);
    }

    private void StartDrag(Point at, bool resizing)
    {
        var w = SelectedWidget!;
        _dragging = !resizing;
        _resizing = resizing;
        _moved = false;
        _dragStartPx = at;
        (_startX, _startY) = (w.X, w.Y);
        (_startW, _startH, _startR) = (w.W ?? 0, w.H ?? 0, w.R ?? 0);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (SelectedWidget is not { } w || (!_dragging && !_resizing)) return;

        var dxPm = (int)((e.X - _dragStartPx.X) / _zoom * 1000 / ScreenSize.Width);
        var dyPm = (int)((e.Y - _dragStartPx.Y) / _zoom * 1000 / ScreenSize.Height);
        if (dxPm == 0 && dyPm == 0) return;

        if (!_moved)
        {
            _moved = true;
            BeforeEdit?.Invoke(this, EventArgs.Empty);
        }

        if (_dragging)
        {
            w.X = Snap(Math.Clamp(_startX + dxPm, 0, 1000));
            w.Y = Snap(Math.Clamp(_startY + dyPm, 0, 1000));
        }
        else if (w.Type == "arc")
        {
            w.R = Math.Clamp(Snap(_startR + Math.Max(dxPm, dyPm) / 2), 20, 500);
        }
        else if (w.Type == "image")
        {
            w.W = Math.Clamp(Snap(_startW + dxPm), 0, 1000);
        }
        else
        {
            w.W = Math.Clamp(Snap(_startW + dxPm), 10, 1000);
            w.H = Math.Clamp(Snap(_startH + dyPm), 10, 1000);
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if ((_dragging || _resizing) && _moved)
            DocumentEdited?.Invoke(this, EventArgs.Empty);
        _dragging = _resizing = _moved = false;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (SelectedWidget is not { } w) return;
        var step = e.Shift ? 1 : GridPm;
        var (dx, dy) = e.KeyCode switch
        {
            Keys.Left => (-step, 0), Keys.Right => (step, 0),
            Keys.Up => (0, -step), Keys.Down => (0, step),
            _ => (0, 0),
        };
        if (dx == 0 && dy == 0) return;
        BeforeEdit?.Invoke(this, EventArgs.Empty);
        w.X = Math.Clamp(w.X + dx, 0, 1000);
        w.Y = Math.Clamp(w.Y + dy, 0, 1000);
        DocumentEdited?.Invoke(this, EventArgs.Empty);
        Invalidate();
        e.Handled = true;
    }

    private int Snap(int pm) => SnapToGrid ? (pm + GridPm / 2) / GridPm * GridPm : pm;
}
