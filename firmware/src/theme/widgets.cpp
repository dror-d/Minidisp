#include "widgets.h"

#include <Arduino.h>

#include "../data/stats_model.h"

namespace widgets {

namespace {

enum class WType : uint8_t { Text, Bar, Arc, Chart };

struct Bound {
    lv_obj_t* obj = nullptr;
    WType type = WType::Text;
    char bind[24] = "";
    char fmt[48] = "";
    float min = 0, max = 100;
    lv_color_t color, warnColor;
    float warnAbove = 0;
    bool hasWarn = false;
    // chart extras
    lv_chart_series_t* series = nullptr;
    bool autoscale = false;
    float maxSeen = 1;
    // arc center label
    lv_obj_t* valueLabel = nullptr;
};

constexpr int kMaxBound = 80;
Bound s_bound[kMaxBound];
int s_nBound = 0;

// --- helpers ---------------------------------------------------------------

int32_t pmX(const ThemeCtx& ctx, int32_t pm) { return (int32_t)pm * ctx.screenW / 1000; }
int32_t pmY(const ThemeCtx& ctx, int32_t pm) { return (int32_t)pm * ctx.screenH / 1000; }
int32_t pmR(const ThemeCtx& ctx, int32_t pm) {
    uint16_t d = ctx.screenW < ctx.screenH ? ctx.screenW : ctx.screenH;
    return (int32_t)pm * d / 1000;
}

lv_color_t parseColor(const ThemeCtx& ctx, const char* s, lv_color_t fallback) {
    if (!s || !*s) return fallback;
    if (s[0] == '#') return lv_color_hex((uint32_t)strtoul(s + 1, nullptr, 16));
    if (!strcmp(s, "bg")) return ctx.bg;
    if (!strcmp(s, "fg")) return ctx.fg;
    if (!strcmp(s, "accent")) return ctx.accent;
    if (!strcmp(s, "accent2")) return ctx.accent2;
    if (!strcmp(s, "muted")) return ctx.muted;
    if (!strcmp(s, "warn")) return ctx.warn;
    return fallback;
}

const lv_font_t* parseFont(const ThemeCtx& ctx, const char* size) {
    if (!size) return ctx.fonts[1];
    if (!strcmp(size, "sm")) return ctx.fonts[0];
    if (!strcmp(size, "lg")) return ctx.fonts[2];
    if (!strcmp(size, "xl")) return ctx.fonts[3];
    return ctx.fonts[1];
}

// Anchors move the widget's reference point using self-relative translation.
void applyAnchor(lv_obj_t* obj, const char* anchor) {
    if (!anchor || strlen(anchor) != 2) return;
    int32_t tx = 0, ty = 0;
    if (anchor[1] == 'c') tx = -50;
    else if (anchor[1] == 'r') tx = -100;
    if (anchor[0] == 'm') ty = -50;
    else if (anchor[0] == 'b') ty = -100;
    if (tx) lv_obj_set_style_translate_x(obj, lv_pct(tx), 0);
    if (ty) lv_obj_set_style_translate_y(obj, lv_pct(ty), 0);
}

void makePassive(lv_obj_t* obj) {
    lv_obj_remove_flag(obj, LV_OBJ_FLAG_CLICKABLE);
    lv_obj_remove_flag(obj, LV_OBJ_FLAG_SCROLLABLE);
}

float warnThreshold(const ThemeCtx& ctx, const char* bind, bool& has) {
    for (int i = 0; i < ctx.nWarns; i++) {
        if (!strcmp(ctx.warns[i].path, bind)) {
            has = true;
            return ctx.warns[i].above;
        }
    }
    has = false;
    return 0;
}

Bound* newBound(lv_obj_t* obj, WType type, const char* bind, const ThemeCtx& ctx,
                lv_color_t color) {
    if (s_nBound >= kMaxBound) return nullptr;
    Bound& b = s_bound[s_nBound++];
    b = Bound{};
    b.obj = obj;
    b.type = type;
    strlcpy(b.bind, bind ? bind : "", sizeof(b.bind));
    b.color = color;
    b.warnColor = ctx.warn;
    b.warnAbove = warnThreshold(ctx, b.bind, b.hasWarn);
    return &b;
}

// Render "CPU {v:.0f}%" style format strings against the stats model.
void formatBind(const char* fmt, const char* bind, char* out, size_t outLen) {
    if (!fmt || !*fmt) fmt = "{v:.0f}";
    size_t o = 0;
    for (const char* p = fmt; *p && o < outLen - 1;) {
        if (p[0] == '{' && p[1] == 'v') {
            int precision = -1;
            const char* close = strchr(p, '}');
            if (!close) break;
            if (p[2] == ':' && p[3] == '.') {
                precision = atoi(p + 4);
            }
            char valBuf[48];
            float v;
            if (precision >= 0 && stats::getNumber(bind, v)) {
                snprintf(valBuf, sizeof(valBuf), "%.*f", precision, v);
            } else if (stats::getText(bind, valBuf, sizeof(valBuf))) {
                // ok
            } else if (stats::getNumber(bind, v)) {
                snprintf(valBuf, sizeof(valBuf), "%.0f", v);
            } else {
                strlcpy(valBuf, "--", sizeof(valBuf));
            }
            for (const char* q = valBuf; *q && o < outLen - 1; q++) out[o++] = *q;
            p = close + 1;
        } else {
            out[o++] = *p++;
        }
    }
    out[o] = 0;
}

// --- creators --------------------------------------------------------------

void createText(lv_obj_t* parent, JsonObjectConst w, const ThemeCtx& ctx) {
    lv_obj_t* label = lv_label_create(parent);
    makePassive(label);
    lv_obj_set_pos(label, pmX(ctx, w["x"] | 0), pmY(ctx, w["y"] | 0));
    applyAnchor(label, w["anchor"]);
    lv_obj_set_style_text_font(label, parseFont(ctx, w["size"]), 0);
    lv_color_t color = parseColor(ctx, w["color"], ctx.fg);
    lv_obj_set_style_text_color(label, color, 0);

    const char* bind = w["bind"];
    if (bind && *bind) {
        Bound* b = newBound(label, WType::Text, bind, ctx, color);
        if (b) {
            strlcpy(b->fmt, w["fmt"] | "{v:.0f}", sizeof(b->fmt));
            char buf[64];
            formatBind(b->fmt, b->bind, buf, sizeof(buf));
            lv_label_set_text(label, buf);
        }
    } else {
        lv_label_set_text(label, w["text"] | "");
    }
}

void createBar(lv_obj_t* parent, JsonObjectConst w, const ThemeCtx& ctx) {
    lv_obj_t* bar = lv_bar_create(parent);
    makePassive(bar);
    lv_obj_set_pos(bar, pmX(ctx, w["x"] | 0), pmY(ctx, w["y"] | 0));
    lv_obj_set_size(bar, pmX(ctx, w["w"] | 300), pmY(ctx, w["h"] | 40));
    applyAnchor(bar, w["anchor"]);

    lv_color_t color = parseColor(ctx, w["color"], ctx.accent);
    lv_obj_set_style_bg_color(bar, parseColor(ctx, w["bg"], ctx.muted), LV_PART_MAIN);
    lv_obj_set_style_bg_opa(bar, LV_OPA_40, LV_PART_MAIN);
    lv_obj_set_style_bg_color(bar, color, LV_PART_INDICATOR);
    lv_obj_set_style_radius(bar, 3, LV_PART_MAIN);
    lv_obj_set_style_radius(bar, 3, LV_PART_INDICATOR);

    int min = w["min"] | 0, max = w["max"] | 100;
    lv_bar_set_range(bar, min, max);

    const char* bind = w["bind"];
    Bound* b = newBound(bar, WType::Bar, bind, ctx, color);
    if (b) {
        b->min = min;
        b->max = max;
    }
}

void createArc(lv_obj_t* parent, JsonObjectConst w, const ThemeCtx& ctx) {
    lv_obj_t* arc = lv_arc_create(parent);
    makePassive(arc);
    int32_t r = pmR(ctx, w["r"] | 200);
    lv_obj_set_size(arc, r * 2, r * 2);
    lv_obj_set_pos(arc, pmX(ctx, w["x"] | 0), pmY(ctx, w["y"] | 0));
    applyAnchor(arc, w["anchor"]);

    lv_arc_set_bg_angles(arc, 135, 45); // 270° sweep
    lv_arc_set_rotation(arc, 0);
    int min = w["min"] | 0, max = w["max"] | 100;
    lv_arc_set_range(arc, min, max);
    lv_arc_set_value(arc, min);
    lv_obj_remove_style(arc, nullptr, LV_PART_KNOB);

    int32_t thickness = pmR(ctx, w["thickness"] | 40);
    if (thickness < 3) thickness = 3;
    lv_color_t color = parseColor(ctx, w["color"], ctx.accent);
    lv_obj_set_style_arc_width(arc, thickness, LV_PART_MAIN);
    lv_obj_set_style_arc_width(arc, thickness, LV_PART_INDICATOR);
    lv_obj_set_style_arc_color(arc, parseColor(ctx, w["bg"], ctx.muted), LV_PART_MAIN);
    lv_obj_set_style_arc_opa(arc, LV_OPA_40, LV_PART_MAIN);
    lv_obj_set_style_arc_color(arc, color, LV_PART_INDICATOR);

    Bound* b = newBound(arc, WType::Arc, w["bind"], ctx, color);
    if (b) {
        b->min = min;
        b->max = max;
        if (w["label"] | false) {
            lv_obj_t* label = lv_label_create(arc);
            makePassive(label);
            lv_obj_center(label);
            lv_obj_set_style_text_font(label, parseFont(ctx, w["size"]), 0);
            lv_obj_set_style_text_color(label, ctx.fg, 0);
            lv_label_set_text(label, "--");
            b->valueLabel = label;
        }
    }
}

void createChart(lv_obj_t* parent, JsonObjectConst w, const ThemeCtx& ctx) {
    lv_obj_t* chart = lv_chart_create(parent);
    makePassive(chart);
    lv_obj_set_pos(chart, pmX(ctx, w["x"] | 0), pmY(ctx, w["y"] | 0));
    lv_obj_set_size(chart, pmX(ctx, w["w"] | 400), pmY(ctx, w["h"] | 250));
    applyAnchor(chart, w["anchor"]);

    lv_chart_set_type(chart, LV_CHART_TYPE_LINE);
    int points = w["points"] | 60;
    lv_chart_set_point_count(chart, points);
    lv_chart_set_update_mode(chart, LV_CHART_UPDATE_MODE_SHIFT);
    int min = w["min"] | 0, max = w["max"] | 100;
    lv_chart_set_range(chart, LV_CHART_AXIS_PRIMARY_Y, min, max);
    lv_chart_set_div_line_count(chart, 3, 4);

    lv_obj_set_style_bg_color(chart, ctx.bg, LV_PART_MAIN);
    lv_obj_set_style_bg_opa(chart, LV_OPA_20, LV_PART_MAIN);
    lv_obj_set_style_border_width(chart, 1, LV_PART_MAIN);
    lv_obj_set_style_border_color(chart, ctx.muted, LV_PART_MAIN);
    lv_obj_set_style_line_color(chart, ctx.muted, LV_PART_MAIN); // div lines
    lv_obj_set_style_size(chart, 0, 0, LV_PART_INDICATOR);       // no point dots

    lv_color_t color = parseColor(ctx, w["color"], ctx.accent);
    lv_chart_series_t* series =
        lv_chart_add_series(chart, color, LV_CHART_AXIS_PRIMARY_Y);

    Bound* b = newBound(chart, WType::Chart, w["bind"], ctx, color);
    if (b) {
        b->min = min;
        b->max = max;
        b->series = series;
        b->autoscale = w["autoscale"] | false;
    }
}

void createImage(lv_obj_t* parent, JsonObjectConst w, const ThemeCtx& ctx) {
    lv_obj_t* img = lv_image_create(parent);
    makePassive(img);

    char path[96];
    snprintf(path, sizeof(path), "S:%s/%s", ctx.themeDir, w["src"] | "logo.png");
    lv_image_set_src(img, path);

    int32_t targetW = pmX(ctx, w["w"] | 0);
    if (targetW > 0) {
        lv_image_header_t header;
        if (lv_image_decoder_get_info(path, &header) == LV_RESULT_OK && header.w > 0) {
            lv_image_set_scale(img, (uint32_t)256 * targetW / header.w);
        }
    }
    lv_obj_set_pos(img, pmX(ctx, w["x"] | 0), pmY(ctx, w["y"] | 0));
    applyAnchor(img, w["anchor"]);
}

void createRect(lv_obj_t* parent, JsonObjectConst w, const ThemeCtx& ctx) {
    lv_obj_t* rect = lv_obj_create(parent);
    makePassive(rect);
    lv_obj_set_pos(rect, pmX(ctx, w["x"] | 0), pmY(ctx, w["y"] | 0));
    lv_obj_set_size(rect, pmX(ctx, w["w"] | 100), pmY(ctx, w["h"] | 100));
    applyAnchor(rect, w["anchor"]);
    lv_obj_set_style_bg_color(rect, parseColor(ctx, w["color"], ctx.muted), 0);
    lv_obj_set_style_bg_opa(rect, LV_OPA_COVER, 0);
    lv_obj_set_style_border_width(rect, 0, 0);
    lv_obj_set_style_radius(rect, w["radius"] | 4, 0);
}

} // namespace

void reset() {
    s_nBound = 0;
}

void create(lv_obj_t* parent, JsonObjectConst spec, const ThemeCtx& ctx) {
    const char* type = spec["type"] | "text";
    if (!strcmp(type, "text")) createText(parent, spec, ctx);
    else if (!strcmp(type, "bar")) createBar(parent, spec, ctx);
    else if (!strcmp(type, "arc")) createArc(parent, spec, ctx);
    else if (!strcmp(type, "chart")) createChart(parent, spec, ctx);
    else if (!strcmp(type, "image")) createImage(parent, spec, ctx);
    else if (!strcmp(type, "rect")) createRect(parent, spec, ctx);
}

void updateAll() {
    for (int i = 0; i < s_nBound; i++) {
        Bound& b = s_bound[i];
        float v = 0;
        bool haveNum = b.bind[0] && stats::getNumber(b.bind, v);
        bool warn = b.hasWarn && haveNum && v > b.warnAbove;

        switch (b.type) {
            case WType::Text: {
                char buf[64];
                formatBind(b.fmt, b.bind, buf, sizeof(buf));
                lv_label_set_text(b.obj, buf);
                lv_obj_set_style_text_color(b.obj, warn ? b.warnColor : b.color, 0);
                break;
            }
            case WType::Bar: {
                if (haveNum) lv_bar_set_value(b.obj, (int32_t)v, LV_ANIM_OFF);
                lv_obj_set_style_bg_color(b.obj, warn ? b.warnColor : b.color,
                                          LV_PART_INDICATOR);
                break;
            }
            case WType::Arc: {
                if (haveNum) lv_arc_set_value(b.obj, (int32_t)v);
                lv_obj_set_style_arc_color(b.obj, warn ? b.warnColor : b.color,
                                           LV_PART_INDICATOR);
                if (b.valueLabel) {
                    char buf[16];
                    if (haveNum) snprintf(buf, sizeof(buf), "%.0f", v);
                    else strlcpy(buf, "--", sizeof(buf));
                    lv_label_set_text(b.valueLabel, buf);
                }
                break;
            }
            case WType::Chart: {
                if (!haveNum || !b.series) break;
                if (b.autoscale) {
                    b.maxSeen = b.maxSeen * 0.995f;
                    if (v > b.maxSeen) b.maxSeen = v;
                    int32_t top = (int32_t)(b.maxSeen * 1.2f) + 1;
                    lv_chart_set_range(b.obj, LV_CHART_AXIS_PRIMARY_Y, 0, top);
                }
                lv_chart_set_next_value(b.obj, b.series, (int32_t)v);
                break;
            }
        }
    }
}

} // namespace widgets
