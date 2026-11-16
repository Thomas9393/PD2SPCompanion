"""The ratchet on a view model letting go of an item by hand.

    python tests/lifts.py            check: fail where a file does not have the count on the list
    python tests/lifts.py --shrink   write the list as it stands, when it has only fallen

A move across files is several steps, and written by hand at each site it lost items: a way back that asked a file again,
a way back forgotten. A view model takes an item out of a file with ItemLift, whose Back and Done say how it ends. What is
left on tests/lifts-allowed.json lets go for good, with nothing to go back to (a delete, a sale), or is no item at all (the
grail's Release). A file with more than its count fails, and so does one with fewer until the list says so: it only gets shorter.
Exit code: 0 clean, 1 not.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MODELS = ROOT / "src" / "PD2SPCompanion.Shell" / "ViewModels"
ALLOWED = Path(__file__).resolve().parent / "lifts-allowed.json"

RELEASE = re.compile(r"\.Release\(")


def counts() -> dict[str, int]:
    found = {}
    for path in sorted(MODELS.rglob("*.cs")):
        if "bin" in path.parts or "obj" in path.parts:
            continue
        code = re.sub(r"//.*", "", path.read_text(encoding="utf-8-sig"))
        count = len(RELEASE.findall(code))
        if count:
            found[path.relative_to(ROOT).as_posix()] = count
    return found


def main() -> int:
    found = counts()
    allowed = json.loads(ALLOWED.read_text(encoding="utf-8")) if ALLOWED.exists() else None

    if "--shrink" in sys.argv or allowed is None:
        if allowed is not None and any(count > allowed.get(name, 0) for name, count in found.items()):
            print("not shrunk: a file has more than the list allows")
            return 1
        ALLOWED.write_text(json.dumps(found, indent=2) + "\n", encoding="utf-8")
        print(f"lifts: {sum(found.values())} on the list")
        return 0

    wrong = 0
    for name in sorted(set(found) | set(allowed)):
        have, most = found.get(name, 0), allowed.get(name, 0)
        if have > most:
            print(f"{name}: {have} .Release( where {most} are allowed: a move across files goes by ItemLift")
            wrong += 1
        elif have < most:
            print(f"{name}: {have} .Release( where the list says {most}: run tests/lifts.py --shrink")
            wrong += 1

    print(f"lifts: {sum(found.values())} .Release( in view models, {wrong} files off the list")
    return 1 if wrong else 0


if __name__ == "__main__":
    sys.exit(main())
