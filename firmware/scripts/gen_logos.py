#!/usr/bin/env python3
"""Generate placeholder logo.png files for the sample themes.

Pure stdlib (struct+zlib) so it runs anywhere Python does. Draws a small
"mini dashboard" mark: rounded-square ring with three signal bars.
Users replace themes/<name>/logo.png with their own PNG to change the logo.
"""

import struct
import zlib
from pathlib import Path

SIZE = 64
THEMES_DIR = Path(__file__).resolve().parents[2] / "themes"

# (theme, ring color, bar color) as RGB
LOGOS = {
    "carbon": ((0x00, 0xC8, 0xFF), (0xE6, 0xE6, 0xE6)),
    "gauges": ((0x00, 0xC8, 0xFF), (0xFF, 0xB8, 0x4D)),
    "terminal": ((0x33, 0xFF, 0x33), (0x99, 0xFF, 0x99)),
}


def png_chunk(tag: bytes, data: bytes) -> bytes:
    return (
        struct.pack(">I", len(data))
        + tag
        + data
        + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    )


def write_png(path: Path, pixels):
    """pixels: SIZE x SIZE rows of (r, g, b, a)."""
    raw = b"".join(
        b"\x00" + b"".join(struct.pack("BBBB", *px) for px in row) for row in pixels
    )
    ihdr = struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0)
    png = (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", ihdr)
        + png_chunk(b"IDAT", zlib.compress(raw, 9))
        + png_chunk(b"IEND", b"")
    )
    path.write_bytes(png)


def in_rounded_ring(x, y, lo=4, hi=59, r=10, thickness=5):
    """Rounded-square ring border test."""

    def inside(x, y, lo, hi, r):
        if not (lo <= x <= hi and lo <= y <= hi):
            return False
        cx = min(max(x, lo + r), hi - r)
        cy = min(max(y, lo + r), hi - r)
        return (x - cx) ** 2 + (y - cy) ** 2 <= r * r

    return inside(x, y, lo, hi, r) and not inside(
        x, y, lo + thickness, hi - thickness, max(r - thickness, 0)
    )


BARS = [  # (x0, x1, y0, y1)
    (15, 23, 36, 52),
    (28, 36, 22, 52),
    (41, 49, 30, 52),
]


def make_logo(ring_rgb, bar_rgb):
    rows = []
    for y in range(SIZE):
        row = []
        for x in range(SIZE):
            if in_rounded_ring(x, y):
                row.append((*ring_rgb, 255))
            elif any(x0 <= x <= x1 and y0 <= y <= y1 for x0, x1, y0, y1 in BARS):
                row.append((*bar_rgb, 255))
            else:
                row.append((0, 0, 0, 0))
        rows.append(row)
    return rows


def main():
    for theme, (ring, bars) in LOGOS.items():
        out = THEMES_DIR / theme / "logo.png"
        out.parent.mkdir(parents=True, exist_ok=True)
        write_png(out, make_logo(ring, bars))
        print(f"wrote {out}")


if __name__ == "__main__":
    main()
