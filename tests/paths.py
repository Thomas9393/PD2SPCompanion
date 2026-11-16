"""Where this machine keeps what the tests stand on, for every Python runner. The one place for it.

Each location is taken from its environment variable, else from the local file, else it is not known:

    PD2SPC_GAME         the Diablo II folder PD2 is installed into
    PD2SPC_CORPUS       the corpus of real saves (TestCorpus)
    PD2SPC_S13_TABLES   Season 13's tables, the snapshot the tests pinned to S13 run on

The local file is `pd2spc.local.env`, one `NAME=value` a line and never committed. It is looked for in the repo's
folder and then up its parents, so that one file serves a checkout and every worktree inside it. tests/TestPaths.cs
(the unit tests) and tests/paths.ps1 (the UI cases) read the same file by the same rule.

What is not known is a path that does not exist, named for the variable, so a runner's own `.exists()` says so and
its message names what to set. What is known is put in this process's environment, for what it starts: the command
line tool takes --game or PD2SPC_GAME and reads no file.
"""

import os
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
LOCAL_FILE = "pd2spc.local.env"


def _local() -> dict[str, str]:
    """The nearest local file's lines: this repo's folder first, then each parent."""
    for folder in (REPO, *REPO.parents):
        file = folder / LOCAL_FILE
        if file.is_file():
            pairs = (line.split("=", 1) for line in file.read_text(encoding="utf-8-sig").splitlines()
                     if "=" in line and not line.lstrip().startswith("#"))
            return {name.strip(): value.strip() for name, value in pairs}
    return {}


_LOCAL = _local()


def _where(name: str) -> Path:
    value = os.environ.get(name) or _LOCAL.get(name)
    if not value:
        return Path(f"{name}-not-set")
    os.environ[name] = value
    return Path(value)


GAME = _where("PD2SPC_GAME")
CORPUS = _where("PD2SPC_CORPUS")
S13_TABLES = _where("PD2SPC_S13_TABLES")


def missing() -> list[str]:
    """What is not there, a line each, naming what sets it."""
    return [f"no {what} at {path} ({name}, or {LOCAL_FILE})"
            for what, path, name in (("game", GAME, "PD2SPC_GAME"),
                                     ("corpus", CORPUS, "PD2SPC_CORPUS"),
                                     ("S13 tables", S13_TABLES, "PD2SPC_S13_TABLES"))
            if not path.is_dir()]
