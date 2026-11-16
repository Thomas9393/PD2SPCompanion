# Small tests, each of one thing: scoped runs not strongly linked to the issue to test,
# refactored into more precise and smaller test units, and a suite with less overhead and repetition.
# Until then 112 scripts stood beside this file, some 200 separate things in
# bundles - one of them twelve - so the smallest thing that could be run for a change was a minute of mostly other
# matters, each script starting the program for itself.
#
# A CASE is one thing: a name that says what it proves, a few checks, where it starts, and whether it changes
# what is saved. Cases live in tests/ui/units/*.ps1 and are run by run.ps1, which picks them by name or tag,
# lets cases that start alike share one start of the program, and puts the pad back only after one that changed
# it. This file is the kit they are written with; fast.ps1 is still the driving under it.
#
#   Case 'search.box-reopens-its-list' -Tags search { ...; Check (...) "what was looked at" }
#
#   -Start   where the case begins: Items (a character open on the Items page - the usual) or Home (the
#            program as it opens, no character). The runner brings the program there before the case; a case
#            leaves the program wherever it ends.
#   -Changes the case changes a save, the stash, the vault or the settings: the pad is put back after it, so
#            the next case starts from the pad as it was handed in - under the running program where that can
#            be (Reset-Pad below), else by closing it and starting it again.
#   -Restart the case leaves the PROGRAM changed in a way putting the pad back does not undo - a part resized,
#            a look not kept in the settings file: the program is closed after it.
#   -Mode    the play mode it runs in (Free, unless said): cases of one mode share a start.
#   -Pad     pad (the shared one) or card (the one characters are made on).
#   -Typing  it types letters through the keyboard: not run under another input language.
#   -Before  what the case needs made before the program starts - a character, an item given with the command
#            line tool ($script:Cli) - on the pad ($pad), which is as it was handed in. Such a case has a start
#            of the program to itself, and the pad is put back after it.
#
# What a unit file needs beyond its cases - a helper or two of its own - it defines at its top; the runner loads
# the file afresh for each of its cases, so two files may name a helper alike. Not as the kit names one: run.ps1
# refuses a unit that does.

$script:Cases = New-Object System.Collections.Generic.List[object]
$script:CaseFile = ''
$script:failed = @()

function Case {
  param(
    [Parameter(Mandatory, Position = 0)][string]$Name,
    [Parameter(Mandatory, Position = 1)][scriptblock]$Body,
    [string[]]$Tags = @(),
    [ValidateSet('Items', 'Home')][string]$Start = 'Items',
    [string]$Mode = 'Free',
    [ValidateSet('pad', 'card')][string]$Pad = 'pad',
    [switch]$Changes,
    [switch]$Typing,
    [switch]$Restart,
    [scriptblock]$Before)
  $script:Cases.Add([pscustomobject]@{
      Name = $Name; Body = $Body; Tags = $Tags; Start = $Start; Mode = $Mode; Pad = $Pad
      Changes = [bool]$Changes -or [bool]$Before -or [bool]$Restart; Typing = [bool]$Typing; Restart = [bool]$Restart
      Before = $Before; File = $script:CaseFile })
}

# The command line tool, for what a case makes before the program starts.
$script:Cli = "$PSScriptRoot\..\..\src\PD2SPCompanion.Cli\bin\Debug\net10.0\PD2SPCompanion.Cli.exe"

# The vault a case on this pad meets, read from the pad: its newest season's folder (a pad keeps the season before's
# beside it), and that season's first vault. Never a season written into a unit: the units that named S13 went on
# reading a file nobody wrote once the pads moved to S14, and a count that never changes passes a check of no change
# (tests/cases.py refuses it now).
function PadSeason {
  (Get-ChildItem "$pad\vtsmart" -Directory | Where-Object { $_.Name -match '^S\d+$' } | Sort-Object { [int]$_.Name.Substring(1) } | Select-Object -Last 1).Name
}
function MainVault { "$(PadSeason) Main" }
function OpenVaultFile { "$pad\vtsmart\$(PadSeason)\Softcore\$(MainVault).pd2lib" }
function VaultCount { if ((& $script:Cli vault --file (OpenVaultFile) --list 2>&1 | Out-String) -match "\(season $(PadSeason), (\d+) items\)") { [int]$Matches[1] } }

# One thing looked at: said, and counted against the case where it is not so.
function Check([bool]$ok, [string]$what) {
  if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:failed += $what }
}

# ---- the program, shared by cases that start alike ------------------------------------------------------------

# The program running for the cases: which pad it was started on, in which play mode, and whether a case has
# since changed what is saved - then it is not shared any further.
$script:session = $null

function Start-Session([string]$pad, [string]$mode) {
  UsePlayMode $mode
  # The file the program is asked by to let go of the pad (Reset-Pad): nothing of a start before left lying.
  $env:PD2SPC_TEST_RESET = "$pad\reset.ask"
  Remove-Item "$pad\reset.ask", "$pad\reset.ask.closed", "$pad\reset.ask.refused" -Force -ErrorAction SilentlyContinue
  Launch $script:EditorExe $pad -NoTyping
  $script:session = @{ Pad = $pad; Mode = $mode; Dirty = $false; Reset = $false }
}

function End-Session {
  if ($script:session) { Close }
  $script:session = $null
}

# The pad put back under the running program (the program has its part:
# App.WatchForPadReset, Debug builds only). Asked by a file, the program closes the character as its Close button
# does and lets go of the vault; the files are put back; the file taken away, it opens the vault again. The next
# case loads the character afresh. A start of the program was 3.6 s and the load after it 2.1 (measured), and 82
# of the 254 cases asked for one only to have the pad as it was.
# False where the program did not answer, or would not (something unsaved it would have had to ask about): the
# runner then closes it and starts it again, as it always did.
# The files are asked for with File.Exists, which answers no where Test-Path throws: a file the program is that
# moment deleting is "access denied", and that ended a whole run.
function Reset-Pad([scriptblock]$putBack) {
  $ask = $env:PD2SPC_TEST_RESET
  if (-not $ask -or -not $script:session) { return $false }
  Remove-Item "$ask.refused" -Force -ErrorAction SilentlyContinue
  New-Item -ItemType File $ask -Force | Out-Null
  $waited = [Diagnostics.Stopwatch]::StartNew()
  while (-not ([IO.File]::Exists("$ask.closed")) -and -not ([IO.File]::Exists("$ask.refused")) -and $waited.ElapsedMilliseconds -lt 4000) { Start-Sleep -Milliseconds 30 }
  if (-not ([IO.File]::Exists("$ask.closed"))) { Remove-Item $ask, "$ask.refused" -Force -ErrorAction SilentlyContinue; return $false }

  try { & $putBack | Out-Null } catch { Remove-Item $ask -Force -ErrorAction SilentlyContinue; return $false }

  Remove-Item $ask -Force
  $waited.Restart()
  while (([IO.File]::Exists("$ask.closed")) -and $waited.ElapsedMilliseconds -lt 4000) { Start-Sleep -Milliseconds 30 }
  if ([IO.File]::Exists("$ask.closed")) { return $false }

  # The program is no longer as it opened: a case that starts at Home wants one that is (Reach).
  $script:session.Reset = $true
  # And the pad's files are as handed in again: what Kept waits to see change is measured from here.
  $script:keptAt = PadStamps
  $true
}

function IsLoaded { $items = ById 'SideBar_Inventory'; [bool]($items -and $items.Current.IsEnabled) }

function PressEsc { Front; [Fast]::keybd_event(0x1B, 0, 0, 0); [Fast]::keybd_event(0x1B, 0, 2, 0); Start-Sleep -Milliseconds 150 }

# ---- the item search, on the right page under its Search bookmark --------------------------------------------
# For the cases that ask it. What it asks is kept on its bookmark when the bookmark is left, and asked again
# when it is come back to: a case that asked something empties it before it ends (ItemSearchEnd), or the case
# after it that takes the bookmark would begin on its search.

# The Search page shown, by its bookmark. Clicked whether or not the page seems up: the bookmark that is the
# page already does nothing, and a page seen is not a page up - the right page fades from one bookmark to the
# next, and the Search page of the case before was still found while it faded out under Edit (a case that began
# so waited for a page that was going).
function ItemSearch {
  ClickEl (WaitUntil { ById 'Bookmark_Search' } "the Search bookmark" 4000) "Search, on the right page"
  WaitUntil { ById 'Found_Conditions' } "the Search page" 4000 | Out-Null
  # Folded, or only not drawn yet: the keyword box is given a moment before the heading is clicked. Asked for at
  # once, the box of conditions that stood open was a moment behind its heading now and then, and the click
  # meant to unfold them folded them (a case stopped on "the conditions", twice in ninety).
  $waited = [Diagnostics.Stopwatch]::StartNew()
  while (-not (ById 'Search_Keyword') -and $waited.ElapsedMilliseconds -lt 1200) { Start-Sleep -Milliseconds 50 }
  if (-not (ById 'Search_Keyword')) { ClickEl (ById 'Found_Conditions') "the Conditions heading (folded)" }
  WaitUntil { ById 'Search_Keyword' } "the conditions" 4000 | Out-Null
}
# Its keyword box, under the Keyword heading: by its id - "Name, base or stat" is its placeholder, not
# a label the box stands under.
function ItemSearchBox { ById 'Search_Keyword' }
# How many it found, as the page says beside its title: "3 found"; nothing while nothing is asked.
function ItemSearchCount { $t = ById 'Found_Title'; if ($t) { $t.Current.Name } }
# Its rows in sight, by name.
function ItemSearchRows([string]$like = '*') { @((All (Cond $AE::AutomationIdProperty 'Found_Row')) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -like $like }) }
# A word asked on the page, and its finds waited for.
function ItemSearchFor([string]$words) {
  ItemSearch
  (WaitUntil { ItemSearchBox } "the search's name box" 4000).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($words)
  WaitUntil { if ((ItemSearchCount) -match '^\d+ found$') { 'found' } } "the finds of '$words' counted" 8000 | Out-Null
  # The results unfolded, where a case before left them folded: the list is what the cases read.
  if (-not (ById 'Search_Found')) { ClickEl (WaitUntil { ById 'Found_Results' } "the Results heading" 4000) "the Results heading (folded)" }
  WaitUntil { if ((ItemSearchRows).Count -gt 0) { 'listed' } } "the finds of '$words' listed" 8000 | Out-Null
}
# Its conditions emptied by the brush: nothing asked, nothing kept for the case after.
function ItemSearchEnd {
  ClickEl (WaitUntil { ById 'Bookmark_Search' } "the Search bookmark" 4000) "Search, on the right page"
  $brush = WaitUntil { ById 'Search_Clear' } "the conditions' brush" 4000
  if ($brush.Current.IsEnabled) {
    ClickEl $brush "the conditions' brush"
    WaitUntil { if (-not (ById 'Search_Clear').Current.IsEnabled) { 'emptied' } } "the conditions emptied" 4000 | Out-Null
  }
}

# Nothing standing over the pages from the case before: the search box emptied, a picker or a selection let go.
function Calm {
  $box = ById 'Quick_Box'
  if ($box) {
    $value = $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($value.Current.Value) { $value.SetValue('') }
  }
  # Twice: a picker on top, then a selection under it.
  Front
  foreach ($press in 1, 2) { [Fast]::keybd_event(0x1B, 0, 0, 0); [Fast]::keybd_event(0x1B, 0, 2, 0); Start-Sleep -Milliseconds 70 }
}

# The program brought to where a case starts.
function Reach([string]$start) {
  if ($start -eq 'Home') {
    # Home is the program as it opens: with a character open - or one opened and closed again for the pad to be
    # put back (Reset-Pad) - there is no way back to that but a new start.
    if ((IsLoaded) -or $script:session.Reset) { $pad = $script:session.Pad; $mode = $script:session.Mode; End-Session; Start-Session $pad $mode }
    return
  }

  if (-not (IsLoaded)) {
    # From whatever page the case before left up: the characters' list is the home page's.
    if (-not (ByName 'Open')) { ClickEl (WaitUntil { ByTip 'Home' -Prefix } "the Home button" 4000) "Home" }
    ClickEl (WaitUntil { ByName 'Open' } "Load") "Load"
    WaitUntil { if (IsLoaded) { 'loaded' } } "character loaded" 15000 | Out-Null
  }
  Calm
  # Only from another page: the Items button pressed while its page is up leaves the page.
  if (-not (ById 'Bookmark_Edit')) {
    ClickEl (ById 'SideBar_Inventory') "Items"
    WaitUntil { ById 'Bookmark_Edit' } "the Items page" | Out-Null
  }
  # Waited for, each: the page redraws as the case before is cleared away, and a bookmark is not there for a moment.
  ClickEl (WaitUntil { ById 'Bookmark_Inventory' } "the Inventory bookmark" 4000) "the inventory, on the left page"
  ClickEl (WaitUntil { ById 'Bookmark_Edit' } "the Edit bookmark" 4000) "Edit, on the right page"
  WaitUntil { ItemsPage } "the inventory's panes" | Out-Null
}
