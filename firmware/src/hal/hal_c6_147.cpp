// Waveshare ESP32-C6-Touch-LCD-1.47: 172x320 ST7789-class panel, native USB CDC.
//
// !! BRING-UP PENDING !! Pin assignments below follow the Waveshare
// ESP32-C6-LCD-1.47 wiki but MUST be verified against the board's wiki/demo
// code before first flash (the C6 only has GPIO0-30). The AXS5106L capacitive
// touch controller (I2C) has no LovyanGFX driver; touch is disabled until a
// small I2C driver is added during bring-up.
#ifdef MINIDISP_BOARD_C6_147

#include "hal.h"

namespace {

class LGFX_C6_147 : public lgfx::LGFX_Device {
    lgfx::Panel_ST7789 _panel;
    lgfx::Bus_SPI _bus;
    lgfx::Light_PWM _light;

public:
    LGFX_C6_147() {
        {
            auto cfg = _bus.config();
            cfg.spi_host = SPI2_HOST; // the C6's general-purpose SPI
            cfg.spi_mode = 0;
            cfg.freq_write = 40000000;
            cfg.freq_read = 16000000;
            cfg.spi_3wire = false;
            cfg.use_lock = true;
            cfg.dma_channel = SPI_DMA_CH_AUTO;
            cfg.pin_sclk = 7;  // VERIFY
            cfg.pin_mosi = 6;  // VERIFY
            cfg.pin_miso = -1;
            cfg.pin_dc = 15;   // VERIFY
            _bus.config(cfg);
            _panel.setBus(&_bus);
        }
        {
            auto cfg = _panel.config();
            cfg.pin_cs = 14;  // VERIFY
            cfg.pin_rst = 21; // VERIFY
            cfg.pin_busy = -1;
            cfg.panel_width = 172;
            cfg.panel_height = 320;
            cfg.offset_x = 34; // (240-172)/2, typical for 1.47" ST7789 glass
            cfg.offset_y = 0;
            cfg.offset_rotation = 0;
            cfg.readable = false;
            cfg.invert = true; // ST7789 1.47" modules are usually inverted
            cfg.rgb_order = false;
            cfg.dlen_16bit = false;
            cfg.bus_shared = false;
            _panel.config(cfg);
        }
        {
            auto cfg = _light.config();
            cfg.pin_bl = 22; // VERIFY
            cfg.invert = false;
            cfg.freq = 12000;
            cfg.pwm_channel = 7;
            _light.config(cfg);
            _panel.setLight(&_light);
        }
        setPanel(&_panel);
    }
};

LGFX_C6_147 s_gfx;

} // namespace

namespace hal {

void init() {
    s_gfx.init();
    s_gfx.setRotation(0); // portrait 172x320
    s_gfx.setSwapBytes(true);
    s_gfx.setBrightness(255);
}

lgfx::LGFX_Device& gfx() { return s_gfx; }
uint16_t width() { return s_gfx.width(); }
uint16_t height() { return s_gfx.height(); }

bool touchAvailable() { return false; } // AXS5106L driver pending bring-up
bool readTouch(uint16_t&, uint16_t&) { return false; }

void setBrightness(uint8_t pct) {
    if (pct > 100) pct = 100;
    s_gfx.setBrightness((uint16_t)pct * 255 / 100);
}

void setOrientation(bool portrait) {
    s_gfx.setRotation(portrait ? 0 : 1); // native glass is portrait (172x320)
}

const char* boardName() { return MINIDISP_BOARD_NAME; }

} // namespace hal

#endif // MINIDISP_BOARD_C6_147
