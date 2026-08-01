# Research Brief: Existing PC-Monitor Projects, Data Sources, Themes

Compiled 2026-08-01. Guides the companion app, protocol, and theme engine design.

## Projects studied

| Project | What it is | What we take from it |
|---|---|---|
| [turing-smart-screen-python](https://github.com/mathoudebine/turing-smart-screen-python) (GPLv3) | Python monitor for Turing USB screens; famous YAML theme system (100+ community themes) | Widget model: TEXT / GRAPH(bar) / RADIAL / LINE_GRAPH positioned over a background image. We re-implement the *concept* in our own JSON format (GPL themes not copied). |
| [CYD-System-Monitor](https://github.com/iamlite/CYD-System-Monitor/) (iamlite) | CYD + LVGL dashboard fed by Glances REST | Proof of LVGL layouts that work at 320x240; color-coded warnings; auto-scaling units |
| [Gnat-Stats / HardwareSerialMonitor](https://github.com/koogar/Gnat-Stats) (GPL-2.0) | Windows companion → serial → Arduino/ESP32 screens | Companion-app architecture: OpenHardwareMonitor/LHM backend pushing over USB serial |
| [witnessmenow CYD repo](https://github.com/witnessmenow/ESP32-Cheap-Yellow-Display) | CYD community hub | Board docs, example configs, community project list |

## PC data sources (Windows)

| Source | License | Temps | Notes |
|---|---|---|---|
| **LibreHardwareMonitorLib** (chosen) | MPL 2.0 (safe to link) | Yes | NuGet lib; admin elevation needed for full temp sensors — degrade gracefully without |
| psutil (Python) | MIT | Partial on Windows | Not chosen (user picked .NET) |
| Windows perf counters | built-in | No | Fallback only |
| AIDA64 | proprietary | Yes | See XML notes below |
| HWiNFO | freeware | Yes | Shared memory; 12h limit in free version |

## AIDA64 external-display mechanism

- Writes sensor values to shared memory `AIDA64_SensorValues` (pseudo-XML, **no root element**, null-terminated), to registry `HKCU\Software\FinalWire\AIDA64\SensorValues`, and WMI.
- Fragment format: `<temp><id>..</id><label>CPU Package</label><value>56.3</value></temp>`, same for `<fan>`, `<duty>`, `<volt>`, `<sys>`.
- Parse recipe: wrap with `<root>...</root>`, strip `\0`, then XDocument.Parse. Temps always Celsius, labels always English.
- Our XmlFileSource supports (a) our own documented schema, (b) AIDA64-style fragment files (wrap-and-parse).

## Serial protocol conventions seen in the wild

- JSON lines, newline-terminated, 115200 baud (CH340-safe; 921600 works but unnecessary at 2 Hz).
- 1–5 Hz update rates typical; 2 Hz chosen (smooth enough, low load).

## Theme/asset takeaways

- Turing theme repo themes are GPL/mixed — don't copy assets; design our own 3 themes.
- LVGL demos (MIT) show gauge/dashboard patterns usable at 320x240 with vector-only widgets (no bitmap assets needed except the logo).
- Asset storage: LittleFS partition, LVGL loads images via `S:/path` letter-mapped filesystem driver + PNG decoder (LVGL 9 has built-in lodepng). Logo = `logo.png` per theme, swappable without recompiling.

## Sources

- https://github.com/mathoudebine/turing-smart-screen-python/wiki/System-monitor-:-themes
- https://github.com/iamlite/CYD-System-Monitor/
- https://github.com/koogar/HardwareSerialMonitor
- https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- https://www.aida64.com/user-manual/hardware-monitoring/external-applications
- https://bertstechblog.wordpress.com/2013/08/02/aida64-external-sensor-display/
- https://lvgl.io/demos
