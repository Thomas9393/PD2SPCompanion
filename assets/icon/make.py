"""The editor's icon, from the user's art in this folder to the files the app uses.

    python assets/icon/make.py

"PD2SPC Icon.png" (exported from "PD2SPC Icon.psd"), the badge with its lettering, becomes:
  src/PD2SPCompanion.Shell/Assets/pd2spc.ico        the app icon: the exe, the taskbar, both windows (16-256px)

"PD2SPC Icon NoText.png", the same badge without the lettering (for the homepage icon,
then for the splash too - the name stands in type beside or under it), becomes:
  src/PD2SPCompanion.Shell/Assets/pd2spc-logo.png   the sidebar's Home button (48) and the splash's badge
                                                   (112), 448px for the larger at any display scale

The transparent margin is trimmed first, so the badge fills the icon.
"""
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
ASSETS = HERE.parents[1] / "src" / "PD2SPCompanion.Shell" / "Assets"
SIZES = [256, 128, 64, 48, 40, 32, 24, 20, 16]


def squared(name):
    """The art, its transparent margin trimmed, on a square of its longer side."""
    art = Image.open(HERE / name).convert("RGBA")
    art = art.crop(art.getbbox())
    side = max(art.size)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(art, ((side - art.width) // 2, (side - art.height) // 2))
    return square


def main():
    square = squared("PD2SPC Icon.png")
    bare = squared("PD2SPC Icon NoText.png")

    frames = [square.resize((s, s), Image.LANCZOS) for s in SIZES]
    # Bitmap frames, not PNG: Windows reads either, but .NET's Icon and other older readers take a
    # PNG frame below 256px for a bitmap and draw noise.
    frames[0].save(ASSETS / "pd2spc.ico", format="ICO", sizes=[(s, s) for s in SIZES],
                   append_images=frames[1:], bitmap_format="bmp")
    bare.resize((448, 448), Image.LANCZOS).save(ASSETS / "pd2spc-logo.png", optimize=True)
    print(f"{square.width}px art -> pd2spc.ico ({len(SIZES)} sizes); "
          f"{bare.width}px art without lettering -> pd2spc-logo.png (448px)")


if __name__ == "__main__":
    main()
