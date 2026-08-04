// CYD ESP32-2432S028R: 2.8" 320x240 panel (ILI9341, or ST7789 on the v3
// variant via CYD_PANEL_ST7789), XPT2046 resistive touch on its own SPI pins.
// Pinout source: docs/RESEARCH-hardware.md.
#ifdef MINIDISP_BOARD_CYD

#include "hal.h"

namespace {

class LGFX_CYD : public lgfx::LGFX_Device {
#ifdef CYD_PANEL_ST7789
    lgfx::Panel_ST7789 _panel;
#else
    lgfx::Panel_ILI9341 _panel;
#endif
    lgfx::Bus_SPI _bus;
    lgfx::Light_PWM _light;
    lgfx::Touch_XPT2046 _touch;

public:
    LGFX_CYD() {
        {
            auto cfg = _bus.config();
            cfg.spi_host = SPI2_HOST;
            cfg.spi_mode = 0;
            cfg.freq_write = 40000000;
            cfg.freq_read = 16000000;
            cfg.spi_3wire = false;
            cfg.use_lock = true;
            cfg.dma_channel = SPI_DMA_CH_AUTO;
            cfg.pin_sclk = 14;
            cfg.pin_mosi = 13;
            cfg.pin_miso = 12;
            cfg.pin_dc = 2;
            _bus.config(cfg);
            _panel.setBus(&_bus);
        }
        {
            auto cfg = _panel.config();
            cfg.pin_cs = 15;
            cfg.pin_rst = -1;
            cfg.pin_busy = -1;
            cfg.panel_width = 240;
            cfg.panel_height = 320;
            cfg.offset_x = 0;
            cfg.offset_y = 0;
            cfg.offset_rotation = 0;
            cfg.readable = true;
            cfg.invert = false;
            cfg.rgb_order = false;
            cfg.dlen_16bit = false;
            cfg.bus_shared = false;
            _panel.config(cfg);
        }
        {
            auto cfg = _light.config();
            cfg.pin_bl = 21;
            cfg.invert = false;
            cfg.freq = 12000;
            cfg.pwm_channel = 7;
            _light.config(cfg);
            _panel.setLight(&_light);
        }
        {
            // Touch controller lives on separate pins (second SPI bus).
            auto cfg = _touch.config();
            cfg.spi_host = SPI3_HOST;
            cfg.freq = 1000000;
            cfg.pin_sclk = 25;
            cfg.pin_mosi = 32;
            cfg.pin_miso = 39;
            cfg.pin_cs = 33;
            cfg.pin_int = 36;
            cfg.x_min = 300;
            cfg.x_max = 3900;
            cfg.y_min = 200;
            cfg.y_max = 3700;
            cfg.offset_rotation = 0;
            cfg.bus_shared = false;
            _touch.config(cfg);
            _panel.setTouch(&_touch);
        }
        setPanel(&_panel);
    }
};

LGFX_CYD s_gfx;

} // namespace

namespace hal {

void init() {
    s_gfx.init();
    s_gfx.setRotation(1); // landscape, USB on the right -> 320x240
    s_gfx.setSwapBytes(true);
    s_gfx.setBrightness(255);
}

lgfx::LGFX_Device& gfx() { return s_gfx; }
uint16_t width() { return s_gfx.width(); }
uint16_t height() { return s_gfx.height(); }

bool touchAvailable() { return true; }

bool readTouch(uint16_t& x, uint16_t& y) {
    return s_gfx.getTouch(&x, &y);
}

void setBrightness(uint8_t pct) {
    if (pct > 100) pct = 100;
    s_gfx.setBrightness((uint16_t)pct * 255 / 100);
}

void setOrientation(bool portrait) {
    s_gfx.setRotation(portrait ? 0 : 1);
}

const char* boardName() { return MINIDISP_BOARD_NAME; }

} // namespace hal

#endif // MINIDISP_BOARD_CYD
