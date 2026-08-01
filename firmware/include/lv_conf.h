/**
 * Minidisp LVGL 9 configuration.
 * Only overrides are listed — everything else falls back to the defaults in
 * lvgl's lv_conf_internal.h. Tuned for no-PSRAM boards (CYD).
 */
#ifndef LV_CONF_H
#define LV_CONF_H

#define LV_COLOR_DEPTH 16

/* Use the C library malloc (ESP32 heap) instead of LVGL's static pool — a
 * static pool lands in .bss and overflows the ESP32's DRAM segment. */
#define LV_USE_STDLIB_MALLOC LV_STDLIB_CLIB

#define LV_DEF_REFR_PERIOD 33 /* ~30 fps is plenty for 2 Hz data */

/* Fonts: sm/md/lg/xl mapping per screen class, see docs/THEMES.md */
#define LV_FONT_MONTSERRAT_12 1
#define LV_FONT_MONTSERRAT_14 1
#define LV_FONT_MONTSERRAT_16 1
#define LV_FONT_MONTSERRAT_20 1
#define LV_FONT_MONTSERRAT_24 1
#define LV_FONT_MONTSERRAT_28 1
#define LV_FONT_MONTSERRAT_36 1
#define LV_FONT_DEFAULT &lv_font_montserrat_14

/* PNG decoding for theme logos */
#define LV_USE_LODEPNG 1

/* Map LittleFS to the 'S' drive so themes reference "S:/themes/x/logo.png" */
#define LV_USE_FS_ARDUINO_ESP_LITTLEFS 1
#define LV_FS_ARDUINO_ESP_LITTLEFS_LETTER 'S'

#define LV_USE_LOG 0

#endif /* LV_CONF_H */
