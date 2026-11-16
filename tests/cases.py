"""The UI cases, read and not run: what goes stale in them with no test failing.

    python tests/cases.py

Four things, each a way a case came to prove nothing:

  ids      An automation id a case looks for is one the program gives something. A control removed takes its id
           with it, and `Check ($null -eq (ById 'Vault_CloseGaps'))` then passes for ever - while a case that
           waits for the id fails only when it is next run, which may be weeks. The measuring scripts of
           tests/perf are read too: they are run rarely, and three of them waited for what was gone.
  helpers  A function a unit defines is one the unit calls. What a case no longer needs was left behind with
           the case's old words in its comment.
  areas    A line of tests/ui/areas.txt names files that are there and tags a case wears: a pattern that fits
           no file, or a tag no case has, calls for nothing when its file changes.
  seasons  A unit takes its season from the pad (kit.ps1: PadSeason, MainVault, OpenVaultFile, VaultCount). Sixteen
           named S13 and went on reading a file nobody wrote once the pads moved to S14.

An id made in code from parts ("Grail_Tab_" + key) is found by its beginning. One that is rightly nowhere - a
case showing that something is gone, on purpose - is not written into a case: say so in words, and test what is
there instead.
"""
from __future__ import annotations

import fnmatch
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
UNITS = REPO / "tests" / "ui" / "units"
KIT = [REPO / "tests" / "ui" / "fast.ps1", REPO / "tests" / "ui" / "kit.ps1", REPO / "tests" / "ui" / "run.ps1"]
PERF = REPO / "tests" / "perf"
AREAS = REPO / "tests" / "ui" / "areas.txt"
SOURCES = REPO / "src"

# How a case names an automation id: the kit's own finders, and the raw condition.
ID_USES = [
    re.compile(r"\bById\s+'([A-Za-z]\w*)'"),
    re.compile(r"AutomationIdProperty\s+'([A-Za-z]\w*)'"),
    re.compile(r"\bAny\s+'([A-Za-z]\w*)'"),
    re.compile(r"\bOn\s+'(?:Left|Right)'\s+'([A-Za-z]\w*)'"),
    re.compile(r"\bScroll\s+'([A-Za-z]\w*)'"),
    re.compile(r"-Id\s+'([A-Za-z]\w*)'"),
    re.compile(r"AutomationId\s+-(?:eq|ne|like|notlike)\s+'([A-Za-z]\w*)'"),
]

# The window's own buttons, which the system's frame names and no file of ours.
SYSTEM_IDS = {"MinimizeButton", "MaximizeButton", "CloseButton"}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def source_text() -> str:
    parts = []
    for path in SOURCES.rglob("*"):
        if path.suffix in (".axaml", ".cs") and "obj" not in path.parts and "bin" not in path.parts:
            parts.append(read(path))
    return "\n".join(parts)


def stale_ids(source: str) -> list[str]:
    found = []
    for unit in sorted(UNITS.glob("*.ps1")) + KIT + sorted(PERF.rglob("*.ps1")):
        if not unit.exists():
            continue
        for number, line in enumerate(read(unit).splitlines(), 1):
            if line.lstrip().startswith("#"):
                continue
            for use in ID_USES:
                for match in use.finditer(line):
                    name = match.group(1)
                    if name in SYSTEM_IDS or f'"{name}"' in source:
                        continue
                    # Made from parts: "Grail_Tab_" + key, $"PageRow_{n}".
                    stems = [name[:cut + 1] for cut in range(len(name)) if name[cut] == "_"]
                    if any(f'"{stem}"' in source or f'"{stem}{{' in source or f'$"{stem}' in source for stem in stems):
                        continue
                    found.append(f"{unit.relative_to(REPO).as_posix()}:{number}: the id '{name}' is nowhere in src")
    return found


def idle_helpers() -> list[str]:
    found = []
    define = re.compile(r"^function\s+([A-Za-z][\w-]*)", re.M)
    for unit in sorted(UNITS.glob("*.ps1")):
        text = read(unit)
        code = "\n".join(line for line in text.splitlines() if not line.lstrip().startswith("#"))
        for match in define.finditer(code):
            name = match.group(1)
            uses = len(re.findall(rf"(?<![\w-]){re.escape(name)}(?![\w-])", code))
            if uses < 2:
                found.append(f"{unit.relative_to(REPO).as_posix()}: the function {name} is never called")
    return found


def case_tags() -> set[str]:
    tags = set()
    for unit in UNITS.glob("*.ps1"):
        tags.add(unit.stem)
        for match in re.finditer(r"^Case\s+'[^']+'\s+-Tags\s+([\w\-, ]+?)(?=\s+-\w|\s*\{|\s*$)", read(unit), re.M):
            tags.update(tag.strip() for tag in match.group(1).split(",") if tag.strip())
    return tags


def stale_areas() -> list[str]:
    found = []
    files = [path.relative_to(REPO).as_posix() for folder in ("src", "tests") for path in (REPO / folder).rglob("*")
             if path.is_file() and "obj" not in path.parts and "bin" not in path.parts]
    tags = case_tags()
    for number, line in enumerate(read(AREAS).splitlines(), 1):
        if not line.strip() or line.lstrip().startswith("#") or "\t" not in line:
            continue
        pattern, wearing = (piece.strip() for piece in line.split("\t", 1))
        if not any(fnmatch.fnmatch(path, pattern) for path in files):
            found.append(f"tests/ui/areas.txt:{number}: no file fits {pattern}")
        for tag in (tag.strip() for tag in wearing.split(",")):
            if tag and tag not in tags:
                found.append(f"tests/ui/areas.txt:{number}: no case wears the tag '{tag}'")
    return found


SEASON_WRITTEN = re.compile(r"vtsmart\\S\d+|\bS\d+ Main\b|\(season S\d+|\bto S\d+['\"]")


def written_seasons() -> list[str]:
    found = []
    for unit in sorted(UNITS.glob("*.ps1")):
        for number, line in enumerate(read(unit).splitlines(), 1):
            if not line.lstrip().startswith("#") and SEASON_WRITTEN.search(line):
                found.append(f"{unit.relative_to(REPO).as_posix()}:{number}: a season written in: take it from the pad (PadSeason, MainVault)")
    return found


def main() -> int:
    source = source_text()
    found = stale_ids(source) + idle_helpers() + stale_areas() + written_seasons()
    for line in found:
        print(line)
    print(f"{len(found)} stale in the UI cases" if found else "0 stale in the UI cases: ids, helpers, areas, seasons")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
