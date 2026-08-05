#!/usr/bin/env python3
"""Sync theme sources (repo /themes) into firmware/data/themes, which becomes
the LittleFS image (`pio run -t buildfs`)."""

import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "themes"
DST = ROOT / "firmware" / "data" / "themes"

# Only these ship in the factory LittleFS image (it must fit the 2MB
# partition). Personal themes are pushed from the theme editor instead,
# which resamples images to fit the device.
BUNDLED = ("carbon", "gauges", "terminal")


def main():
    if DST.exists():
        shutil.rmtree(DST)
    for theme_dir in sorted(SRC.iterdir()):
        if not theme_dir.is_dir() or not (theme_dir / "theme.json").exists():
            continue
        if theme_dir.name not in BUNDLED:
            print(f"skipped {theme_dir.name} (personal theme — push it from the editor)")
            continue
        target = DST / theme_dir.name
        target.mkdir(parents=True)
        for f in theme_dir.iterdir():
            if f.is_file() and f.suffix.lower() in (".json", ".png"):
                shutil.copy2(f, target / f.name)
        print(f"synced {theme_dir.name}")


if __name__ == "__main__":
    main()
