"""Cinzel for the editor's titles: two static weights cut from the variable font (Regular 400, Bold 700), each
fitted to the measure the pages were laid out for - capitals 0.59 em tall, 0.19 em of air between them, lines
0.85 up and 0.28 down - by a larger unitsPerEm (outlines untouched, drawn smaller), even tracking on every glyph
and the vertical metrics set. Hinting dropped (made for the old em). Cinzel is under the SIL Open Font License 1.1
with no Reserved Font Name, so the fitted cut keeps its name; the change is said in its copyright string.

    python build.py Cinzel[wght].ttf outdir
"""
import sys
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
from fontTools.pens.boundsPen import BoundsPen

SOURCE, OUT = sys.argv[1], sys.argv[2]
CAP, AIR, ASCENT, DESCENT = 0.59, 0.19, 0.85, 0.28
# Cinzel's lowercase is small capitals at 86% of the capitals; the pages were laid out for 70% (AvQest, and the
# Exocet tweak before it: "all non-capitalized characters scaled down to 70%"), and at 86% mixed-case text ran a
# quarter wider. Scaled to that proportion, with less air between them, as theirs has.
LOWER, LOWER_AIR = 0.70 / 0.857, 0.6
NOTE = " Modified for Project Diablo II Single Player Companion: static weight, scaled and letterspaced."


def bounds(glyphs, name):
    pen = BoundsPen(glyphs)
    glyphs[name].draw(pen)
    return pen.bounds


def shrink_lowercase(font):
    """Every glyph a lowercase letter maps to, scaled about the origin; returns their names."""
    glyf, hmtx = font["glyf"], font["hmtx"]
    names = {name for code, name in font.getBestCmap().items() if chr(code).islower()}
    for name in names:
        glyph = glyf[name]
        width, lsb = hmtx[name]
        if glyph.isComposite():
            for component in glyph.components:
                component.x, component.y = round(component.x * LOWER), round(component.y * LOWER)
        elif glyph.numberOfContours > 0:
            glyph.coordinates.scale((LOWER, LOWER))
            glyph.coordinates.toInt()
        hmtx[name] = (round(width * LOWER), round(lsb * LOWER))
    return names


def fit(font):
    lower = shrink_lowercase(font)
    glyf, hmtx, cmap = font["glyf"], font["hmtx"], font.getBestCmap()
    glyphs = font.getGlyphSet()
    cap = bounds(glyphs, cmap[ord("H")])[3]
    new_upm = round(cap / CAP)
    caps = [cmap[ord(c)] for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ"]
    ink = sum(bounds(glyphs, n)[2] - bounds(glyphs, n)[0] for n in caps) / len(caps)
    adv = sum(hmtx[n][0] for n in caps) / len(caps)
    track = max(0, round(AIR * new_upm - (adv - ink)))
    half = track // 2

    for name in font.getGlyphOrder():
        glyph = glyf[name]
        width, lsb = hmtx[name]
        if width == 0:
            continue
        # A composite takes its parts' move; only outlines of their own are moved.
        own = round(track * LOWER_AIR) if name in lower else track
        if not glyph.isComposite() and glyph.numberOfContours > 0:
            glyph.coordinates.translate((own // 2, 0))
        hmtx[name] = (width + own, lsb)

    for name in font.getGlyphOrder():
        glyph = glyf[name]
        glyph.recalcBounds(glyf)
        hmtx[name] = (hmtx[name][0], getattr(glyph, "xMin", 0))
        if hasattr(glyph, "program"):
            glyph.program.fromBytecode(b"")
    for table in ("fpgm", "prep", "cvt ", "gasp", "hdmx", "LTSH", "VDMX"):
        if table in font:
            del font[table]

    font["head"].unitsPerEm = new_upm
    ascent, descent = round(ASCENT * new_upm), round(DESCENT * new_upm)
    hhea, os2 = font["hhea"], font["OS/2"]
    hhea.ascent, hhea.descent, hhea.lineGap = ascent, -descent, 0
    os2.sTypoAscender, os2.sTypoDescender, os2.sTypoLineGap = ascent, -descent, 0
    os2.usWinAscent, os2.usWinDescent = ascent, descent
    return new_upm, track


for weight, style in ((400, "Regular"), (700, "Bold")):
    font = instancer.instantiateVariableFont(TTFont(SOURCE), {"wght": weight})
    upm, track = fit(font)
    bold = weight >= 700
    font["OS/2"].usWeightClass = weight
    font["OS/2"].fsSelection = (font["OS/2"].fsSelection & ~0x61) | (0x20 if bold else 0x40)
    font["head"].macStyle = (font["head"].macStyle & ~1) | (1 if bold else 0)
    for record in font["name"].names:
        if record.nameID in (1, 16):
            record.string = "Cinzel"
        elif record.nameID in (2, 17):
            record.string = style
        elif record.nameID == 4:
            record.string = "Cinzel" if not bold else "Cinzel Bold"
        elif record.nameID == 6:
            record.string = f"Cinzel-{style}"
        elif record.nameID == 3:
            record.string = f"Cinzel-{style};PD2SPCompanion"
        elif record.nameID == 0:
            record.string = str(record.toUnicode()) + NOTE
    for extra in ("STAT", "fvar", "avar", "gvar", "HVAR", "MVAR"):
        if extra in font:
            del font[extra]
    path = f"{OUT}/Cinzel-{style}.ttf"
    font.save(path)
    print(f"{path}: em {upm}, tracking {track} ({track / upm:.3f} em)")
