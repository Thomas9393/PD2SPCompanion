# UI cases

What only a window can show - where something is drawn, that a click, a drag or a key reaches the right
thing, what the view itself supplies - proven by driving the running program through UI Automation. What a
view model or a service *decides* is not tested here: that is a unit test in
`tests/PD2SPCompanion.Shell.Tests`, the program without a window, milliseconds each.

## Running

```
powershell -File tests/ui/run.ps1 -List                 the cases there are, with their tags; nothing run
powershell -File tests/ui/run.ps1 -Tag vault,forge      the cases wearing any of these tags
powershell -File tests/ui/run.ps1 -Only search.*        cases by name; * stands for anything
powershell -File tests/ui/run.ps1                       every case
powershell -File tests/ui/run.ps1 -Left                 only what an earlier round has not confirmed
python tests/check.py changed --ui                      the cases the changed files call for (areas.txt)
```

A run **takes the mouse and the keyboard**. It refuses to start while the game runs, and a case that types
letters (`-Typing`) is skipped under an input language other than English.

The runner works on **copies**, never the player's own: it copies `TestCorpus\ui-pad\pad` and `\cardpad`
to the temp folder for itself, and the program's settings too (begun from the developer's, for the game
folder). `Launch` points the program at them (`PD2SPC_SAVES`, `PD2SPC_VAULTS`, `PD2SPC_LOG`,
`PD2SPC_FONTS`, `PD2SPC_DATA`), and the program shows a red TEST COPY badge.

Cases that start alike share one start of the program. After a case that changes what is saved the pad is put
back - under the running program where the case changed the pad and nothing else, else by closing it. A case
that fails in a program it shared is tried once more in one of its own, and reported apart ("pass alone").
Each case is recorded as it ends in the corpus's `runs\ui-cases.csv` - which is what `-Left` reads; what each
said is in `<pad>\ui-logs\<case>.txt`.

## Writing a case

`kit.ps1` says what a case is and what its switches mean; `units\search.ps1` and `units\right-click.ps1`
are the shortest examples. One thing per case, named `'<file>.<what-it-proves>'`, wearing its area's tags
(`areas.txt` maps source files to tags). A case begins on the Items page - the inventory on the left, Edit
on the right, the pad's character loaded, no search standing - and must not lean on what the case before it
left. A new check is a new case, or a unit test: never a section added to an existing case.

## The driving (`fast.ps1`)

The helpers every case may call, and the policy they follow:

1. Wait for a condition, never a fixed time: `WaitUntil` polls every 50 ms and fails loudly, naming what
   it waited for, with the window as it stood in `<pad>\ui-fail.png`.
2. Targeted lookups: `ById`, `ByTip`, `ByName` (case-insensitive), one search condition each.
3. Any error ends the case (`$ErrorActionPreference = 'Stop'`).
4. Every click goes through `Front`, which brings the program forward and refuses to click if it is not in
   front. Nothing is clicked that was not found.

A page fading in after a bookmark switch, or redrawing after a save, can swallow a click, and an element
can vanish while it is being read. For a select, use `ClickUntil { click } { shown }`: it clicks, gives it
400 ms to show, and clicks again, up to five times. Waits treat an element that has just gone as "not yet".
`ByName -Prefix` matches a text that goes on or ends in padding.

An item is found by what it is: `Tile "Duriel's Shell"` finds its tile in a grid, a worn slot or the
mercenary's; `VaultTile` its tile on the vault's sheet. Tiles carry the automation id `Tile` and the item's
name, and `ByName` skips them, so a name looked up on the Edit page is never the tile. Window points
(`ClickAt`) remain only for an empty cell.

A helper a units file defines for itself must not take the name of one of `fast.ps1`'s or `kit.ps1`'s
(`Kept`, `VaultRow`, `Standing`...): it would replace that one for every later case of the run. Nor the
name of a PowerShell alias or command (`Where`, `Type`, `Group`, `Compare`): the alias wins, and the
function is never called.

Automation Label vs Border: a `Border` has no automation peer, so an element a case must find (the health
mark) is a `Label`.

`pseudo-language.ps1` is not a case: a tour to read.
