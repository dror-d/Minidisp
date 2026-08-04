# Minidisp Theme Format v1

A theme is a folder on the device's LittleFS under `/themes/<name>/` containing:

```
/themes/carbon/
  theme.json     — layout + colors (this spec)
  logo.png       — optional logo image, referenced by image widgets. Replace this file to change the logo. Keep it small (≤ 120x120 px, PNG, ideally < 20 KB).
```

Theme sources live in `themes/` at the repo root and are synced to `firmware/data/themes/` (which becomes the LittleFS image). To add a theme: copy a folder, edit, re-run the sync + `pio run -t buildfs -t uploadfs` (or reflash via the web flasher).

## Coordinates — resolution independence

All positions/sizes are **per-mille (0–1000) of the screen**, so one theme renders on any resolution/orientation:
- `x`, `w`, `r` scale by screen width; `y`, `h` by screen height (`r` uses the smaller dimension).
- `anchor` places the widget's reference point: `tl` (default), `tc`, `tr`, `ml`, `mc`, `mr`, `bl`, `bc`, `br`. E.g. `{"x":500,"y":500,"anchor":"mc"}` = centered.

## theme.json

```json
{
  "name": "Carbon",
  "author": "VVS",
  "version": 1,
  "colors": {
    "bg": "#101418",
    "fg": "#E6E6E6",
    "accent": "#00C8FF",
    "accent2": "#7CFC00",
    "muted": "#5A6570",
    "warn": "#FF5040"
  },
  "warnAbove": { "cpu.temp": 85, "cpu.load": 95, "mem.pct": 90 },
  "pages": [
    { "name": "Overview", "widgets": [ ... ] },
    { "name": "Network",  "widgets": [ ... ] }
  ]
}
```

- `orientation` (optional): `"portrait"` rotates the device's panel when the theme loads; default is landscape. The editor sets this automatically from the canvas size you design at.
- `colors`: named palette. Any widget `color`/`bg` field may reference a palette name (`"accent"`) or a literal `"#RRGGBB"`.
- `warnAbove`: when a bound value exceeds the threshold, bar/arc/text widgets bound to it switch to the `warn` color.
- Multiple `pages`: tap the screen to cycle pages; long-press cycles themes.

## Widgets

Common fields: `type`, `x`, `y`, `anchor`, `color`, plus per-type fields below. `bind` is a data path from PROTOCOL.md (e.g. `cpu.load`, `net.ip`, `disk1.pct`) — or any **custom value id** supplied via the XML source's `<value id="...">` elements (e.g. `myapp.status`), letting themes show data from other applications.

| type | fields | renders |
|---|---|---|
| `text` | `bind`, `fmt`, `size` (`sm`\|`md`\|`lg`\|`xl` or numeric px `"12"`–`"36"`, snapped to fonts 12/14/16/20/24/28/36), `color` | formatted value. `fmt` example: `"CPU {v:.0f}%"`, `{v}` = raw value/string. Static label: omit `bind`, use `"text": "LABEL"` |
| `bar` | `bind`, `w`, `h`, `min`, `max`, `color`, `bg` | horizontal bar gauge |
| `arc` | `bind`, `r`, `thickness`, `min`, `max`, `color`, `bg`, `label` (bool, shows value in center), `size` | radial gauge, 270° sweep |
| `chart` | `bind`, `w`, `h`, `min`, `max`, `points` (history length, default 60), `autoscale` (bool), `color` | scrolling line chart |
| `image` | `src` (file in theme folder, e.g. `logo.png`), `w`, `h` (0 = natural size) | PNG image |
| `rect` | `w`, `h`, `color`, `radius` | filled rounded rectangle (panels/dividers) |

## Example

```json
{ "type": "arc", "bind": "cpu.load", "x": 250, "y": 400, "r": 300,
  "thickness": 12, "min": 0, "max": 100, "color": "accent", "label": true, "size": "lg" }
```

## Fonts

`size` maps to Montserrat built-ins scaled by screen class:
| screen height | sm | md | lg | xl |
|---|---|---|---|---|
| ≤ 240 px | 12 | 14 | 20 | 28 |
| > 240 px | 14 | 16 | 24 | 36 |

## Sample themes

- **carbon** — dark, text + bars, information-dense overview (default)
- **gauges** — three arc dials (CPU / MEM / GPU) + network page
- **terminal** — green-on-black monospace-look, retro list layout
