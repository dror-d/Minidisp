# Research Brief: ESP32 Display Hardware

Compiled 2026-08-01 from web research. Guides all HAL/board decisions in `firmware/`.

## CYD (ESP32-2432S028R) — primary test device

2.8" ILI9341 320x240, resistive touch, ESP32-WROOM-32, 4MB flash, **no PSRAM**, CH340 USB-UART.

### Pinout

| Component | Function | GPIO |
|-----------|----------|------|
| TFT SPI (HSPI) | MISO | 12 |
| | MOSI | 13 |
| | SCLK | 14 |
| | CS | 15 |
| | DC | 2 |
| | RST | none (software) |
| | Backlight | 21 |
| Touch XPT2046 (**separate bit-bang/VSPI bus**) | IRQ | 36 |
| | MOSI | 32 |
| | MISO | 39 |
| | CLK | 25 |
| | CS | 33 |
| RGB LED (active low) | R / G / B | 4 / 16 / 17 |
| LDR light sensor | analog in | 34 |
| Speaker | DAC | 26 |
| MicroSD | MISO/MOSI/SCK/CS | 19 / 23 / 18 / 5 |

### Variants

- v1/v2: ILI9341 (single micro-USB or dual micro+USB-C).
- **v3 ("2USB" some batches): ST7789** — silkscreen says 7789. If display shows garbage/inverted colors, switch panel class. Our HAL keeps panel type selectable per build flag.

## Waveshare ESP32-C6-Touch-LCD-1.47

172x320 ST7789/JD9853, capacitive touch AXS5106L (I2C, addr 0x51/0x6B/0x7E), ESP32-C6FH8 (RISC-V 160MHz), 8MB flash, 512KB HP SRAM, native USB CDC (no bridge chip), WiFi 6.

| Function | GPIO |
|----------|------|
| DC | 45 |
| CS | 21 |
| SCK | 38 |
| MOSI | 39 |
| RST | 47 |
| Touch | I2C (AXS5106L), polling-based (IRQ often unpopulated) |

Note: verify against Waveshare wiki demo code when bring-up starts; Medium/AndroidCrypto articles are the best walkthroughs.

## ESP32-1732S019 (generic 1.9")

170x320 ST7789, ESP32-S3. TFT_eSPI community setup file `Setup809_ESP32_CYD_1721S019_170x320.h` documents pins — pull GPIO mapping from there at bring-up time.

## Graphics stack decision

**LVGL 9.x + LovyanGFX** (both MIT):
- TFT_eSPI is unmaintained (>12 months as of 2025) and breaks on ESP32-C6 (`'VSPI' was not declared`).
- LovyanGFX ~3x faster, actively maintained, LVGL docs now prefer it.
- LVGL buffer strategy on CYD (no PSRAM): 2 partial buffers of ~1/10 screen (240*320/10*2B ≈ 19KB each) in internal SRAM. Never place LVGL draw buffers in PSRAM (3-4x slower writes).

## Build system decision

**PlatformIO with pioarduino fork** for all envs:
- Official `platform = espressif32` is frozen at Arduino core 2.x → no ESP32-C6 Arduino support.
- `platform = https://github.com/pioarduino/platform-espressif32/...` ships Arduino 3.x + IDF 5.x, supports classic ESP32, S3, and C6.
- Community CYD board JSONs: `mariusdp/platformio-esp32-2432s028`, `rzeldent/platformio-espressif32-sunton`.

## USB serial

| Device | Bridge | Windows | VID:PID |
|--------|--------|---------|---------|
| CYD | CH340 UART0 | COMx (needs CH340 driver) | 1A86:7523 |
| ESP32-C6 | native USB CDC | COMx (no driver) | 303A:1001 (Espressif) |

Both look like plain serial ports to the PC app; auto-detect by VID:PID first, then probe-with-ping fallback. 115200 baud is the reliable CH340 rate.

## Touch

- XPT2046: `XPT2046_Touchscreen` (PaulStoffregen) or LovyanGFX built-in touch support. Raw 12-bit 0-4095; typical usable range X 200-3700, Y 240-3800. Persist calibration to LittleFS.
- AXS5106L: I2C polling driver (Waveshare provides `esp_lcd_touch_axs5106l`); no calibration needed.

## Sources

- https://randomnerdtutorials.com/esp32-cheap-yellow-display-cyd-pinout-esp32-2432s028r/
- https://github.com/witnessmenow/ESP32-Cheap-Yellow-Display
- https://www.waveshare.com/wiki/ESP32-C6-Touch-LCD-1.47
- https://github.com/pioarduino/platform-espressif32
- https://github.com/mariusdp/platformio-esp32-2432s028
- https://docs.lvgl.io/master/integration/chip_vendors/espressif/tips_and_tricks.html
- https://github.com/limpens/esp32-2432S028R (LVGL9 + LovyanGFX CYD example)
- https://medium.com/@androidcrypto/getting-started-with-an-esp32-c6-waveshare-lcd-device-with-1-47-inch-st7789-tft-display-07804fdc589a
