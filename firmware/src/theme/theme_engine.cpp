#include "theme_engine.h"

#include <Arduino.h>
#include <ArduinoJson.h>
#include <LittleFS.h>

#include <memory>

#include "../hal/hal.h"
#include "widgets.h"

namespace theme_engine {

namespace {

constexpr int kMaxThemes = 8;
constexpr int kMaxPages = 6;
constexpr size_t kMaxThemeFile = 16 * 1024;

char s_themes[kMaxThemes][24];
int s_nThemes = 0;
char s_current[24] = "";
lv_obj_t* s_screen = nullptr;
lv_obj_t* s_pages[kMaxPages];
int s_nPages = 0;

lv_color_t colorOr(JsonVariantConst v, uint32_t fallback) {
    const char* s = v.as<const char*>();
    if (s && s[0] == '#') return lv_color_hex((uint32_t)strtoul(s + 1, nullptr, 16));
    return lv_color_hex(fallback);
}

void pickFonts(widgets::ThemeCtx& ctx) {
    if (hal::height() <= 240) {
        ctx.fonts[0] = &lv_font_montserrat_12;
        ctx.fonts[1] = &lv_font_montserrat_14;
        ctx.fonts[2] = &lv_font_montserrat_20;
        ctx.fonts[3] = &lv_font_montserrat_28;
    } else {
        ctx.fonts[0] = &lv_font_montserrat_14;
        ctx.fonts[1] = &lv_font_montserrat_16;
        ctx.fonts[2] = &lv_font_montserrat_24;
        ctx.fonts[3] = &lv_font_montserrat_36;
    }
}

} // namespace

int scanThemes() {
    s_nThemes = 0;
    File root = LittleFS.open("/themes");
    if (!root || !root.isDirectory()) return 0;
    File entry;
    while ((entry = root.openNextFile()) && s_nThemes < kMaxThemes) {
        if (!entry.isDirectory()) continue;
        char probe[80];
        snprintf(probe, sizeof(probe), "/themes/%s/theme.json", entry.name());
        if (LittleFS.exists(probe)) {
            strlcpy(s_themes[s_nThemes++], entry.name(), sizeof(s_themes[0]));
        }
    }
    // Alphabetical order keeps the cycle order stable across boots.
    for (int i = 1; i < s_nThemes; i++) {
        for (int j = i; j > 0 && strcmp(s_themes[j - 1], s_themes[j]) > 0; j--) {
            char tmp[24];
            memcpy(tmp, s_themes[j - 1], sizeof(tmp));
            memcpy(s_themes[j - 1], s_themes[j], sizeof(tmp));
            memcpy(s_themes[j], tmp, sizeof(tmp));
        }
    }
    return s_nThemes;
}

int themeCount() { return s_nThemes; }
const char* themeName(int i) { return (i >= 0 && i < s_nThemes) ? s_themes[i] : ""; }

bool hasTheme(const char* name) {
    for (int i = 0; i < s_nThemes; i++) {
        if (!strcmp(s_themes[i], name)) return true;
    }
    return false;
}

const char* currentTheme() { return s_current; }

int currentThemeIndex() {
    for (int i = 0; i < s_nThemes; i++) {
        if (!strcmp(s_themes[i], s_current)) return i;
    }
    return -1;
}

lv_obj_t* loadTheme(const char* name) {
    char path[80];
    snprintf(path, sizeof(path), "/themes/%s/theme.json", name);
    File f = LittleFS.open(path, "r");
    if (!f) return nullptr;
    size_t size = f.size();
    if (size == 0 || size > kMaxThemeFile) {
        f.close();
        return nullptr;
    }
    std::unique_ptr<char[]> buf(new (std::nothrow) char[size + 1]);
    if (!buf) {
        f.close();
        return nullptr;
    }
    f.readBytes(buf.get(), size);
    buf[size] = 0;
    f.close();

    JsonDocument doc;
    if (deserializeJson(doc, buf.get())) return nullptr;

    widgets::ThemeCtx ctx;
    JsonObjectConst colors = doc["colors"];
    ctx.bg = colorOr(colors["bg"], 0x101418);
    ctx.fg = colorOr(colors["fg"], 0xE6E6E6);
    ctx.accent = colorOr(colors["accent"], 0x00C8FF);
    ctx.accent2 = colorOr(colors["accent2"], 0x7CFC00);
    ctx.muted = colorOr(colors["muted"], 0x5A6570);
    ctx.warn = colorOr(colors["warn"], 0xFF5040);
    snprintf(ctx.themeDir, sizeof(ctx.themeDir), "/themes/%s", name);
    ctx.screenW = hal::width();
    ctx.screenH = hal::height();
    pickFonts(ctx);

    JsonObjectConst warns = doc["warnAbove"];
    for (JsonPairConst kv : warns) {
        if (ctx.nWarns >= (int)(sizeof(ctx.warns) / sizeof(ctx.warns[0]))) break;
        widgets::WarnRule& r = ctx.warns[ctx.nWarns++];
        strlcpy(r.path, kv.key().c_str(), sizeof(r.path));
        r.above = kv.value().as<float>();
    }

    JsonArrayConst pages = doc["pages"];
    if (pages.isNull() || pages.size() == 0) return nullptr;

    // Build the new screen before tearing down the old one.
    widgets::reset();
    lv_obj_t* screen = lv_obj_create(nullptr);
    lv_obj_set_style_bg_color(screen, ctx.bg, 0);
    lv_obj_set_style_bg_opa(screen, LV_OPA_COVER, 0);
    lv_obj_remove_flag(screen, LV_OBJ_FLAG_SCROLLABLE);

    s_nPages = 0;
    for (JsonObjectConst page : pages) {
        if (s_nPages >= kMaxPages) break;
        lv_obj_t* cont = lv_obj_create(screen);
        lv_obj_set_size(cont, LV_PCT(100), LV_PCT(100));
        lv_obj_set_pos(cont, 0, 0);
        lv_obj_set_style_bg_opa(cont, LV_OPA_TRANSP, 0);
        lv_obj_set_style_border_width(cont, 0, 0);
        lv_obj_set_style_pad_all(cont, 0, 0);
        lv_obj_set_style_radius(cont, 0, 0);
        lv_obj_remove_flag(cont, LV_OBJ_FLAG_CLICKABLE);
        lv_obj_remove_flag(cont, LV_OBJ_FLAG_SCROLLABLE);
        if (s_nPages > 0) lv_obj_add_flag(cont, LV_OBJ_FLAG_HIDDEN);
        s_pages[s_nPages++] = cont;

        for (JsonObjectConst w : page["widgets"].as<JsonArrayConst>()) {
            widgets::create(cont, w, ctx);
        }
    }

    lv_obj_t* old = s_screen;
    s_screen = screen;
    lv_screen_load(screen);
    if (old) lv_obj_delete(old);

    strlcpy(s_current, name, sizeof(s_current));
    return screen;
}

int pageCount() { return s_nPages; }
lv_obj_t* pageObj(int i) { return (i >= 0 && i < s_nPages) ? s_pages[i] : nullptr; }

void applyStats() {
    widgets::updateAll();
}

} // namespace theme_engine
