"""
Tiered regression for PD2SPCompanion, run against the corpus (PD2SPC_CORPUS; tests/paths.py).

    python tests/regress.py fast        ~2 min   after any change
    python tests/regress.py mid         ~5 min   before a commit
    python tests/regress.py full        ~10 min  after changing the legitimacy check,
                                                 item building or the tables it reads

    --jobs N      processes at once (default 6; lower it while the game is running)
    --no-build    use the CLI as built
    --accept      make this run the new baseline (after reading why it differs)

Every tier is offline: the ladder audit reads the cached armories and never fetches.
Season 13's corpora (the stress 35, the trade listings, PhySpearZon, TestCorpus/ladder) are checked on S13's
tables (S13_TABLES); what is forged now (cleancheck) and Season 14's ladder (TestCorpus/ladder-s14) on the
installed ones. fast samples the S14 ladder, mid and full take it whole.
"""

import os
import re
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from paths import CORPUS, REPO, S13_TABLES

CLI = REPO / "src/PD2SPCompanion.Cli/bin/Debug/net10.0/PD2SPCompanion.Cli.exe"
PACK_S12 = CORPUS / "packs/tdlofcc-s12"
TABLES_S12 = PACK_S12 / "PD2-PlugY-main/ProjectD2/data/global/excel/modpacks/official"

LADDERS = ("softcore", "hardcore")

# Each ladder folder and the tables it is read with: None, the installed; S13_TABLES, the season before's, the app's
# own snapshot - for the corpora taken in it.
SEASON_LADDERS = {"ladder": S13_TABLES, "ladder-s14": None}

# Season 14 ladder items flagged and known, one "<character>: <item>" a line - open questions, not regressions.
# A flag on anything else fails the tier.
KNOWN = CORPUS / "baseline" / "ladder-s14-known.txt"
MID_SAMPLE = 10  # mid audits every tenth ladder character
SHARD = 100  # ladder characters per process

BUILT = re.compile(r"(\d+) built, (\d+) failed; items (\d+)/(\d+) rebuilt whole; legitimacy (\d+)/(\d+) silent")
LEGIT_ITEM = re.compile(r"^[^ ].*\(.*, v[0-9]+\)")


def run(out: Path, *args: str, tables: Path | None = None) -> str:
    """Runs the CLI with its output in a file, and returns that output - on the installed tables, or on `tables`."""
    environment = {key: value for key, value in os.environ.items() if key != "PD2SPC_TABLES"}
    if tables is not None:
        environment["PD2SPC_TABLES"] = str(tables)
    with open(out, "w", encoding="utf-8", errors="replace") as file:
        subprocess.run([str(CLI), *args], stdout=file, stderr=subprocess.STDOUT, env=environment)
    return out.read_text(encoding="utf-8", errors="replace")


def names(folder: str, ladder: str, step: int) -> list[str]:
    found = sorted(p.stem for p in (CORPUS / folder / ladder).glob("*.json") if not p.stem.startswith("_"))
    return found[::step]


def audit_jobs(folder: str, step: int, run_dir: Path) -> list:
    """One season's ladder audits in small shards, so the pool keeps every process busy to the end."""
    jobs = []
    for ladder in LADDERS:
        members = names(folder, ladder, step)
        count = max(1, -(-len(members) // SHARD))
        for shard in range(count):
            part = members[shard::count]
            out = run_dir / f"audit_{folder}_{ladder}_{shard + 1}.txt"
            jobs.append((f"{folder}/{ladder} {shard + 1}", lambda part=part, out=out, ladder=ladder: run(
                out, "armorystress", *part, "--out", str(CORPUS / folder / ladder), "--quick", "--verbose",
                tables=SEASON_LADDERS[folder])))
    return jobs


def built_check(text: str, known: set[str] | None = None) -> tuple[bool, str, list[int]]:
    match = BUILT.search(text)
    if not match:
        return False, "no summary line", [0] * 6

    n = [int(g) for g in match.groups()]
    # An item flagged is its "  -- <character>: <item> [...]" line; a known one (KNOWN) is let stand.
    heads = [line.strip()[3:].split(" [")[0] for line in text.splitlines() if line.startswith("  --")]
    unknown = [head for head in heads if head not in (known or set())]
    ok = n[1] == 0 and n[2] == n[3] and n[4] + len(heads) - len(unknown) == n[5] and not unknown
    return ok, match.group(0), n


def main() -> int:
    args = sys.argv[1:]
    tier = next((a for a in args if a in ("fast", "mid", "full")), None)
    if tier is None:
        print(__doc__)
        return 2

    jobs_at_once = int(args[args.index("--jobs") + 1]) if "--jobs" in args else 6

    if not CORPUS.exists():
        print(f"no corpus at {CORPUS}")
        return 2

    if "--no-build" not in args:
        built = subprocess.run(["dotnet", "build", "-c", "Debug", str(REPO / "src/PD2SPCompanion.Cli")],
                               capture_output=True, text=True)
        if built.returncode != 0:
            print(built.stdout[-3000:])
            return 1

    run_dir = CORPUS / "runs" / f"{time.strftime('%Y-%m-%d_%H%M%S')}_{tier}"
    run_dir.mkdir(parents=True)
    trade = CORPUS / "trade"
    spec = str(CORPUS / "saves/PhySpearZon.d2s")

    # Season 13's corpora on its tables; what is forged now on the installed ones.
    jobs = [
        ("stress35", lambda: run(run_dir / "stress35.txt", "armorystress", "--all",
                                 "--out", str(CORPUS / "stress"), "--quick", tables=S13_TABLES)),
        ("clean", lambda: run(run_dir / "clean.txt", "cleancheck", "--baseline")),
        ("roll", lambda: run(run_dir / "roll.txt", "rollcheck", spec, str(trade / "recent"), str(trade / "desec"),
                             tables=S13_TABLES)),
        ("leftoff", lambda: run(run_dir / "leftoff.txt", "leftoff", str(trade / "recent"),
                                str(trade / "desec"), str(trade / "armorydir"), tables=S13_TABLES)),
    ]
    # Season 14's ladder: a sample in fast, whole above it.
    jobs += audit_jobs("ladder-s14", MID_SAMPLE if tier == "fast" else 1, run_dir)
    if tier in ("mid", "full"):
        jobs.append(("roll2", lambda: run(run_dir / "roll2.txt", "rollcheck", spec, str(trade / "recent"),
                                          str(trade / "desec"), "--twice", tables=S13_TABLES)))
        if TABLES_S12.exists():
            jobs.append(("legit12", lambda: run(run_dir / "legit12.txt", "legit", "--corpus", str(PACK_S12),
                                                "--tables", str(TABLES_S12), "--items")))
        jobs += audit_jobs("ladder", 1 if tier == "full" else MID_SAMPLE, run_dir)

    started = time.time()
    with ThreadPoolExecutor(max_workers=jobs_at_once) as pool:
        texts = dict(zip([name for name, _ in jobs], pool.map(lambda job: job[1](), jobs)))
    seconds = time.time() - started

    lines, failed, unread = [], 0, 0

    def say(ok: bool | None, what: str, detail: str):
        nonlocal failed, unread
        failed += ok is False
        unread += "no summary line" in detail
        mark = {True: "pass", False: "FAIL", None: "note"}[ok]
        lines.append(f"{mark:4}  {what:<14} {detail}")

    layers = subprocess.run([sys.executable, str(REPO / "tests/layers.py")], capture_output=True, text=True)
    say(layers.returncode == 0, "layers", (layers.stdout.strip().splitlines() or ["no output"])[-1])
    for line in layers.stdout.splitlines():
        if line.startswith("NEW"):
            lines.append(f"            {line}")
    docs = subprocess.run([sys.executable, str(REPO / "tests/docs.py")], capture_output=True, text=True)
    say(docs.returncode == 0, "docs", (docs.stdout.strip().splitlines() or ["no output"])[-1])
    for line in docs.stdout.splitlines()[:-1][:10]:
        lines.append(f"            {line}")
    # Every string a player reads through the language framework.
    found = subprocess.run([sys.executable, str(REPO / "tests/words.py")], capture_output=True, text=True, encoding="utf-8",
                           env={**os.environ, "PYTHONIOENCODING": "utf-8"})
    say(found.returncode == 0, "words", (found.stdout.strip().splitlines() or ["no output"])[-1])
    for line in found.stdout.splitlines()[:-1][:10]:
        lines.append(f"            {line}")

    ok, detail, _ = built_check(texts["stress35"])
    say(ok, "stress35", detail)

    clean = re.search(r"forged (\d+): silent (\d+), findings (\d+)", texts["clean"])
    say(bool(clean) and clean.group(1) == clean.group(2) and clean.group(3) == "0", "cleancheck",
        clean.group(0) if clean else "no summary line")

    for key in ("roll", "roll2"):
        if key in texts:
            roll = re.search(r"(\d+) items .*?(\d+) rolls: flagged (\d+), no affix at all (\d+)", texts[key])
            say(bool(roll) and roll.group(3) == "0" and roll.group(4) == "0", "rollcheck" + (" x2" if key == "roll2" else ""),
                f"{roll.group(2)} rolls, flagged {roll.group(3)}, no affix {roll.group(4)}" if roll else "no summary line")

    # Silence says a line is allowed, not that it holds the same number: seeded rolls are held to the baseline's bytes.
    for key, name in (("clean", "clean bytes"), ("roll", "roll bytes"), ("roll2", "roll x2 bytes")):
        if key in texts:
            now = re.search(r"^bytes (\w+)", texts[key], re.M)
            file = CORPUS / "baseline" / f"{key}.txt"
            then = re.search(r"^bytes (\w+)", file.read_text(encoding="utf-8", errors="replace"), re.M) if file.exists() else None
            if then is None:
                say(None, name, "no baseline yet: --accept")
            else:
                say(bool(now) and now.group(1) == then.group(1), name,
                    "same as the baseline" if now and now.group(1) == then.group(1) else "differ from the baseline")

    left = re.search(r"(\d+) listings rebuilt, (\d+) whole", texts["leftoff"])
    say(bool(left) and left.group(1) == left.group(2), "leftoff", left.group(0) if left else "no summary line")

    if "legit12" in texts:
        silent = re.search(r"(\d+) silent \(([0-9.]+)%\)", texts["legit12"])
        items = sorted(line for line in texts["legit12"].splitlines() if LEGIT_ITEM.match(line))
        (run_dir / "items.txt").write_text("\n".join(items) + "\n", encoding="utf-8")
        base = CORPUS / "baseline/items.txt"
        before = set(base.read_text(encoding="utf-8").splitlines()) if base.exists() else set()
        newly = [item for item in items if item not in before]
        cleared = len(before - set(items))
        # No summary is the command having stopped, not an item-less pack: nothing was checked, and the list is empty.
        say(bool(silent) and not newly, "legit S12",
            f"{silent.group(0) if silent else 'no summary line'}; newly flagged {len(newly)}, cleared {cleared}")
        for item in newly[:15]:
            lines.append(f"            new: {item[:150]}")
    elif tier != "fast":
        say(None, "legit S12", f"skipped, no pack at {PACK_S12}")

    known = {line.strip() for line in KNOWN.read_text(encoding="utf-8").splitlines()
             if line.strip() and not line.startswith("#")} if KNOWN.exists() else set()
    for folder in SEASON_LADDERS:
        whole = (folder == "ladder-s14" and tier != "fast") or (folder == "ladder" and tier == "full")
        for ladder in LADDERS:
            parts = [text for key, text in texts.items() if key.startswith(f"{folder}/{ladder} ")]
            if not parts:
                continue
            totals, all_ok = [0] * 6, True
            for text in parts:
                ok, _, n = built_check(text, known if folder == "ladder-s14" else None)
                all_ok &= ok
                totals = [a + b for a, b in zip(totals, n)]
            name = ("S14 " if folder == "ladder-s14" else "S13 ") + ladder + ("" if whole else f" 1/{MID_SAMPLE}")
            say(all_ok, name,
                f"{totals[0]} built, {totals[1]} failed; items {totals[2]}/{totals[3]}; silent {totals[4]}/{totals[5]}")
            for text in parts:
                for line in text.splitlines():
                    if line.startswith("  --") or line.startswith("     ! "):
                        lines.append(f"            {line.strip()[:150]}")

    lines.append(f"{tier} tier, {seconds / 60:.1f} min, {jobs_at_once} at once; outputs in {run_dir}")
    report = "\n".join(lines)
    (run_dir / "summary.txt").write_text(report + "\n", encoding="utf-8")
    print(report)

    # A real change may be accepted, a failure among it; a step that said nothing has nothing to accept, and its empty
    # list would take the place of the baseline's.
    if "--accept" in args and unread:
        print(f"not accepted: {unread} step(s) gave no summary line")
        return 1

    if "--accept" in args:
        for file in run_dir.glob("*.txt"):
            if file.name != "summary.txt":
                (CORPUS / "baseline" / file.name).write_bytes(file.read_bytes())
        print("accepted as the new baseline")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
