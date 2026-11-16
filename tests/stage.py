"""
Stages every ladder item, rebuilt once, so questions about real items read a file instead of
rebuilding 68,000 items (~7 min on 8 processes).

    python tests/stage.py            rebuild the stage
    python tests/stage.py --jobs N   processes at once (default 8)

Writes TestCorpus/staged/ladder-items.tsv (header row first) and stage.json, which records the
commit and whether the tree was dirty. A stage says what the editor built at that commit: rebuild
it after changing TradeItems or the tables' reading, and never use it in place of the regression,
which exists to test the rebuild.
"""

import json
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from regress import CLI, CORPUS, LADDERS, REPO

COLUMNS = ["ladder", "character", "clvl", "index", "mercenary", "code", "quality", "ilvl", "ethereal",
           "runeword", "automagic", "prefixes", "suffixes", "rare_name", "corruption", "desecration",
           "sockets", "name", "blob"]
BATCH = 100


def main() -> int:
    args = sys.argv[1:]
    jobs = int(args[args.index("--jobs") + 1]) if "--jobs" in args else 8

    built = subprocess.run(["dotnet", "build", "-c", "Debug", str(REPO / "src/PD2SPCompanion.Cli")],
                           capture_output=True, text=True)
    if built.returncode != 0:
        print(built.stdout[-3000:])
        return 1

    files = [str(p) for ladder in LADDERS for p in sorted((CORPUS / "ladder" / ladder).glob("*.json"))
             if not p.name.startswith("_")]
    batches = [files[at:at + BATCH] for at in range(0, len(files), BATCH)]

    def stage(batch: list[str]) -> str:
        done = subprocess.run([str(CLI), "stageitems", *batch], capture_output=True, text=True, encoding="utf-8")
        return done.stdout

    started = time.time()
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        outputs = list(pool.map(stage, batches))

    lines = [line for output in outputs for line in output.splitlines() if line.count("\t") == len(COLUMNS) - 1]
    failed = sum("FAILED:" in line for line in lines)

    out = CORPUS / "staged"
    out.mkdir(exist_ok=True)
    (out / "ladder-items.tsv").write_text("\t".join(COLUMNS) + "\n" + "\n".join(lines) + "\n", encoding="utf-8")

    def git(*command: str) -> str:
        return subprocess.run(["git", *command], cwd=REPO, capture_output=True, text=True).stdout.strip()

    stamp = {
        "staged": time.strftime("%Y-%m-%d %H:%M:%S"),
        "commit": git("rev-parse", "--short", "HEAD"),
        "dirty": bool(git("status", "--porcelain", "--", "src")),
        "characters": len(files),
        "items": len(lines),
        "failed": failed,
        "minutes": round((time.time() - started) / 60, 1),
    }
    (out / "stage.json").write_text(json.dumps(stamp, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(stamp))
    return 0


if __name__ == "__main__":
    sys.exit(main())
