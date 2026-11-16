"""Renders [Glyph] paths the way Path.pin draws them: a 12-unit grid at 14 logical px, stroke 1.3714
units, round caps and joins, unique gold (#A59263) on a dark button. Display scale 1.25.
Each glyph: the real-pixel render, then the same pixels enlarged x4 (nearest) to judge legibility.

usage: python tests/ui/glyphsheet.py out.png [extra.txt]
  extra.txt: lines "name<TAB>path" - candidates, drawn after the current glyphs of the same names.
Needs resvg_py (pip install resvg_py). The rule it serves is in Glyphs.axaml's header: a glyph is
checked at real size before it is used.
"""
import io
import re
import sys
from pathlib import Path

import resvg_py
from PIL import Image, ImageDraw

GLYPHS = Path(__file__).resolve().parents[2] / "src" / "PD2SPCompanion.Shell" / "Resources" / "UI_Main" / "Glyphs.axaml"
SCALE = 1.25
BOX = 14 * SCALE            # the glyph's 12 units on screen
BTN = int(round(26 * SCALE))  # a small icon button around it
FG, BG, FRAME = "#A59263", "#16120D", "#5A4A2A"


def glyphs(path):
    text = open(path, encoding="utf-8-sig").read()
    return re.findall(r'<StreamGeometry x:Key="\[Glyph\] ([^"]+)">([^<]+)</StreamGeometry>', text)


def render(d, filled=False):
    px = BTN
    off = (px - BOX) / 2
    k = BOX / 12
    fill_rule = "evenodd" if d.strip().startswith("F0") else "nonzero"
    d = re.sub(r"^\s*F[01]\s*", "", d)
    style = (f'fill="{FG}" fill-rule="{fill_rule}" stroke="none"' if filled else
             f'fill="none" stroke="{FG}" stroke-width="1.3714" stroke-linecap="round" stroke-linejoin="round"')
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{px}" height="{px}">'
           f'<rect x="0.5" y="0.5" width="{px-1}" height="{px-1}" rx="3" fill="{BG}" stroke="{FRAME}"/>'
           f'<g transform="translate({off},{off}) scale({k})"><path d="{d}" {style}/></g></svg>')
    png = bytes(resvg_py.svg_to_bytes(svg_string=svg))
    return Image.open(io.BytesIO(png)).convert("RGB")


def sheet(items, out):
    cols = 6
    cell_w, cell_h = BTN + 8 + BTN * 4 + 16, BTN * 4 + 34
    rows = (len(items) + cols - 1) // cols
    img = Image.new("RGB", (cols * cell_w, rows * cell_h), "#2B2620")
    draw = ImageDraw.Draw(img)
    for i, (name, d) in enumerate(items):
        x, y = (i % cols) * cell_w + 6, (i // cols) * cell_h + 6
        g = render(d, filled=name == "Dice")
        img.paste(g, (x, y))
        img.paste(g.resize((BTN * 4, BTN * 4), Image.NEAREST), (x + BTN + 8, y))
        draw.text((x, y + BTN * 4 + 6), name, fill="#E8DCC0")
    img.save(out)


if __name__ == "__main__":
    items = glyphs(GLYPHS)
    if len(sys.argv) > 2:
        extra = [tuple(l.rstrip("\n").split("\t", 1)) for l in open(sys.argv[2], encoding="utf-8") if "\t" in l]
        names = {n for n, _ in extra}
        items = [(n, d) for n, d in items if n in names or any(e.startswith(n + " ") for e in names)] + extra
    sheet(items, sys.argv[1])
