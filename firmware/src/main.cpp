// Minidisp — PC status display firmware.
// Data flow: PC companion app --USB serial JSON--> serial_link -> stats_model
//            -> theme_engine/widgets (LVGL) -> LovyanGFX panel (hal).
#include <Arduino.h>
#include <LittleFS.h>
#include <lvgl.h>

#include "comm/serial_link.h"
#include "hal/hal.h"
#include "theme/theme_engine.h"
#include "ui/ui.h"

namespace {

uint32_t tickCb() { return millis(); }

void flushCb(lv_display_t* disp, const lv_area_t* area, uint8_t* pxMap) {
    auto& gfx = hal::gfx();
    uint32_t w = area->x2 - area->x1 + 1;
    uint32_t h = area->y2 - area->y1 + 1;
    gfx.startWrite();
    gfx.setAddrWindow(area->x1, area->y1, w, h);
    gfx.writePixels(reinterpret_cast<uint16_t*>(pxMap), w * h);
    gfx.endWrite();
    lv_display_flush_ready(disp);
}

void touchCb(lv_indev_t*, lv_indev_data_t* data) {
    uint16_t x, y;
    if (hal::readTouch(x, y)) {
        data->state = LV_INDEV_STATE_PRESSED;
        data->point.x = x;
        data->point.y = y;
    } else {
        data->state = LV_INDEV_STATE_RELEASED;
    }
}

} // namespace

void setup() {
    serial_link::begin();
    hal::init();

    if (!LittleFS.begin(true)) {
        Serial.println(F("{\"err\":{\"msg\":\"LittleFS mount failed\"}}"));
    }

    lv_init();
    lv_tick_set_cb(tickCb);

    lv_display_t* disp = lv_display_create(hal::width(), hal::height());
    // Two partial buffers of 1/10 screen in internal SRAM (CYD has no PSRAM).
    size_t bufSize = (size_t)hal::width() * hal::height() / 10 * 2;
    static uint8_t* buf1 = (uint8_t*)malloc(bufSize);
    static uint8_t* buf2 = (uint8_t*)malloc(bufSize);
    lv_display_set_buffers(disp, buf1, buf2, bufSize, LV_DISPLAY_RENDER_MODE_PARTIAL);
    lv_display_set_flush_cb(disp, flushCb);

    if (hal::touchAvailable()) {
        lv_indev_t* indev = lv_indev_create();
        lv_indev_set_type(indev, LV_INDEV_TYPE_POINTER);
        lv_indev_set_read_cb(indev, touchCb);
    }

    ui::init();
    theme_engine::scanThemes();
    ui::applyTheme(ui::savedThemeName());
    serial_link::sendHello();
}

void loop() {
    serial_link::poll();
    ui::update();
    lv_timer_handler();
    delay(5);
}
