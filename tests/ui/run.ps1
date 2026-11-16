# Runs the small tests of tests/ui/units (kit.ps1 says what a case is).
#
#   run.ps1 [-Only search.*,vault.tree-*] [-Tag search] [-List] [-Pad <a copy of the pad> -CardPad <a copy of the card pad>]
#
# -Only  cases by name; * stands for anything.          -Tag   cases wearing any of these tags.
# -List  the cases there are, with their tags, and nothing run.
# -Left  of those picked, only the cases an earlier round has not confirmed: whose last result in the record is
#        not a pass, or that were never run. What a round passed is jumped over - so a round that stopped part way, or left failures, is taken
#        up where it was and never from its start. With -List, it shows what is left.
# The pads are copies, never the corpus's own: with none handed in, the run makes fresh ones of
# TestCorpus\ui-pad\pad and \cardpad (tests\paths.ps1 says where the corpus is) under the temp folder, and says where.
#
# The cases picked are put in an order that shares the program: one start for every case of the same pad and play
# mode, those that change nothing first. After a case that changes what is saved the pad is put back - under the
# running program where that can be (kit.ps1's Reset-Pad), else by closing it; the next case then loads the
# character afresh, or starts the program again. After a case that failed the program is always closed.
#
# The program's own settings are a copy too: the run works in <Pad>\data, begun from the developer's settings
# (the game folder is in them), so no case reads how the developer left the program or writes to it - ninety
# of the old scripts saved and restored that file by hand.
#
# A case that fails in a program it shared with the cases before it is run once more in a program of its own, on
# the pad as handed in: what an earlier case left on screen is then not what failed it. It is reported apart
# ("pass, alone") - a case that needs that is leaning on, or tripping over, another, and wants looking at.
#
# At the end: a line per case with its seconds, how many times the program was started, and the same appended to
# the corpus's runs\ui-cases.csv - the record the next review of the kit reads. What each case said is in
# <Pad>\ui-logs\<case>.txt.
param(
  [string]$Pad,
  [string]$CardPad,
  [string[]]$Only,
  [string[]]$Tag,
  [switch]$List,
  [switch]$Left)

# Given through -File, "a,b" arrives as one word: taken apart here.
if ($Only) { $Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ }) }
if ($Tag) { $Tag = @($Tag | ForEach-Object { $_ -split ',' } | Where-Object { $_ }) }

# $script:Corpus and $script:GameRoot. A run asks for them here; -List needs neither.
. "$PSScriptRoot\..\paths.ps1"
$corpus = $script:Corpus
if (-not $List) {
  if (-not $corpus -or -not (Test-Path -LiteralPath $corpus)) { throw "no corpus at '$corpus' (PD2SPC_CORPUS, or pd2spc.local.env): the cases run on copies of its pads" }
  if (-not $script:GameRoot -or -not (Test-Path -LiteralPath $script:GameRoot)) { throw "no game at '$($script:GameRoot)' (PD2SPC_GAME, or pd2spc.local.env)" }
}

# Fresh copies of the corpus's pads, where none were handed in.
if (-not $List -and (-not $Pad -or -not $CardPad)) {
  $work = Join-Path ([IO.Path]::GetTempPath()) 'pd2spc-ui-cases'
  foreach ($old in 'pad', 'pad.fresh', 'cardpad', 'cardpad.fresh') { if (Test-Path "$work\$old") { [IO.Directory]::Delete("$work\$old", $true) } }
  New-Item -ItemType Directory $work -Force | Out-Null
  Copy-Item "$corpus\ui-pad\pad" "$work\pad" -Recurse
  Copy-Item "$corpus\ui-pad\cardpad" "$work\cardpad" -Recurse
  $Pad = "$work\pad"; $CardPad = "$work\cardpad"
  Write-Host "pads: $work"
}

# The shared pad, under a name of its own: PowerShell does not tell $Pad from $pad, and $pad is what a case reads
# for the pad it runs on - set for each case below.
$shared = $Pad

# The settings the run works on, before fast.ps1 asks where they are.
$live = [Environment]::GetEnvironmentVariable('PD2SPC_DATA', 'User')
$data = "$shared\data"
if (-not $List) {
  # Never over the game: the run takes the mouse and the foreground from whoever is playing.
  if (Get-Process Game -ErrorAction SilentlyContinue) { throw "the game is running: no UI case is started over it" }
  if (-not $live -or -not (Test-Path "$live\settings.json")) { throw "the developer's settings were not found (PD2SPC_DATA): the run copies them for the game folder" }
  if (Test-Path $data) { [IO.Directory]::Delete($data, $true) }
  New-Item -ItemType Directory $data | Out-Null
  Copy-Item "$live\settings.json" $data
  # The locks as the program starts with them: none. The developer's own (the inventory lock, a locked page) stood
  # under every case and refused the picks they make - six cases failed on them in a full run. A case
  # about a lock sets the one it asks itself.
  $text = [IO.File]::ReadAllText("$data\settings.json")
  $text = $text -replace '"InventoryLock":\s*true', '"InventoryLock": false'
  $text = [regex]::Replace($text, '"LockedPages":\s*\[[^\]]*\]', '"LockedPages": []')
  # No version run before, which tells nothing: the developer's own last run is older than the build whenever the
  # version has just moved, and the notes of the new one opened at every start and took its first click - the OPEN
  # under them, a card's double click - so 299 cases of a full run never had a character. version-whatsnew sets
  # an older one itself.
  $text = [regex]::Replace($text, '"LastRunVersion":\s*"[^"]*"', '"LastRunVersion": ""')
  [IO.File]::WriteAllText("$data\settings.json", $text)
  if (Test-Path "$live\Seasons") { Copy-Item "$live\Seasons" "$data\Seasons" -Recurse }
  $env:PD2SPC_DATA = $data
}

$pad = $shared
. "$PSScriptRoot\fast.ps1"
. "$PSScriptRoot\kit.ps1"

# Every case there is: each unit file read once, for what it holds.
$files = @(Get-ChildItem "$PSScriptRoot\units\*.ps1" | Sort-Object Name)
# The kit's helpers are the kit's. The units are read on top of it, so a unit that names a helper of its own as
# the kit names one takes its place for every case after - of every other unit too (Hover and Wide
# were moved into the kit, and a unit's Hover that does not wait and another's Wide that measures a width stood
# in for them: tooltips read before they were up, a window never made wide again). Refused here, by name.
$kitNames = @(Get-ChildItem function: | Where-Object { $_.ScriptBlock.File -like "$PSScriptRoot\*.ps1" } | ForEach-Object { $_.Name })
foreach ($file in $files) {
  $own = @([regex]::Matches((Get-Content $file.FullName -Raw), '(?m)^\s*function\s+([\w-]+)') | ForEach-Object { $_.Groups[1].Value })
  $taken = @($own | Where-Object { $kitNames -contains $_ })
  if ($taken) { throw "$($file.Name) names a helper as the kit does ($($taken -join ', ')): it would stand in for the kit's in every case after - give it a name of its own" }
  $script:CaseFile = $file.FullName
  . $file.FullName
}
$all = $script:Cases.ToArray()

function Wanted($case) {
  if ($Only) {
    $named = $false
    foreach ($pattern in $Only) { if ($case.Name -like $pattern) { $named = $true } }
    if (-not $named) { return $false }
  }
  if ($Tag) {
    $tagged = $false
    foreach ($one in $Tag) { if ($case.Tags -contains $one) { $tagged = $true } }
    if (-not $tagged) { return $false }
  }
  $true
}
$picked = @($all | Where-Object { Wanted $_ })

# The record every round adds to, a line as each case ends: so a round that is stopped keeps what it had proven.
$record = "$corpus\runs\ui-cases.csv"
if ($Left) {
  $last = @{}
  if (Test-Path $record) { Import-Csv $record | ForEach-Object { $last[$_.case] = $_.result } }
  $confirmed = @($picked | Where-Object { $last[$_.Name] -eq 'pass' })
  $picked = @($picked | Where-Object { $last[$_.Name] -ne 'pass' })
  Write-Host "$($confirmed.Count) confirmed by an earlier round and jumped over; $($picked.Count) left"
}

if ($List) {
  $picked | Sort-Object Name | ForEach-Object { "{0,-52} {1,-6} {2,-5} {3}{4}" -f $_.Name, $_.Start, $_.Mode, ($_.Tags -join ','), $(if ($_.Changes) { '  (changes)' }) }
  "$($picked.Count) of $($all.Count) cases"
  return
}
if ($picked.Count -eq 0) {
  if ($Left) { "nothing is left: every case picked was confirmed by an earlier round"; return }
  throw "no case is named or tagged so; -List shows them"
}

# The order that shares the program: by pad and play mode; within those, Home starts first (a character once
# open, Home is a new start), then what changes nothing, then what does.
$order = @($picked | Sort-Object Pad, Mode, @{ Expression = { if ($_.Start -eq 'Home') { 0 } else { 1 } } }, @{ Expression = { [int]$_.Changes } }, File, Name)

# The pads as they were handed in, to put back after a case that changed one.
function Aside([string]$at) {
  $kept = "$at.fresh"
  if (Test-Path $kept) { [IO.Directory]::Delete($kept, $true) }
  New-Item -ItemType Directory $kept | Out-Null
  foreach ($part in 'saves', 'vtsmart') { Copy-Item "$at\$part" "$kept\$part" -Recurse }
}
function Afresh([string]$at) {
  foreach ($part in 'saves', 'vtsmart') {
    if (Test-Path "$at\$part") { [IO.Directory]::Delete("$at\$part", $true) }
    Copy-Item "$at.fresh\$part" "$at\$part" -Recurse
  }
}
Aside $shared
Aside $CardPad
$settingsAsHandedIn = [IO.File]::ReadAllBytes("$data\settings.json")

$logs = "$shared\ui-logs"
New-Item -ItemType Directory $logs -Force | Out-Null
$english = InputIsEnglish
$table = New-Object System.Collections.Generic.List[object]
$starts = 0
$resets = 0
$whole = [Diagnostics.Stopwatch]::StartNew()

try {
  foreach ($case in $order) {
    $at = if ($case.Pad -eq 'card') { $CardPad } else { $shared }
    Write-Host "== $($case.Name)"

    if ($case.Typing -and -not $english) {
      Write-Host "   skipped: it types, and the input language is $(InputLanguage)"
      $table.Add([pscustomobject]@{ Case = $case.Name; Result = 'skip'; Seconds = 0 })
      continue
    }

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $began = Get-Date
    $script:failed = @()
    $said = New-Object System.Collections.Generic.List[string]
    # What a case's -Before may set for the program's start - a PD2SPC_ switch, how the window is shown - as it
    # stands now: put back after the case whether or not its body ran (a start that failed never reaches the
    # body's own finally, and the switch stayed set for every case after it).
    $switches = @{}; Get-ChildItem Env:PD2SPC_* | ForEach-Object { $switches[$_.Name] = $_.Value }
    $shown = $script:show
    $loaded = $null
    $alone = $false
    for ($attempt = 1; ; $attempt++) {
    $sharing = [bool]$script:session
    try {
      # The case's file loaded afresh, for the helpers it defines for itself; the case is taken from it by its name.
      $pad = $at
      $script:Cases = New-Object System.Collections.Generic.List[object]
      $script:CaseFile = $case.File
      . $case.File
      $loaded = $script:Cases | Where-Object { $_.Name -eq $case.Name } | Select-Object -First 1

      # A program that fits the case, or a new one on the pad as it was handed in - always a new one for a case
      # that makes something before the program starts.
      if ($script:session -and ($loaded.Before -or $script:session.Dirty -or $script:session.Pad -ne $at -or $script:session.Mode -ne $case.Mode)) { End-Session }
      if (-not $script:session) {
        Afresh $at
        [IO.File]::WriteAllBytes("$data\settings.json", $settingsAsHandedIn)
        # A new log with each start: the program appends to it, and a line of a start before - the scale it
        # drew at - was read as this one's (fast.ps1's Unit).
        Remove-Item "$at\editor.log" -Force -ErrorAction SilentlyContinue
        if ($loaded.Before) { & $loaded.Before 2>&1 | ForEach-Object { $said.Add("before: $_") } }
        Start-Session $at $case.Mode
        $starts++
      }
      # Sharing: the program was there before this case and is the one it runs in.
      $sharing = $sharing -and [bool]$script:session
      $before = $script:session
      Reach $case.Start
      if (-not [object]::ReferenceEquals($before, $script:session)) { $starts++; $sharing = $false }
      # The settings as the case meets them: one that leaves them otherwise has changed the program, not the pad alone.
      $settingsMet = [Convert]::ToBase64String([IO.File]::ReadAllBytes("$data\settings.json"))

      & $loaded.Body 2>&1 | ForEach-Object { Write-Host "   $_"; $said.Add("$_") }
    }
    catch {
      $script:failed += "stopped: $($_.Exception.Message)"
      Write-Host "   STOPPED  $($_.Exception.Message)"
      $said.Add("STOPPED  $($_.Exception.Message)")
      # What the window showed as the wait ran out (WaitUntil's ui-fail.png), kept by the case's name: the pad's
      # one picture is the next stop's too, and a case that then passes in a program of its own left nothing to
      # look at (tip-actions.alt-turns-the-slot: once in seventeen, never seen again).
      $shot = "$at\ui-fail.png"
      if ((Test-Path $shot) -and (Get-Item $shot).LastWriteTime -gt $began) {
        Copy-Item $shot "$logs\$($case.Name)$(if ($sharing) { '.shared' }).png" -Force
      }
    }

    if ($loaded -and $loaded.Before) {
      Get-ChildItem Env:PD2SPC_* | Where-Object { -not $switches.ContainsKey($_.Name) } | ForEach-Object { Remove-Item "Env:$($_.Name)" }
      foreach ($name in $switches.Keys) { Set-Item "Env:$name" $switches[$name] }
      $script:show = $shown
    }

    if ($script:failed.Count -eq 0 -or -not $sharing -or $attempt -gt 1) { break }
    Write-Host "   -- failed in a shared program: once more, in one of its own"
    $said.Add("-- failed in a shared program: once more, in one of its own")
    End-Session
    $script:failed = @()
    $alone = $true
    }

    $passed = $script:failed.Count -eq 0
    if ($script:session -and -not $passed) { $script:session.Dirty = $true }
    elseif ($script:session -and $case.Changes) {
      # The pad put back under the running program, where the case changed the pad and nothing else.
      $settingsLeft = [Convert]::ToBase64String([IO.File]::ReadAllBytes("$data\settings.json"))
      $back = $false
      if (-not $loaded.Before -and -not $loaded.Restart -and $settingsLeft -eq $settingsMet) { try { $back = Reset-Pad { Afresh $at } } catch { $back = $false } }
      if (-not $back) { $script:session.Dirty = $true }
      else { $resets++ }
    }
    $said | Set-Content "$logs\$($case.Name).txt" -Encoding utf8
    $row = [pscustomobject]@{ Case = $case.Name; Result = $(if (-not $passed) { 'FAIL' } elseif ($alone) { 'pass alone' } else { 'pass' }); Seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1) }
    $table.Add($row)
    # Written now, not at the end: a round that is stopped has still said what it proved.
    if (Test-Path "$corpus\runs") {
      if (-not (Test-Path $record)) { 'when,case,result,seconds' | Set-Content $record -Encoding utf8 }
      "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'),$($row.Case),`"$($row.Result)`",$($row.Seconds)" | Add-Content $record -Encoding utf8
    }
  }
}
finally {
  End-Session
}

$table | Format-Table -AutoSize | Out-String
"$($table.Count) cases in $([int]$whole.Elapsed.TotalSeconds) s; the program was started $starts time(s), the pad put back under it $resets time(s)"

$leaning = @($table | Where-Object Result -eq 'pass alone')
if ($leaning) { "passed only in a program of their own ($($leaning.Count)): $($leaning.Case -join ', ')" }
$bad = @($table | Where-Object Result -eq 'FAIL')
if ($bad) { throw "$($bad.Count) of $($table.Count) failed: $($bad.Case -join ', ')" }
