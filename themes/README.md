# Minidisp Themes

Each folder is one theme: `theme.json` (layout, see `../docs/THEMES.md`) plus
`logo.png` (replace this file with your own PNG to change the logo — keep it
≤ 120x120 px and small).

These folders are the **source of truth**. They are copied to
`firmware/data/themes/` (the LittleFS image) by:

```
python firmware/scripts/sync_themes.py
```

then flashed with `pio run -e cyd -t uploadfs` (or via the web flasher).

| Theme | Style |
|---|---|
| `carbon` | Dark, information-dense text + bars (default) |
| `gauges` | Three arc dials (CPU / RAM / GPU) + network page |
| `terminal` | Green-on-black retro terminal look |

On the device: **tap** = next page, **long-press** = next theme.
