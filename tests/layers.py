"""The layering ratchet for Core.

    python tests/layers.py            check: fail on any inward-pointing violation not on the list
    python tests/layers.py --shrink   rewrite the list without the violations that are gone

Core's domains, inner to outer - a layer may use only the layers before it:

    Diagnostics  <  Localization  <  Storage  <  Mpq, GameFiles, Graphics  <  Items  <  Saves  <  Characters  <  Collection  <  Trade  <  Testbed  <  root

A reference from a file in one layer to a Core type declared in a later layer is a violation.
tests/layers-allowed.txt lists the violations that existed when this was written; each cut removes
its lines (--shrink), and a new one fails the check. The list only gets shorter.

Found by names: a top-level type's name used in code, not in comments or strings: bare (not after a dot, not a
member of the same name declared in the file), or written with its namespace - Saves.ItemSeason,
PD2SPCompanion.Core.Saves.ItemSeason - which is no way round the rule. Crude but
conservative; a false violation is cut by renaming or by a line on the list, never silently.
Exit code: 0 clean, 1 a new violation.
"""
import re
import sys
from pathlib import Path

CORE = Path(__file__).resolve().parent.parent / "src" / "PD2SPCompanion.Core"
ALLOWED = Path(__file__).resolve().parent / "layers-allowed.txt"

ORDER = ["Diagnostics", "Localization", "Storage", "Mpq", "GameFiles", "Graphics", "Items", "Saves", "Characters", "Collection", "Trade", "Testbed", ""]


def layer_of(namespace: str) -> int:
    tail = namespace.removeprefix("PD2SPCompanion.Core").lstrip(".")
    return ORDER.index(tail)


def code_of(text: str) -> str:
    text = re.sub(r"//.*", "", text)
    return re.sub(r'"(?:[^"\\\n]|\\.)*"', '""', text)


def main() -> int:
    files = {}
    for path in CORE.rglob("*.cs"):
        if "bin" in path.parts or "obj" in path.parts or path.name == "GlobalUsings.cs":
            continue
        text = path.read_text(encoding="utf-8-sig")
        match = re.search(r"^namespace ([\w.]+);", text, re.M)
        if match:
            files[path] = (match.group(1), text)

    declared = {}
    for path, (namespace, text) in files.items():
        for m in re.finditer(r"^(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*"
                             r"(?:class|record|struct|enum|interface)\s+([A-Z]\w*)", text, re.M):
            declared.setdefault(m.group(1), (namespace, path))

    found = set()
    for path, (namespace, text) in files.items():
        here = layer_of(namespace)
        code = code_of(text)
        members = set(re.findall(r"[\w>\]?]\s+([A-Z]\w*)\s*(?:[{=;(]|=>)", code))
        members |= set(re.findall(r"^\s+([A-Z]\w*)\s*(?:=\s*[-\w]+\s*)?,?\s*$", code, re.M))  # enum members
        for name, (their_namespace, their_path) in declared.items():
            if their_path == path or layer_of(their_namespace) <= here:
                continue
            tail = their_namespace.removeprefix("PD2SPCompanion.Core").lstrip(".")
            qualified = bool(tail) and re.search(
                r"(?<![.\w])(?:PD2SPCompanion\.Core\.)?" + re.escape(tail) + r"\." + name + r"\b", code)
            bare = name not in members and re.search(r"(?<![.\w])" + name + r"\b", code)
            if qualified or bare:
                found.add(f"{path.relative_to(CORE).as_posix()} -> {name}")

    allowed = set(line.strip() for line in ALLOWED.read_text(encoding="utf-8").splitlines()
                  if line.strip() and not line.startswith("#")) if ALLOWED.exists() else set()

    if "--shrink" in sys.argv or not ALLOWED.exists():
        kept = sorted(found & allowed) if ALLOWED.exists() else sorted(found)
        header = "# Core's inward-pointing violations still standing (tests/layers.py). Only ever shorter.\n"
        ALLOWED.write_text(header + "".join(line + "\n" for line in kept), encoding="utf-8")
        print(f"{len(kept)} allowed ({len(allowed - found)} gone since the list was written)")
        return 0

    new = sorted(found - allowed)
    gone = sorted(allowed - found)
    for line in new:
        print(f"NEW   {line}")
    print(f"{len(found)} violations: {len(new)} new, {len(gone)} gone from the list"
          + (" - run with --shrink" if gone else ""))
    return 1 if new else 0


if __name__ == "__main__":
    sys.exit(main())
