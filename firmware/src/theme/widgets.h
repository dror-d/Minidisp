// Widget factory: builds LVGL objects from theme JSON specs and keeps a
// registry of data-bound widgets to refresh when stats arrive (docs/THEMES.md).
#pragma once

#include <ArduinoJson.h>
#include <lvgl.h>

namespace widgets {

struct WarnRule {
    char path[24];
    float above;
};

// Everything a widget needs from the enclosing theme.
struct ThemeCtx {
    lv_color_t bg, fg, accent, accent2, muted, warn;
    char themeDir[48]; // e.g. "/themes/carbon"
    WarnRule warns[8];
    int nWarns = 0;
    const lv_font_t* fonts[4]; // sm, md, lg, xl
    uint16_t screenW = 0, screenH = 0;
};

// Drop all registry entries (call before deleting the old theme's screen).
void reset();

// Create one widget from its JSON spec inside `parent`.
void create(lv_obj_t* parent, JsonObjectConst spec, const ThemeCtx& ctx);

// Refresh every bound widget from the current stats snapshot.
void updateAll();

} // namespace widgets
