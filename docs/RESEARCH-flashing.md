# Research Brief: Browser-Based ESP32 Flashing

Compiled 2026-08-01. Guides `flasher/` and `firmware/scripts/release.py`.

## How bruce.computer/flasher works

Bruce (pr3y/Bruce) uses **esphome/esp-web-tools** (which wraps espressif/esptool-js + Web Serial API): a static page with a device picker where each device points its `<esp-web-install-button>` at a per-device `manifest.json`. Hosted on GitHub Pages (HTTPS automatic). We copy this architecture.

## esp-web-tools essentials

- Chrome/Edge 89+ (Web Serial); Safari unsupported. HTTPS required, **localhost is exempt** → local testing with `python -m http.server` works.
- Page embed:
  ```html
  <script type="module" src="https://unpkg.com/esp-web-tools@10/dist/web/install-button.js?module"></script>
  <esp-web-install-button manifest="firmware/cyd/manifest.json"></esp-web-install-button>
  ```
- manifest.json (offsets are **decimal**):
  ```json
  { "name": "Minidisp", "version": "0.1.0",
    "builds": [{ "chipFamily": "ESP32",
      "parts": [
        {"path": "bootloader.bin", "offset": 4096},
        {"path": "partitions.bin", "offset": 32768},
        {"path": "boot_app0.bin",  "offset": 57344},
        {"path": "firmware.bin",   "offset": 65536},
        {"path": "littlefs.bin",   "offset": <FS offset from partitions.csv>} ] }] }
  ```
- `chipFamily`: `"ESP32"`, `"ESP32-C6"`, `"ESP32-S3"` — esp-web-tools auto-detects the connected chip and picks the matching build, so one manifest can carry several chip builds; we use one manifest per device instead (different pin/panel firmware per device even with same chip).

## Flash offsets (ESP32 and C6 both)

| Part | Offset |
|---|---|
| bootloader.bin | 0x1000 (4096) |
| partitions.bin | 0x8000 (32768) |
| boot_app0.bin | 0xe000 (57344) |
| firmware.bin | 0x10000 (65536) |
| littlefs.bin | from partitions.csv `spiffs` row offset |

PlatformIO puts all of these in `.pio/build/<env>/` (boot_app0.bin comes from the Arduino framework package; visible in verbose upload output `pio run -v -t upload`).

## Release script recipe

Post-build (or standalone) python script:
1. `pio run -e <env>` and `pio run -e <env> -t buildfs`
2. Copy bootloader/partitions/boot_app0/firmware/littlefs bins → `flasher/firmware/<env>/`
3. Parse `partitions/*.csv` for the FS offset; emit `manifest.json` with decimal offsets.

Optional single-file image: `esptool --chip esp32 merge-bin -o merged.bin --flash-mode dio --flash-size 4MB 0x1000 bootloader.bin 0x8000 partitions.bin 0xe000 boot_app0.bin 0x10000 firmware.bin` (not needed for esp-web-tools, which takes parts).

## Gotchas

- CH340 driver must be installed on Windows or the port never appears in the Web Serial picker.
- Auto-reset into bootloader usually works via DTR/RTS on CYD; if not, hold BOOT while clicking Connect.
- SPIFFS ≠ LittleFS on-flash — partitions row is named `spiffs` but `board_build.filesystem = littlefs` controls the actual format; keep firmware and FS image consistent.
- CORS: keep bins on the same origin as the page (GitHub Pages same-repo = fine).

## Sources

- https://github.com/esphome/esp-web-tools
- https://github.com/pr3y/Bruce (WebPage branch = flasher site)
- https://docs.espressif.com/projects/esptool/en/latest/esp32/esptool/basic-commands.html
- https://witnessmenow.github.io/ESP-Web-Tools-Tutorial/
- https://github.com/espressif/esptool-js
