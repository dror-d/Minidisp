#!/usr/bin/env python3
"""Build firmware + filesystem for one or more envs and stage everything the
web flasher needs under flasher/firmware/<env>/ (bins + esp-web-tools
manifest.json with decimal offsets). See docs/RESEARCH-flashing.md.

Usage:
    python scripts/release.py            # default: cyd
    python scripts/release.py cyd esp32c6-147
    python scripts/release.py --no-build cyd   # just stage existing bins
"""

import csv
import json
import shutil
import subprocess
import sys
from pathlib import Path

FIRMWARE_DIR = Path(__file__).resolve().parents[1]
ROOT = FIRMWARE_DIR.parent
FLASHER_DIR = ROOT / "flasher" / "firmware"

VERSION = "0.2.0"

ENVS = {
    "cyd": {"chipFamily": "ESP32", "partitions": "minidisp_4mb.csv"},
    "cyd-st7789": {"chipFamily": "ESP32", "partitions": "minidisp_4mb.csv"},
    "esp32c6-147": {"chipFamily": "ESP32-C6", "partitions": "minidisp_8mb.csv"},
    "esp32-1732s019": {"chipFamily": "ESP32-S3", "partitions": "minidisp_4mb.csv"},
}

# Standard Arduino-core flash layout (same for ESP32 / C6 / S3 app images).
FIXED_PARTS = [
    ("bootloader.bin", 0x1000),
    ("partitions.bin", 0x8000),
    ("boot_app0.bin", 0xE000),
    ("firmware.bin", 0x10000),
]


def run_pio(env: str, target: str | None = None):
    cmd = [sys.executable, "-m", "platformio", "run", "-e", env]
    if target:
        cmd += ["-t", target]
    print(f"+ {' '.join(cmd)}")
    subprocess.run(cmd, cwd=FIRMWARE_DIR, check=True)


def fs_offset(partitions_csv: str) -> int:
    with open(FIRMWARE_DIR / "partitions" / partitions_csv, newline="") as f:
        for row in csv.reader(f):
            if len(row) >= 4 and row[0].strip() == "spiffs":
                return int(row[3].strip(), 0)
    raise SystemExit(f"no spiffs row in {partitions_csv}")


def find_boot_app0(build_dir: Path) -> Path:
    candidate = build_dir / "boot_app0.bin"
    if candidate.exists():
        return candidate
    packages = Path.home() / ".platformio" / "packages"
    for hit in sorted(packages.glob("framework-arduinoespressif32*/tools/partitions/boot_app0.bin")):
        return hit
    raise SystemExit("boot_app0.bin not found — run a build first")


def stage(env: str, build: bool):
    cfg = ENVS[env]
    if build:
        run_pio(env)
        run_pio(env, "buildfs")

    build_dir = FIRMWARE_DIR / ".pio" / "build" / env
    out = FLASHER_DIR / env
    out.mkdir(parents=True, exist_ok=True)

    parts = []
    for name, offset in FIXED_PARTS:
        src = find_boot_app0(build_dir) if name == "boot_app0.bin" else build_dir / name
        if not src.exists():
            raise SystemExit(f"missing {src} — did the build succeed?")
        shutil.copy2(src, out / name)
        parts.append({"path": name, "offset": offset})

    littlefs = build_dir / "littlefs.bin"
    if littlefs.exists():
        shutil.copy2(littlefs, out / "littlefs.bin")
        parts.append({"path": "littlefs.bin", "offset": fs_offset(cfg["partitions"])})
    else:
        print(f"warning: {littlefs} missing — flasher will not install themes")

    manifest = {
        "name": f"Minidisp ({env})",
        "version": VERSION,
        "new_install_prompt_erase": True,
        "builds": [{"chipFamily": cfg["chipFamily"], "parts": parts}],
    }
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2))
    print(f"staged {out}")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    build = "--no-build" not in sys.argv
    envs = args or ["cyd"]
    for env in envs:
        if env not in ENVS:
            raise SystemExit(f"unknown env '{env}' (choose from {', '.join(ENVS)})")
    # Refresh theme assets into firmware/data before buildfs.
    subprocess.run([sys.executable, str(FIRMWARE_DIR / "scripts" / "sync_themes.py")],
                   check=True)
    for env in envs:
        stage(env, build)


if __name__ == "__main__":
    main()
