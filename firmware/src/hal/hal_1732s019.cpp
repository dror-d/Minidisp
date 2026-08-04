// ESP32-1732S019: 1.9" 170x320 ST7789 on ESP32-S3, no touch.
//
// !! BRING-UP PENDING !! Pins follow the community TFT_eSPI setup for this
// board (Setup809) — verify at bring-up.
#ifdef MINIDISP_BOARD_1732S019

#include "hal.h"

namespace {

class LGFX_1732S019 : public lgfx::LGFX_Device {
    lgfx::Panel_ST7789 _panel;
    lgfx::Bus_SPI _bus;
    lgfx::Light_PWM _light;

public:
    LGFX_1732S019() {
        {
            auto cfg = _bus.config();
            cfg.spi_host = SPI2_HOST;
            cfg.spi_mode = 0;
            cfg.freq_write = 40000000;
            cfg.freq_read = 16000000;
            cfg.spi_3wire = false;
            cfg.use_lock = true;
            cfg.dma_channel = SPI_DMA_CH_AUTO;
            cfg.pin_sclk = 12; // VERIFY
            cfg.pin_mosi = 13; // VERIFY
            cfg.pin_miso = -1;
            cfg.pin_dc = 11;   // VERIFY
            _bus.config(cfg);
            _panel.setBus(&_bus);
        }
        {
            auto cfg = _panel.config();
            cfg.pin_cs = 10; // VERIFY
            cfg.pin_rst = 1; // VERIFY
            cfg.pin_busy = -1;
            cfg.panel_width = 170;
            cfg.panel_height = 320;
            cfg.offset_x = 35; // (240-170)/2
            cfg.offset_y = 0;
            cfg.offset_rotation = 0;
            cfg.readable = false;
            cfg.invert = true;
            cfg.rgb_order = false;
            cfg.dlen_16bit = false;
            cfg.bus_shared = false;
            _panel.config(cfg);
        }
        {
            auto cfg = _light.config();
            cfg.pin_bl = 14; // VERIFY
            cfg.invert = false;
            cfg.freq = 12000;
            cfg.pwm_channel = 7;
            _light.config(cfg);
            _panel.setLight(&_light);
        }
        setPanel(&_panel);
    }
};

LGFX_1732S019 s_gfx;

} // namespace

namespace hal {

void init() {
    s_gfx.init();
    s_gfx.setRotation(1); // landscape 320x170
    s_gfx.setSwapBytes(true);
    s_gfx.setBrightness(255);
}

lgfx::LGFX_Device& gfx() { return s_gfx; }
uint16_t width() { return s_gfx.width(); }
uint16_t height() { return s_gfx.height(); }

bool touchAvailable() { return false; }
bool readTouch(uint16_t&, uint16_t&) { return false; }

void setBrightness(uint8_t pct) {
    if (pct > 100) pct = 100;
    s_gfx.setBrightness((uint16_t)pct * 255 / 100);
}

void setOrientation(bool portrait) {
    s_gfx.setRotation(portrait ? 0 : 1); // native glass is portrait (170x320)
}

const char* boardName() { return MINIDISP_BOARD_NAME; }

} // namespace hal

#endif // MINIDISP_BOARD_1732S019
