"""The one way in to verifying a change.

    python tests/check.py quick             after every change: under a minute, no window
    python tests/check.py quick --no-game   what a clone can run: no game, no corpus
    python tests/check.py changed [--ui]    quick, and what the changed files call for
    python tests/check.py full              everything - only when asked for

quick     builds the solution, runs the unit tests but the slow ones (Trait Speed=Slow), and the static checks -
          the words, the doc comments, the layers.
changed   quick; then the slow unit tests and `regress.py fast` where Core, the save parser or the command line
          tool changed; and it names the UI cases the changed files call for (tests/ui/areas.txt). They take
          the mouse, so they are run only with --ui.
full      the solution, every unit test, the static checks, `regress.py full`, and with --ui every UI case
          (tests/ui/run.ps1).

The tables each part runs on are set here, never by hand: the installed season's, but for the Core tests of
Season=S13, on S13's snapshot (S13_TABLES). The Shell suite's and the UI cases' pads are S14's
(`pd2spc padseason`; S13's kept in TestCorpus/ui-pad/s13).

Before this there were six or seven commands, typed in the order remembered. Every run appends what it ran and
the seconds each part took to <corpus>/runs/checks.csv, so the next review of the tests is of measurements.

The first part that fails ends the run, said as it ended. Exit code 0 when everything passed.

Every level needs the game, the corpus and S13's tables (tests/paths.py says where they are found), and stops at once
without them. A test skipped fails the part it is in. --no-game is the other way to run: it uses none of the three,
whatever this machine has (PD2SPC_NO_GAME=1 to the test projects), leaves out what stands on them - the S13 pass, the
regression, the UI cases - allows the skips, and says at the end what was left out.
"""

import csv
import fnmatch
import os
import re
import shutil
import subprocess
import sys
import tempfile

# A test's own words go to the console as they are: an arrow in a sort order's name stopped the whole check on a
# console that could not print it (cp1252), in place of the failure it was reporting.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(errors="replace")
import time
from pathlib import Path

from paths import CORPUS, REPO, S13_TABLES, missing

RECORD = CORPUS / "runs" / "checks.csv"
AREAS = REPO / "tests" / "ui" / "areas.txt"

# The season before's tables (the app's own snapshot), for what was measured in it: the Core tests pinned to S13
# (trait Season=S13: its game pages, specimens and counts). Everything else runs on the installed tables (S14,
# the Shell suite's and the UI cases' pads too).
ON_S13 = {"PD2SPC_TABLES": str(S13_TABLES), "PD2SPC_EXPECT_SEASON": "S13"}

# What a change under these calls the regression and the slow unit tests for.
DEEP = ("src/PD2SPCompanion.Core/", "src/PD2SPCompanion.D2S/", "src/PD2SPCompanion.Cli/", "tests/regress.py")

parts = []  # (name, seconds, passed, said)
no_game = False  # --no-game
skipped_total = 0
left_out = []  # the parts --no-game did not run

# A test project's summary line, as `dotnet test` writes it.
SUMMARY = re.compile(r"(Passed|Failed)!\s+- Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+).*Duration: ([^-]+) - PD2SPCompanion\.(Shell\.)?Tests")


def skipped_in(text: str) -> int:
    """How many tests a part's output says were skipped."""
    return sum(int(found.group(4)) for found in SUMMARY.finditer(text))


def run(name: str, command: list[str], say=None, env: dict | None = None) -> bool:
    """One part: run, timed, its last word kept; False when it failed, or when a test was skipped: a test skipped has
    looked at nothing. `env` is laid over this one's environment; PD2SPC_TABLES is taken off it first, so a part runs
    on the installed tables unless it asks for others."""
    print(f"-- {name}", flush=True)
    started = time.time()
    environment = {key: value for key, value in os.environ.items() if key not in ("PD2SPC_TABLES", "PD2SPC_NO_GAME")}
    environment.update(env or {})
    if no_game:
        environment["PD2SPC_NO_GAME"] = "1"
    done = subprocess.run(command, cwd=REPO, capture_output=True, text=True, encoding="utf-8", errors="replace",
                          env=environment)
    seconds = time.time() - started
    text = (done.stdout or "") + (done.stderr or "")
    global skipped_total
    skipped = skipped_in(text)
    skipped_total += skipped
    passed = done.returncode == 0 and (no_game or skipped == 0)
    word = say(text) if say else (text.strip().splitlines() or [""])[-1]
    if done.returncode == 0 and not passed:
        word += " - a test skipped has looked at nothing (--no-game if that is meant)"
    parts.append((name, seconds, passed, word))
    print(f"   {'pass' if passed else 'FAIL'}  {seconds:5.1f} s  {word[:150]}", flush=True)
    if not passed:
        tail = [line for line in text.strip().splitlines() if line.strip()][-25:]
        print("\n".join("   | " + line[:200] for line in tail), flush=True)
    return passed


def last(pattern: str):
    """The last line of a part's output that holds the pattern, for its one-line word."""
    def pick(text: str) -> str:
        found = [line.strip() for line in text.splitlines() if pattern in line]
        return found[-1] if found else (text.strip().splitlines() or [""])[-1]
    return pick


def build_said(text: str) -> str:
    if "MSB3027" in text or "MSB3021" in text:
        return "the program is running and holds its files: close it and run again"
    return last("Error(s)")(text)


def unit_said(text: str) -> str:
    """Each test project's own last line, in a few words: "Core 321 passed, 2 skipped in 13 s; Shell 11 passed in 1 s"."""
    said = []
    for found in SUMMARY.finditer(text):
        which = "Shell" if found.group(6) else "Core"
        failed, skipped = int(found.group(2)), int(found.group(4))
        said.append(f"{which} {found.group(3)} passed{f', {failed} FAILED' if failed else ''}"
                    f"{f', {skipped} skipped' if skipped else ''} in {found.group(5).strip()}")
    return "; ".join(said) if said else (text.strip().splitlines() or [""])[-1]


def changed_files() -> list[str]:
    """What differs from the last commit, new files among it."""
    out = subprocess.run(["git", "status", "--porcelain", "--untracked-files=all"], cwd=REPO, capture_output=True, text=True).stdout
    found = []
    for line in out.splitlines():
        path = line[3:].strip().strip('"')
        if " -> " in path:
            path = path.split(" -> ")[1].strip('"')
        found.append(path.replace("\\", "/"))
    return found


def tags_for(files: list[str]) -> list[str]:
    """The UI cases' tags the changed files call for: tests/ui/areas.txt, a pattern and its tags to a line."""
    if not AREAS.exists():
        return []
    tags = []
    for line in AREAS.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.lstrip().startswith("#") or "\t" not in line:
            continue
        pattern, wearing = (piece.strip() for piece in line.split("\t", 1))
        if any(fnmatch.fnmatch(path, pattern) for path in files):
            tags += [tag.strip() for tag in wearing.split(",") if tag.strip()]
    return sorted(set(tags))


def pads() -> tuple[Path, Path, Path]:
    """Copies of the two pads to write on, in a folder of the run's own."""
    at = Path(tempfile.mkdtemp(prefix="pd2spc-check-"))
    shutil.copytree(CORPUS / "ui-pad" / "pad", at / "pad")
    shutil.copytree(CORPUS / "ui-pad" / "cardpad", at / "card")
    return at, at / "pad", at / "card"


def powershell(script: Path, *arguments: str) -> list[str]:
    return ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script), *arguments]


def game_runs() -> bool:
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Game.exe"], capture_output=True, text=True).stdout
    return "Game.exe" in out


def main() -> int:
    arguments = [a for a in sys.argv[1:] if not a.startswith("--")]
    level = arguments[0] if arguments else ""
    if level not in ("quick", "changed", "full"):
        print(__doc__)
        return 2
    with_ui = "--ui" in sys.argv
    global no_game
    no_game = "--no-game" in sys.argv
    run_name = f"{level} --no-game" if no_game else level

    # A folder missing is taken quietly by what reads it: a tables folder gone left the installed tables in place and the
    # S13 pass said "34 passed" with nothing looked at. Said here, before anything is run.
    if not no_game and missing():
        print("\n".join(missing()))
        print(f"This run stands on them. `python tests/check.py {level} --no-game` runs what a clone can, and says what it left out.")
        return 1

    started = time.time()
    files = changed_files() if level == "changed" else []
    deep = level == "full" or any(path.startswith(DEEP) for path in files)
    tags = tags_for(files) if level == "changed" else []

    def steps():
        yield run("build", ["dotnet", "build", "PD2SPCompanion.slnx", "-v", "q", "--nologo"], build_said)

        # Both test projects on the installed tables - the Core's S13-pinned ones on S13's - the view models' and services'
        # (the program without its window) on their S14 pad.
        def unit(project: str, only: str | None = None) -> list[str]:
            wanted = [part for part in (only, None if deep else "Speed!=Slow") if part]
            return (["dotnet", "test", f"tests/{project}", "--no-build", "--nologo"]
                    + (["--filter", "&".join(wanted)] if wanted else []))

        # These two look at a running game's files by leaving them alone: they return, and count as passed.
        if not no_game and game_runs():
            print("-- note: the game is running: CharacterMigrationTests and EncodingRepairTests look at nothing this run", flush=True)

        slow = "" if deep else " (not the slow ones)"
        yield run(f"unit tests, Core{slow}", unit("PD2SPCompanion.Tests"), unit_said)

        if no_game:
            left_out.append("unit tests, Core pinned to S13")
        else:
            yield run("unit tests, Core pinned to S13", unit("PD2SPCompanion.Tests", "Season=S13"), unit_said, ON_S13)

        yield run(f"unit tests, Shell{slow}", unit("PD2SPCompanion.Shell.Tests"), unit_said)

        yield run("words", [sys.executable, "tests/words.py"])
        yield run("doc comments", [sys.executable, "tests/docs.py"])
        yield run("layers", [sys.executable, "tests/layers.py"])
        if (REPO / "tests" / "public.py").exists():
            yield run("public", [sys.executable, "tests/public.py"])
        yield run("lifts", [sys.executable, "tests/lifts.py"])
        yield run("UI cases, read", [sys.executable, "tests/cases.py"])

        if deep and no_game:
            left_out.append("regression")
        elif deep:
            yield run(f"regression ({'full' if level == 'full' else 'fast'})",
                      [sys.executable, "tests/regress.py", "full" if level == "full" else "fast", "--no-build"],
                      last("tier,"))

        if no_game:
            if with_ui or level == "full" or tags:
                left_out.append("UI cases")
            return

        if level == "changed":
            if not tags:
                print("-- UI cases: the changed files call for none (tests/ui/areas.txt)", flush=True)
            elif not with_ui:
                print(f"-- UI cases: tagged {', '.join(tags)} - they take the mouse; run again with --ui", flush=True)

        if with_ui and (level == "full" or tags):
            if game_runs():
                print("-- UI cases: not run - the game is running", flush=True)
                parts.append(("UI cases", 0.0, False, "the game is running"))
                yield False
                return
            folder, pad, card = pads()
            try:
                cases = ["-Pad", str(pad), "-CardPad", str(card)]
                if level == "changed":
                    cases += ["-Tag", ",".join(tags)]
                yield run("UI cases", powershell(REPO / "tests/ui/run.ps1", *cases), last("cases in"))
            finally:
                shutil.rmtree(folder, ignore_errors=True)

    passed = True
    for ok in steps():
        if not ok:
            passed = False
            break

    total = time.time() - started
    print(f"== {run_name}: {'passed' if passed else 'FAILED'} in {total:.0f} s", flush=True)
    if no_game:
        print(f"   left out: {'; '.join(left_out + [f'{skipped_total} tests skipped']) if passed else 'the run ended early'}", flush=True)

    # The record is the corpus's: with no corpus, nowhere to keep it.
    if not CORPUS.is_dir():
        return 0 if passed else 1

    try:
        RECORD.parent.mkdir(parents=True, exist_ok=True)
        new = not RECORD.exists()
        with RECORD.open("a", newline="", encoding="utf-8") as record:
            writer = csv.writer(record)
            if new:
                writer.writerow(["when", "level", "part", "seconds", "result", "said"])
            when = time.strftime("%Y-%m-%d %H:%M:%S")
            for name, seconds, ok, word in parts:
                writer.writerow([when, run_name, name, f"{seconds:.1f}", "pass" if ok else "FAIL", word[:200]])
            writer.writerow([when, run_name, "(all)", f"{total:.1f}", "pass" if passed else "FAIL", ""])
    except OSError as problem:
        print(f"   (the record was not written: {problem})")

    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
