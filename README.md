# Minidisp

PC status display for small ESP32 touch screens: CPU load, temperatures, RAM,
network interfaces + IPs, disks — streamed from a Windows tray app over USB, or
read from any auto-updating XML file. Themes (including the logo) live on the
device's flash and can be swapped without recompiling.

**Primary device:** CYD "Cheap Yellow Display" ESP32-2432S028R (2.8" touch).
Also targeted: Waveshare ESP32-C6 Touch LCD 1.47", ESP32-1732S019 1.9".

```
┌─────────────┐  USB serial (JSON lines, 115200)  ┌──────────────────┐
│ PC companion │ ────────────────────────────────> │ ESP32 + display  │
│ (.NET tray)  │   live sensors  OR  XML file      │ LVGL 9 + themes  │
└─────────────┘                                    └──────────────────┘
```

## Layout

| Path | What |
|---|---|
| `firmware/` | PlatformIO project (LVGL 9 + LovyanGFX, envs: `cyd`, `cyd-st7789`, `esp32c6-147`, `esp32-1732s019`) |
| `companion/` | .NET 8 Windows tray app (LibreHardwareMonitor live mode + XML file mode) |
| `themes/` | Theme sources — JSON layout + replaceable `logo.png` per theme |
| `flasher/` | Browser flasher site (esp-web-tools), GitHub-Pages ready |
| `docs/` | Protocol spec, theme format, research briefs |

## Quick start (CYD)

> **Which CYD do I have?** Units with a single micro-USB are usually the
> ILI9341 panel (`-e cyd`). Units with **two USB ports (micro + USB-C)** are
> usually the v3 with an **ST7789** panel — use `-e cyd-st7789`. If the screen
> renders garbled/blue-tinted with one build, flash the other.

```bash
# 1. Firmware + themes onto the device (USB)
cd firmware
python scripts/sync_themes.py
python -m platformio run -e cyd-st7789 -t upload -t uploadfs   # or -e cyd

# 2. Companion app on the PC
cd ../companion
dotnet run --project src/Minidisp.Companion
```

The display shows "Waiting for PC..." until the companion connects (it
auto-detects the COM port). **Tap** = next page, **long-press** = next theme.
Tray menu: switch source (live / XML), pick device theme, brightness.

Browser flashing: `python firmware/scripts/release.py cyd`, then serve
`flasher/` (see `flasher/README.md`).

## XML mode

Point the companion at any auto-updating XML file (tray → "Choose XML file").
Native schema: `companion/docs/sample.xml`. AIDA64-style sensor fragment files
(`<temp><label>..</label><value>..</value></temp>`) are also understood.

## Troubleshooting

- **Flashing hangs or crashes on Windows** (esptool `UnicodeEncodeError`, or
  `pio run -t upload` never finishes when output is piped): the esptool v5
  progress bar needs a UTF-8 console. Set `$env:PYTHONUTF8='1'` (PowerShell)
  before flashing.
- **Flash fails to connect**: close the companion app first (it holds the COM
  port), install the CH340 driver, and if needed hold `BOOT` while flashing.
- **Garbled / wrong colors**: wrong panel variant — see the CYD note above.
- **Companion flagged by Defender**: expected for a freshly built unsigned
  exe; allow it or sign your build.

## Docs

- `docs/PROTOCOL.md` — serial JSON protocol
- `docs/THEMES.md` — theme format + how to replace the logo
- `docs/RESEARCH-*.md` — hardware/projects/flashing research briefs
