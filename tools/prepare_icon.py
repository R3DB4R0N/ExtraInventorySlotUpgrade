"""Produces the Thunderstore icon at the exact 256x256 the store requires.

Thunderstore rejects anything that is not exactly 256x256, so this takes the
authored artwork and normalises it. Source art is kept untouched.

    python tools/prepare_icon.py                     # icon-source.png -> icon.png
    python tools/prepare_icon.py my-art.png          # explicit source

If the source is not square it is centre-cropped to square first, so the result
is never stretched.
"""

import sys
from pathlib import Path

from PIL import Image

TARGET = 256
DEFAULT_SOURCE = "icon-source.png"
OUTPUT = "icon.png"


def main():
    root = Path(__file__).resolve().parent.parent
    source = Path(sys.argv[1]) if len(sys.argv) > 1 else root / DEFAULT_SOURCE

    if not source.is_absolute():
        source = root / source

    if not source.exists():
        raise SystemExit(f"Source image not found: {source}")

    image = Image.open(source).convert("RGBA")
    print(f"source: {source.name} {image.width}x{image.height}")

    # Centre-crop to square rather than stretching a non-square source.
    if image.width != image.height:
        side = min(image.width, image.height)
        left = (image.width - side) // 2
        top = (image.height - side) // 2
        image = image.crop((left, top, left + side, top + side))
        print(f"centre-cropped to {side}x{side}")

    if image.size != (TARGET, TARGET):
        resample = Image.LANCZOS
        image = image.resize((TARGET, TARGET), resample)
        print(f"resampled to {TARGET}x{TARGET}")

    # Thunderstore icons are shown on an opaque card; flatten so any transparency
    # does not render as black.
    if image.mode == "RGBA":
        flat = Image.new("RGB", image.size, (26, 22, 30))
        flat.paste(image, mask=image.split()[3])
        image = flat

    out = root / OUTPUT
    image.save(out, "PNG")
    print(f"wrote {out} {image.width}x{image.height}")


if __name__ == "__main__":
    main()
