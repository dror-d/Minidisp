// Board hardware abstraction. Exactly one hal_*.cpp compiles per build env
// (selected by the MINIDISP_BOARD_* build flag in platformio.ini).
#pragma once

#include <stdint.h>

#define LGFX_USE_V1
#include <LovyanGFX.hpp>

namespace hal {

// Initializes panel, backlight and touch. Must be called before LVGL setup.
void init();

lgfx::LGFX_Device& gfx();

uint16_t width();
uint16_t height();

bool touchAvailable();
// Returns true while touched; x/y in display coordinates.
bool readTouch(uint16_t& x, uint16_t& y);

// pct 0-100
void setBrightness(uint8_t pct);

// Rotate the panel for the theme's orientation (touch follows automatically).
void setOrientation(bool portrait);

const char* boardName();

} // namespace hal
