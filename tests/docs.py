"""Doc comments that sit on the wrong member.

    python tests/docs.py

Moving code leaves a member's /// comment behind, where it stacks onto the next member's own -
two summaries on one member, one of them about something else. Also a <para> left outside its
summary. Exit code: 0 clean, 1 found.
"""
import re
import sys
from pathlib import Path

SRC = Path(__file__).resolve().parent.parent / "src"

CLOSES = re.compile(r"^\s*/// (?:</(?:summary|param|para|remarks|returns)>|<summary>.*</summary>)\s*$")


def main() -> int:
    found = []
    for path in SRC.rglob("*.cs"):
        if {"obj", "bin", "_vendor"} & set(path.parts):
            continue
        lines = path.read_text(encoding="utf-8-sig").splitlines()
        for n, (before, line) in enumerate(zip(lines, lines[1:]), start=2):
            here = f"{path.relative_to(SRC).as_posix()}:{n}"
            if line.strip().startswith("/// <summary>") and CLOSES.match(before):
                found.append(f"{here}  a second summary: the one above belongs to another member")
            elif line.strip() == "/// <para>" and before.strip() == "/// </summary>":
                found.append(f"{here}  a para outside its summary")
    for each in found:
        print(each)
    print(f"{len(found)} misplaced doc comments")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
