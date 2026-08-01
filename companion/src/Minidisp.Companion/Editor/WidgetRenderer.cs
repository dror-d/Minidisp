using System.Drawing.Drawing2D;

namespace Minidisp.Companion.Editor;

/// <summary>
/// GDI+ renderer replicating the firmware's widget rules
/// (firmware/src/theme/widgets.cpp) so the canvas preview matches the device:
/// per-mille coordinates, anchor translation, screen-class font mapping,
/// 270° arcs from 135°, 40%-opacity gauge backgrounds, warn thresholds.
/// Fonts are approximated with Segoe UI at the firmware's pixel sizes.
/// </summary>
public static class WidgetRenderer
{
    private static readonly Dictionary<float, Font> FontCache = new();
    private static readonly Dictionary<string, Image?> ImageCache = new();

    public static void ClearImageCache()
    {
        foreach (var img in ImageCache.Values) img?.Dispose();
        ImageCache.Clear();
    }

    // ---- layout primitives (mirror pmX/pmY/pmR) ---------------------------

    private static int PmX(int pm, Size s) => pm * s.Width / 1000;
    private static int PmY(int pm, Size s) => pm * s.Height / 1000;
    private static int PmR(int pm, Size s) => pm * Math.Min(s.Width, s.Height) / 1000;

    /// <summary>Firmware font mapping: sm/md/lg/xl px per screen class.</summary>
    public static Font GetFont(string? size, Size screen)
    {
        var px = (screen.Height <= 240, size) switch
        {
            (true, "sm") => 12f, (true, "lg") => 20f, (true, "xl") => 28f, (true, _) => 14f,
            (false, "sm") => 14f, (false, "lg") => 24f, (false, "xl") => 36f, (false, _) => 16f,
        };
        if (!FontCache.TryGetValue(px, out var font))
            FontCache[px] = font = new Font("Segoe UI", px * 0.75f, FontStyle.Regular, GraphicsUnit.Point);
        return font;
    }

    public static Color ResolveColor(ThemeDocument doc, string? spec, string fallbackKey)
    {
        var s = string.IsNullOrEmpty(spec) ? doc.PaletteColor(fallbackKey)
            : spec.StartsWith('#') ? spec
            : doc.PaletteColor(spec);
        try { return ColorTranslator.FromHtml(s); }
        catch { return Color.Magenta; }
    }

    private static Color Alpha(Color c, int pct) =>
        Color.FromArgb(255 * pct / 100, c);

    /// <summary>Anchor offset: tl default; c/r → −50%/−100% of own size.</summary>
    private static Point Anchored(int x, int y, Size own, string? anchor)
    {
        if (anchor is not { Length: 2 }) return new Point(x, y);
        var dx = anchor[1] switch { 'c' => -own.Width / 2, 'r' => -own.Width, _ => 0 };
        var dy = anchor[0] switch { 'm' => -own.Height / 2, 'b' => -own.Height, _ => 0 };
        return new Point(x + dx, y + dy);
    }

    // ---- value formatting (port of widgets.cpp formatBind) ----------------

    public static string FormatBind(string? fmt, string? bind, IStatsProvider stats)
    {
        if (string.IsNullOrEmpty(fmt)) fmt = "{v:.0f}";
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < fmt.Length;)
        {
            if (fmt[i] == '{' && i + 1 < fmt.Length && fmt[i + 1] == 'v')
            {
                var close = fmt.IndexOf('}', i);
                if (close < 0) break;
                int precision = -1;
                if (i + 3 < fmt.Length && fmt[i + 2] == ':' && fmt[i + 3] == '.')
                {
                    var end = i + 4;
                    while (end < close && char.IsDigit(fmt[end])) end++;
                    _ = int.TryParse(fmt.AsSpan(i + 4, end - i - 4), out precision);
                }
                string valText;
                var b = bind ?? "";
                if (precision >= 0 && stats.TryGetNumber(b, out var v1))
                    valText = v1.ToString("F" + precision, System.Globalization.CultureInfo.InvariantCulture);
                else if (stats.TryGetText(b, out var t))
                    valText = t;
                else if (stats.TryGetNumber(b, out var v2))
                    valText = v2.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                else
                    valText = "--";
                result.Append(valText);
                i = close + 1;
            }
            else
            {
                result.Append(fmt[i++]);
            }
        }
        return result.ToString();
    }

    private static bool IsWarn(ThemeDocument doc, ThemeWidget w, IStatsProvider stats) =>
        w.Bind is { Length: > 0 } bind &&
        doc.WarnAbove is { } rules && rules.TryGetValue(bind, out var threshold) &&
        stats.TryGetNumber(bind, out var v) && v > threshold;

    // ---- bounds (shared by painting and canvas hit-testing) ---------------

    public static Rectangle GetBounds(ThemeWidget w, ThemeDocument doc, Size screen,
        Graphics g, IStatsProvider stats)
    {
        int x = PmX(w.X, screen), y = PmY(w.Y, screen);
        Size own;
        switch (w.Type)
        {
            case "bar":
                own = new Size(PmX(w.W ?? 300, screen), PmY(w.H ?? 40, screen));
                break;
            case "chart":
                own = new Size(PmX(w.W ?? 400, screen), PmY(w.H ?? 250, screen));
                break;
            case "rect":
                own = new Size(PmX(w.W ?? 100, screen), PmY(w.H ?? 100, screen));
                break;
            case "arc":
                var d = PmR(w.R ?? 200, screen) * 2;
                own = new Size(d, d);
                break;
            case "image":
                own = ImageSize(w, screen);
                break;
            default: // text
                var text = w.Bind is { Length: > 0 }
                    ? FormatBind(w.Fmt, w.Bind, stats)
                    : w.Text ?? "";
                var sz = g.MeasureString(text.Length == 0 ? " " : text, GetFont(w.Size, screen));
                own = new Size((int)Math.Ceiling(sz.Width), (int)Math.Ceiling(sz.Height));
                break;
        }
        var p = Anchored(x, y, own, w.Anchor);
        return new Rectangle(p, own);
    }

    private static Size ImageSize(ThemeWidget w, Size screen)
    {
        var img = LoadImage(w);
        if (img is null) return new Size(32, 32);
        var targetW = PmX(w.W ?? 0, screen);
        if (targetW <= 0) return img.Size;
        // lv_image_set_scale is uniform zoom based on width.
        return new Size(targetW, img.Height * targetW / Math.Max(1, img.Width));
    }

    /// <summary>Theme folder used to resolve image widget sources.</summary>
    public static string? ThemeDir { get; set; }

    private static Image? LoadImage(ThemeWidget w)
    {
        var src = w.Src ?? "logo.png";
        var key = $"{ThemeDir}|{src}";
        if (ImageCache.TryGetValue(key, out var cached)) return cached;
        Image? img = null;
        try
        {
            var path = ThemeDir is null ? null : Path.Combine(ThemeDir, src);
            if (path is not null && File.Exists(path))
                using (var stream = File.OpenRead(path))
                    img = Image.FromStream(stream);
        }
        catch (Exception)
        {
            // broken image — placeholder box is drawn instead
        }
        ImageCache[key] = img;
        return img;
    }

    // ---- painting ---------------------------------------------------------

    public static void DrawPage(Graphics g, ThemeDocument doc, int pageIndex,
        Size screen, IStatsProvider stats, ChartHistory history)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using (var bg = new SolidBrush(ResolveColor(doc, null, "bg")))
            g.FillRectangle(bg, 0, 0, screen.Width, screen.Height);

        if (pageIndex < 0 || pageIndex >= doc.Pages.Count) return;
        foreach (var w in doc.Pages[pageIndex].Widgets)
            DrawWidget(g, doc, w, screen, stats, history);
    }

    public static void DrawWidget(Graphics g, ThemeDocument doc, ThemeWidget w,
        Size screen, IStatsProvider stats, ChartHistory history)
    {
        var bounds = GetBounds(w, doc, screen, g, stats);
        var warn = IsWarn(doc, w, stats);
        var warnColor = ResolveColor(doc, null, "warn");

        switch (w.Type)
        {
            case "text": DrawText(g, doc, w, bounds, warn, warnColor, screen, stats); break;
            case "bar": DrawBar(g, doc, w, bounds, warn, warnColor, stats); break;
            case "arc": DrawArc(g, doc, w, bounds, warn, warnColor, screen, stats); break;
            case "chart": DrawChart(g, doc, w, bounds, history); break;
            case "image": DrawImage(g, doc, w, bounds); break;
            case "rect": DrawRect(g, doc, w, bounds); break;
        }
    }

    private static void DrawText(Graphics g, ThemeDocument doc, ThemeWidget w,
        Rectangle b, bool warn, Color warnColor, Size screen, IStatsProvider stats)
    {
        var text = w.Bind is { Length: > 0 } ? FormatBind(w.Fmt, w.Bind, stats) : w.Text ?? "";
        var color = warn ? warnColor : ResolveColor(doc, w.Color, "fg");
        using var brush = new SolidBrush(color);
        g.DrawString(text, GetFont(w.Size, screen), brush, b.Location);
    }

    private static void DrawBar(Graphics g, ThemeDocument doc, ThemeWidget w,
        Rectangle b, bool warn, Color warnColor, IStatsProvider stats)
    {
        float min = w.Min ?? 0, max = w.Max ?? 100;
        var color = warn ? warnColor : ResolveColor(doc, w.Color, "accent");
        var bgColor = Alpha(ResolveColor(doc, w.Bg, "muted"), 40);

        using var path = Rounded(b, 3);
        using (var bg = new SolidBrush(bgColor)) g.FillPath(bg, path);

        if (w.Bind is { Length: > 0 } && stats.TryGetNumber(w.Bind, out var v))
        {
            var frac = Math.Clamp((v - min) / Math.Max(1e-3f, max - min), 0, 1);
            var fill = new Rectangle(b.X, b.Y, Math.Max(1, (int)(b.Width * frac)), b.Height);
            using var fillPath = Rounded(fill, 3);
            using var fg = new SolidBrush(color);
            g.FillPath(fg, fillPath);
        }
    }

    private static void DrawArc(Graphics g, ThemeDocument doc, ThemeWidget w,
        Rectangle b, bool warn, Color warnColor, Size screen, IStatsProvider stats)
    {
        float min = w.Min ?? 0, max = w.Max ?? 100;
        var thickness = Math.Max(3, PmR(w.Thickness ?? 40, screen));
        var color = warn ? warnColor : ResolveColor(doc, w.Color, "accent");
        var bgColor = Alpha(ResolveColor(doc, w.Bg, "muted"), 40);

        var inner = Rectangle.Inflate(b, -thickness / 2, -thickness / 2);
        if (inner.Width <= 0 || inner.Height <= 0) return;

        // lv_arc_set_bg_angles(135, 45): 270° sweep clockwise from 135°.
        using (var pen = new Pen(bgColor, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(pen, inner, 135, 270);

        float value = min;
        var haveValue = w.Bind is { Length: > 0 } && stats.TryGetNumber(w.Bind, out value);
        if (haveValue)
        {
            var frac = Math.Clamp((value - min) / Math.Max(1e-3f, max - min), 0, 1);
            if (frac > 0.001f)
                using (var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, inner, 135, 270 * frac);
        }

        if (w.Label == true)
        {
            var text = haveValue ? value.ToString("F0") : "--";
            var font = GetFont(w.Size, screen);
            var sz = g.MeasureString(text, font);
            using var brush = new SolidBrush(ResolveColor(doc, null, "fg"));
            g.DrawString(text, font, brush,
                b.X + (b.Width - sz.Width) / 2, b.Y + (b.Height - sz.Height) / 2);
        }
    }

    private static void DrawChart(Graphics g, ThemeDocument doc, ThemeWidget w,
        Rectangle b, ChartHistory history)
    {
        float min = w.Min ?? 0, max = w.Max ?? 100;
        var points = Math.Max(2, w.Points ?? 60);
        var color = ResolveColor(doc, w.Color, "accent");
        var muted = ResolveColor(doc, null, "muted");

        var series = history.Get(w.Bind ?? "", points, min, max);
        if (w.Autoscale == true && series.Length > 0)
            max = Math.Max(1, series.Max() * 1.2f);

        using (var bg = new SolidBrush(Alpha(ResolveColor(doc, null, "bg"), 20)))
            g.FillRectangle(bg, b);

        // lv_chart_set_div_line_count(3, 4)
        using (var div = new Pen(Alpha(muted, 60), 1))
        {
            for (int i = 1; i <= 3; i++)
                g.DrawLine(div, b.X, b.Y + b.Height * i / 4, b.Right, b.Y + b.Height * i / 4);
            for (int i = 1; i <= 4; i++)
                g.DrawLine(div, b.X + b.Width * i / 5, b.Y, b.X + b.Width * i / 5, b.Bottom);
        }
        using (var border = new Pen(muted, 1)) g.DrawRectangle(border, b);

        if (series.Length >= 2)
        {
            var pts = new PointF[series.Length];
            for (int i = 0; i < series.Length; i++)
            {
                var frac = Math.Clamp((series[i] - min) / Math.Max(1e-3f, max - min), 0, 1);
                pts[i] = new PointF(
                    b.X + (float)b.Width * i / (series.Length - 1),
                    b.Bottom - b.Height * frac);
            }
            using var pen = new Pen(color, 2);
            var clip = g.Clip;
            g.SetClip(b);
            g.DrawLines(pen, pts);
            g.Clip = clip;
        }
    }

    private static void DrawImage(Graphics g, ThemeDocument doc, ThemeWidget w, Rectangle b)
    {
        var img = LoadImage(w);
        if (img is null)
        {
            using var pen = new Pen(ResolveColor(doc, null, "muted"), 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, b);
            using var brush = new SolidBrush(ResolveColor(doc, null, "muted"));
            g.DrawString("img", new Font("Segoe UI", 8), brush, b.Location);
            return;
        }
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, b);
    }

    private static void DrawRect(Graphics g, ThemeDocument doc, ThemeWidget w, Rectangle b)
    {
        using var path = Rounded(b, w.Radius ?? 4);
        using var brush = new SolidBrush(ResolveColor(doc, w.Color, "muted"));
        g.FillPath(brush, path);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || r.Width < radius * 2 || r.Height < radius * 2)
        {
            path.AddRectangle(r);
            return path;
        }
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
