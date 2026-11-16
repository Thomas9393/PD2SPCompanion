"""The new-season checklist, runnable.

    python tests/season.py            run every re-check and compare with the recorded baseline
    python tests/season.py --record   run them and record the answers as the baseline

Run it the day a PD2 season lands, before playing, with the new archives installed. Each line is
one checklist item: what it measured now, what the baseline said, and CHANGED where they differ.
A change is not a failure - it is what the checklist says to look at, with the note to open.
Exit code: 0 nothing changed, 1 something changed or failed to run.
"""
import json
import re
import subprocess
import sys
import time

from paths import CORPUS, REPO

CLI = REPO / "src/PD2SPCompanion.Cli/bin/Debug/net10.0/PD2SPCompanion.Cli.exe"
BASELINE = CORPUS / "baseline" / "season.json"
SAVES = CORPUS / "saves"


def cli(*args: str) -> str:
    done = subprocess.run([str(CLI), *args], capture_output=True, text=True, encoding="utf-8", errors="replace")
    return done.stdout + done.stderr


def table_rows(name: str) -> tuple[list[str], list[list[str]]]:
    lines = cli("table", name, "--rows", "100000").splitlines()
    header = [cell.strip() for cell in lines[1].split("|")]
    return header, [[cell.strip() for cell in line.split("|")] for line in lines[2:] if "|" in line]


def stat_widths() -> dict:
    """A1-A3: the narrowed stats' save widths now and last season's, from itemstatcost."""
    header, rows = table_rows("itemstatcost")
    # The newest: S14's table carries S12's columns and S13's, and S13's is last season's.
    last = max((h for h in header if re.fullmatch(r"Save Bits S\d+", h)), key=lambda h: int(h[len("Save Bits S"):]), default=None)
    found = {}
    for row in rows:
        cells = dict(zip(header, row))
        if cells.get("ID") in ("206", "207", "375", "490"):
            found[f"{cells['Stat']} ({cells['ID']})"] = f"{cells.get('Save Bits')} bits, {last or 'no last-season column'}: {cells.get(last, '-') if last else '-'}"
    return found


def row_values() -> dict:
    """A1-A2: what the data asks of the narrowed stats."""
    header, rows = table_rows("uniqueitems")
    found = {}
    for row in rows:
        cells = dict(zip(header, row))
        if cells.get("index") in ("Eaglehorn", "Imperial Palace Map"):
            for n in range(1, 13):
                if cells.get(f"prop{n}", "").startswith(("eaglehorn", "map-glob-monsterrarity")):
                    found[f"{cells['index']} {cells[f'prop{n}']}"] = f"{cells.get(f'min{n}')}-{cells.get(f'max{n}')}"
    return found


def summary(pattern: str, text: str) -> str:
    match = re.search(pattern, text)
    return match.group(0).strip() if match else "(no summary line)"


def season_facts() -> dict:
    """Item 10 of the season pipeline: what the code knows about the season that no table says (SeasonFacts)."""
    facts = {}
    for line in cli("seasonfacts").splitlines():
        if not line.strip():
            break  # where each is written follows, after a blank line
        name, _, state = line.partition(": ")
        if state:
            facts[name] = state
    return facts


def checks() -> dict:
    results = {}
    results["season facts in code"] = season_facts()
    results["A1-A3 stat widths"] = stat_widths()
    results["A1-A2 data asks"] = row_values()
    results["B1 saves read back (guard)"] = summary(r"\d+ of \d+ read back exactly", cli("guard", str(SAVES)))
    results["B4-B5 cleancheck"] = summary(r"forged \d+: silent \d+, findings \d+", cli("cleancheck", "--baseline"))
    for save in ("PhySpearZon.d2s", "pd2_shared.stash"):
        results[f"B6 legit {save}"] = summary(r"\d+ silent \([0-9.]+%\)", cli("legit", str(SAVES / save)))
    results["B7 multi-field properties"] = summary(r"\d+ properties write more than one field; \d+ disagree with the table", cli("props"))
    desec = CORPUS / "trade" / "desec"
    if desec.exists():
        results["B8 desecrated listings"] = summary(r".*rebuilt.*", cli("desecscan", str(desec)))
    results["B10 skin pictures"] = str(len([line for line in cli("files", "_se.").splitlines() if "_se." in line])) + " files"
    tests = subprocess.run(["dotnet", "test", str(REPO / "tests/PD2SPCompanion.Tests"), "--nologo", "-v", "q"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
    # Whether any failed, not how many ran: the count grows with every test added, which is not the season.
    failed = re.search(r"Failed:\s*(\d+)", tests.stdout)
    results["unit tests"] = "no summary line" if not failed else "all passed" if failed.group(1) == "0" else f"{failed.group(1)} failed"
    return results


def main() -> int:
    record = "--record" in sys.argv
    started = time.time()
    # The CLI built first: a stale one answered for the code of days before (the S14 beta's
    # previous-season columns read as S12 by a binary that predated the fix).
    built = subprocess.run(["dotnet", "build", str(REPO / "src/PD2SPCompanion.Cli"), "--nologo", "-v", "q"],
                           capture_output=True, text=True)
    if built.returncode != 0:
        print(built.stdout[-2000:])
        return 1
    now = checks()
    if record:
        BASELINE.write_text(json.dumps(now, indent=1), encoding="utf-8")
        print(f"baseline recorded: {BASELINE}")
        return 0

    before = json.loads(BASELINE.read_text(encoding="utf-8")) if BASELINE.exists() else {}
    changed = 0
    for key, value in now.items():
        was = before.get(key)
        mark = "same   " if was == value else "CHANGED"
        changed += was != value
        print(f"{mark}  {key}: {json.dumps(value)}")
        if was != value:
            print(f"         was: {json.dumps(was)}")
    print(f"\n{changed} changed, {time.time() - started:.0f} s. SeasonFacts.cs says what each means.")
    return 1 if changed else 0


if __name__ == "__main__":
    sys.exit(main())
