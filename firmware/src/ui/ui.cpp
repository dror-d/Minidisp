#include "ui.h"

#include <Arduino.h>
#include <Preferences.h>
#include <lvgl.h>

#include "../data/stats_model.h"
#include "../hal/hal.h"
#include "../theme/theme_engine.h"

namespace ui {

namespace {

constexpr uint32_t kStaleMs = 5000;

Preferences s_prefs;
lv_obj_t* s_bootScreen = nullptr;
lv_obj_t* s_banner = nullptr;
int s_page = 0;
char s_pendingTheme[24] = "";
char s_savedTheme[24] = "";
bool s_suppressClick = false;
bool s_bannerVisible = false;

void screenEventCb(lv_event_t* e) {
    lv_event_code_t code = lv_event_get_code(e);
    if (code == LV_EVENT_SHORT_CLICKED) {
        if (s_suppressClick) {
            s_suppressClick = false;
            return;
        }
        nextPage();
    } else if (code == LV_EVENT_LONG_PRESSED) {
        s_suppressClick = true;
        nextTheme();
    }
}

void attachScreenEvents(lv_obj_t* screen) {
    lv_obj_add_event_cb(screen, screenEventCb, LV_EVENT_SHORT_CLICKED, nullptr);
    lv_obj_add_event_cb(screen, screenEventCb, LV_EVENT_LONG_PRESSED, nullptr);
}

void setBannerVisible(bool visible) {
    if (!s_banner || visible == s_bannerVisible) return;
    s_bannerVisible = visible;
    if (visible) lv_obj_remove_flag(s_banner, LV_OBJ_FLAG_HIDDEN);
    else lv_obj_add_flag(s_banner, LV_OBJ_FLAG_HIDDEN);
}

} // namespace

void init() {
    s_prefs.begin("minidisp", false);
    s_prefs.getString("theme", s_savedTheme, sizeof(s_savedTheme));
    int bright = s_prefs.getInt("bright", 90);
    hal::setBrightness(bright);

    // Boot screen on the default active screen.
    s_bootScreen = lv_screen_active();
    lv_obj_set_style_bg_color(s_bootScreen, lv_color_hex(0x101418), 0);
    lv_obj_set_style_bg_opa(s_bootScreen, LV_OPA_COVER, 0);

    lv_obj_t* title = lv_label_create(s_bootScreen);
    lv_label_set_text(title, "Minidisp");
    lv_obj_set_style_text_font(title, &lv_font_montserrat_28, 0);
    lv_obj_set_style_text_color(title, lv_color_hex(0x00C8FF), 0);
    lv_obj_align(title, LV_ALIGN_CENTER, 0, -20);

    lv_obj_t* sub = lv_label_create(s_bootScreen);
    lv_label_set_text_fmt(sub, "v%s  •  %s", MINIDISP_VERSION, hal::boardName());
    lv_obj_set_style_text_font(sub, &lv_font_montserrat_12, 0);
    lv_obj_set_style_text_color(sub, lv_color_hex(0x5A6570), 0);
    lv_obj_align(sub, LV_ALIGN_CENTER, 0, 14);

    // "Waiting for PC" banner lives on the top layer, above any theme screen.
    s_banner = lv_obj_create(lv_layer_top());
    lv_obj_remove_flag(s_banner, LV_OBJ_FLAG_CLICKABLE);
    lv_obj_remove_flag(s_banner, LV_OBJ_FLAG_SCROLLABLE);
    lv_obj_set_size(s_banner, LV_SIZE_CONTENT, LV_SIZE_CONTENT);
    lv_obj_set_style_bg_color(s_banner, lv_color_hex(0x202830), 0);
    lv_obj_set_style_bg_opa(s_banner, LV_OPA_90, 0);
    lv_obj_set_style_border_color(s_banner, lv_color_hex(0xFF5040), 0);
    lv_obj_set_style_border_width(s_banner, 1, 0);
    lv_obj_set_style_radius(s_banner, 6, 0);
    lv_obj_set_style_pad_all(s_banner, 6, 0);
    lv_obj_align(s_banner, LV_ALIGN_BOTTOM_MID, 0, -8);

    lv_obj_t* bannerText = lv_label_create(s_banner);
    lv_label_set_text(bannerText, "Waiting for PC...");
    lv_obj_set_style_text_font(bannerText, &lv_font_montserrat_12, 0);
    lv_obj_set_style_text_color(bannerText, lv_color_hex(0xE6E6E6), 0);

    lv_obj_add_flag(s_banner, LV_OBJ_FLAG_HIDDEN);
}

void applyTheme(const char* name) {
    const char* target = (name && theme_engine::hasTheme(name))
                             ? name
                             : theme_engine::themeName(0);
    if (!target || !*target) return; // no themes on FS — stay on boot screen

    lv_obj_t* screen = theme_engine::loadTheme(target);
    if (!screen) return;
    attachScreenEvents(screen);
    s_page = 0;
    if (s_bootScreen) {
        // theme_engine only deletes screens it created; drop the boot screen once.
        lv_obj_delete(s_bootScreen);
        s_bootScreen = nullptr;
    }
    s_prefs.putString("theme", target);
    strlcpy(s_savedTheme, target, sizeof(s_savedTheme));
    theme_engine::applyStats();
}

void requestThemeSwitch(const char* name) {
    strlcpy(s_pendingTheme, name, sizeof(s_pendingTheme));
}

void update() {
    if (s_pendingTheme[0]) {
        char name[24];
        strlcpy(name, s_pendingTheme, sizeof(name));
        s_pendingTheme[0] = 0;
        applyTheme(name);
    }
    setBannerVisible(!stats::isFresh(kStaleMs));
}

void showPage(int n) {
    int count = theme_engine::pageCount();
    if (count == 0) return;
    s_page = ((n % count) + count) % count;
    for (int i = 0; i < count; i++) {
        lv_obj_t* p = theme_engine::pageObj(i);
        if (!p) continue;
        if (i == s_page) lv_obj_remove_flag(p, LV_OBJ_FLAG_HIDDEN);
        else lv_obj_add_flag(p, LV_OBJ_FLAG_HIDDEN);
    }
}

void nextPage() {
    showPage(s_page + 1);
}

void nextTheme() {
    int count = theme_engine::themeCount();
    if (count < 2) return;
    int idx = (theme_engine::currentThemeIndex() + 1) % count;
    requestThemeSwitch(theme_engine::themeName(idx));
}

void onStatsUpdated() {
    theme_engine::applyStats();
    setBannerVisible(false);
}

void setBrightness(int pct) {
    hal::setBrightness(pct);
    s_prefs.putInt("bright", pct);
}

const char* savedThemeName() {
    return s_savedTheme;
}

} // namespace ui
