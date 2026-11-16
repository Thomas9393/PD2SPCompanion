# The mercenary's editing, as only a window shows it (MercenaryEdit): the three lines drawn
# over its slots, the pencil after the name and the popup it opens, shut by its header's cross, a level typed in the
# level's box, a pick in a box of the popup reaching the file, the DEAD badge with Revive after it, and Legit's
# page with neither pencil nor Revive.
#
# What the page decides - the lines' words, the level held to the character's, the variant written as the one
# hired in Normal, the name, the undo, Revive, what Legit leaves, and a box's write-back that is no edit - is
# proven without a window: tests/PD2SPCompanion.Shell.Tests/Mercenary/MercenaryEditTests.
#
# On the pad's PhySpearZon, level 86, whose Ascendant (Dark, hired in Hell) is level 86 too.

Add-Type -AssemblyName System.Windows.Forms

# What the file says of the mercenary: its name and the name's place in the list, its variant line, level, row.
function Hired {
  $text = & $script:Cli reqs "$pad\saves\PhySpearZon.d2s" 2>&1 | Out-String
  $h = @{ Name = ''; NameId = -1; Line = ''; Level = 0; Type = -1 }
  if ($text -match 'mercenary name: (.+?) \(name id (\d+) of (\d+)') { $h.Name = $Matches[1]; $h.NameId = [int]$Matches[2] }
  if ($text -match '(?m)^mercenary: \w[\w ]*\((.+), level (\d+)\), type (\d+)') { $h.Line = $Matches[1]; $h.Level = [int]$Matches[2]; $h.Type = [int]$Matches[3] }
  $h
}
function Val($el) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
# What a box shows: its value - it has no name of its own, and its selection answers empty.
function Shown($id) { Val (ById $id) }
function Any($id) { @(All (Cond $AE::AutomationIdProperty $id))[0] }

# The mercenary's row wheeled clear of the page's foot, the Equipped section folded out of its way.
function OpenRow([switch]$NoWheel) {
  $header = WaitUntil { ByName 'Equipped' -Prefix } "the Equipped header"
  ClickEl $header "fold Equipped"
  $b = $header.Current.BoundingRectangle
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + 90)) | Out-Null
  # The page ends well above the window's foot: its frame and the message strip lie under it.
  $foot = $script:r.Y + $script:r.Height - 160
  if ($NoWheel) { return }
  $last = $null
  for ($n = 0; $n -lt 12; $n++) {
    $up = Any 'Merc_VariantLine'
    $low = if ($up) { $up.Current.BoundingRectangle.Bottom + 220 } else { -1 }
    # Clear of the foot, or the page at its end: a wheel that moved nothing.
    if ($up -and -not $up.Current.IsOffscreen -and $low -gt 0 -and ($low -lt $foot -or $low -eq $last)) { break }
    $last = $low
    Front; [Fast]::mouse_event(0x0800, 0, 0, -120, 0); Start-Sleep -Milliseconds 250
  }
  Step "the mercenary on screen (wheeled $n)"
}
# The page as OpenRow found it, for the cases after one that changed nothing: wheeled back to its top, the
# Equipped section unfolded. A case that changes the pad ends with the popup up and says -Restart instead: the
# pad is put back under the running program, and the fold - kept nowhere in a test run (PD2SPC_FOLDS=off) - stayed
# with it, so the case after met no worn item (two cases stopped on "the worn ring", "the worn
# gloves", each only where no start of the program came between).
function ShutRow {
  Front
  [Fast]::SetCursorPos([int]($script:r.X + $script:r.Width * 0.25), [int]($script:r.Y + $script:r.Height * 0.5)) | Out-Null
  1..12 | ForEach-Object { [Fast]::mouse_event(0x0800, 0, 0, 120, 0); Start-Sleep -Milliseconds 100 }
  ClickEl (WaitUntil { ByName 'Equipped' -Prefix } "the Equipped header") "unfold Equipped"
}
# The popup, opened by the pencil after the mercenary's name.
function OpenPopup {
  ClickEl (WaitUntil { ById 'Merc_Edit' } "the pencil") "the pencil"
  WaitUntil { ById 'Merc_LevelUp' } "the popup" | Out-Null
}

# One of a box's entries: the box opened, and the entry clicked - by name, or the lowest one that was not on
# the page before the box opened and is not the one it shows - told, the box having no name of its own for
# UI Automation. The lowest, because the list opens wheeled to
# the entry shown - the pad's is its last - and the ones far above it are in the list but not in sight.
function Choose([string]$id, [string]$entry = '', [string]$shown = '') {
  $box = WaitUntil { ById $id } "the box $id"
  $there = @((All (Cond $AE::ControlTypeProperty $CT::ListItem)) | ForEach-Object { $_.Current.Name })
  ClickEl $box $id
  $pick = WaitUntil {
    $rows = @((All (Cond $AE::ControlTypeProperty $CT::ListItem)) | Where-Object { -not $_.Current.IsOffscreen })
    if ($entry) { @($rows | Where-Object { $_.Current.Name -eq $entry } | Sort-Object { $_.Current.BoundingRectangle.Y })[-1] }
    else { @($rows | Where-Object { $_.Current.Name -and $_.Current.Name -notin $there -and $_.Current.Name -ne $shown } | Sort-Object { $_.Current.BoundingRectangle.Y })[-1] }
  } "an entry of $id"
  $name = $pick.Current.Name
  ClickEl $pick "entry '$name'"
  $name
}

Case 'merc-edit.three-lines-over-the-slots' -Tags mercenary, items {
  $start = Hired
  try {
    OpenRow
    $lines = @('Merc_NameLine', 'Merc_LevelLine', 'Merc_VariantLine' | ForEach-Object { (WaitUntil { ById $_ } $_).Current.Name }) -join ' | '
    Check ($lines -eq "$($start.Name) | Level 86 Ascendant | Dark (Amplify Damage)") "the lines: $lines"
    Check (-not (Any 'Merc_LevelUp')) "no controls on the page until the pencil is pressed"
    Check (-not (Any 'Merc_Revive') -and -not (Any 'Merc_Dead')) "alive: no badge, no Revive"
  }
  finally { ShutRow }
}

Case 'merc-edit.pencil-opens-the-popup-focused-on-its-cross' -Tags mercenary, items, keys {
  try {
    OpenRow
    OpenPopup
    # Its focus is on its cross as it opens, not on the level's box: a key pressed next changes nothing
    # (the cross in the header).
    $focused = WaitUntil { $f = [System.Windows.Automation.AutomationElement]::FocusedElement; if ($f.Current.AutomationId -like 'Merc_*') { $f } } "the focus in the popup" 3000
    Check ($focused.Current.AutomationId -eq 'Merc_Close') "the popup opens with the focus on its cross ($($focused.Current.AutomationId))"
    Check ((Val (ById 'Merc_Level')) -eq '86') "the box says its level, 86"
    Check ((ById 'Merc_Variant').Current.IsEnabled -and (ById 'Merc_Name').Current.IsEnabled) "name and variant are offered"
    ClickEl (ById 'Merc_Close') "the cross"
    WaitUntil { -not (ById 'Merc_LevelUp') } "the popup shut" 3000 | Out-Null
    Check ([bool](ById 'Merc_Edit')) "the cross shuts the popup; the pencil is there for the next time"
  }
  finally { ShutRow }
}

Case 'merc-edit.casual-holds-the-level-to-the-characters' -Tags mercenary, items -Mode Casual -Changes -Restart -Typing {
  OpenRow
  OpenPopup
  $up = WaitUntil { ById 'Merc_LevelUp' } "Raise level"
  Check (-not $up.Current.IsEnabled) "Casual: Raise is dimmed at the character's own level"

  ClickEl (ById 'Merc_LevelDown') "Lower level"
  WaitUntil { (Val (ById 'Merc_Level')) -eq '85' } "the box at 85" | Out-Null
  Kept "level 85"
  Check ((Hired).Level -eq 85) "Lower: the file says level $((Hired).Level)"
  Check ((ById 'Merc_LevelLine').Current.Name -eq 'Level 85 Ascendant') "and the line under the name: $((ById 'Merc_LevelLine').Current.Name)"
  Check ((ById 'Merc_LevelUp').Current.IsEnabled) "and Raise is offered again"

  # Typed past the character's level: held to it.
  ClickEl (ById 'Merc_Level') "the level box"
  [System.Windows.Forms.SendKeys]::SendWait("^a95{ENTER}")
  Kept "level typed"
  Check ((Hired).Level -eq 86) "Casual: 95 typed is held to the character's 86 (the file says $((Hired).Level))"
}

Case 'merc-edit.level-typed-in-free-is-written' -Tags mercenary, items -Changes -Restart -Typing {
  OpenRow
  OpenPopup
  ClickEl (WaitUntil { ById 'Merc_Level' } "the level box") "the level box"
  [System.Windows.Forms.SendKeys]::SendWait("^a98{ENTER}")
  Kept "level 98"
  Check ((Hired).Level -eq 98) "Free: 98 typed, the file says level $((Hired).Level)"
  Check (-not (ById 'Merc_LevelUp').Current.IsEnabled) "Raise is dimmed at 98, the most there is"
}

# A click on an element's middle with keys held: 'Shift', 'Ctrl', or both joined by '+'.
function ClickWith($el, [string]$with) {
  Front
  $b = $el.Current.BoundingRectangle
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 120
  $keys = @($with -split '\+' | ForEach-Object { @{ Shift = 0x10; Ctrl = 0x11 }[$_] })
  foreach ($key in $keys) { [Fast]::keybd_event($key, 0, 0, 0); Start-Sleep -Milliseconds 40 }
  try { [Fast]::mouse_event(2, 0, 0, 0, 0); Start-Sleep -Milliseconds 40; [Fast]::mouse_event(4, 0, 0, 0, 0) }
  finally { foreach ($key in $keys) { Start-Sleep -Milliseconds 40; [Fast]::keybd_event($key, 0, 2, 0) } }
  Start-Sleep -Milliseconds 300
}

# The level's steppers take the character's level's keys: Ctrl 5, Shift 20, both to the end.
Case 'merc-edit.level-steppers-take-ctrl-and-shift' -Tags mercenary, items, keys -Changes -Restart {
  OpenRow
  OpenPopup
  $down = WaitUntil { ById 'Merc_LevelDown' } "Lower level"
  Check ($down.Current.HelpText -match 'Ctrl \+ LMB: 5 levels' -and $down.Current.HelpText -match 'Ctrl \+ Shift \+ LMB: level 1') "the tooltip says the keys: '$(($down.Current.HelpText -split "`n") -join ' / ')'"
  ClickWith $down 'Ctrl'
  Check ([bool](WaitUntil { if ((Val (ById 'Merc_Level')) -eq '81') { 'ok' } } "86 less 5" 3000)) "Ctrl: five levels down, 86 to $(Val (ById 'Merc_Level'))"
  ClickWith $down 'Shift'
  Check ([bool](WaitUntil { if ((Val (ById 'Merc_Level')) -eq '61') { 'ok' } } "81 less 20" 3000)) "Shift: twenty down, to $(Val (ById 'Merc_Level'))"
  ClickWith (ById 'Merc_LevelUp') 'Ctrl+Shift'
  Check ([bool](WaitUntil { if ((Val (ById 'Merc_Level')) -eq '98') { 'ok' } } "the most" 3000)) "Ctrl + Shift: to the end, $(Val (ById 'Merc_Level'))"
}

Case 'merc-edit.variant-picked-in-its-box-is-written' -Tags mercenary, items -Changes -Restart {
  $start = Hired
  OpenRow
  OpenPopup
  Choose 'Merc_Variant' 'Light (Sanctuary)' | Out-Null
  Kept "the variant"
  $now = Hired
  Check ($now.Type -eq 31 -and $now.Line -eq 'Light (Sanctuary), Normal') "another variant, offered by its name alone and written as the one hired in Normal: $($now.Line) (row $($now.Type))"
  Check ((ById 'Merc_VariantLine').Current.Name -eq 'Light (Sanctuary)') "the line says $((ById 'Merc_VariantLine').Current.Name)"
  Check ($now.Level -eq $start.Level -and $now.NameId -eq $start.NameId) "its level and its name are kept ($($now.Level), $($now.Name))"
}

Case 'merc-edit.name-picked-in-its-box-is-written-and-undone' -Tags mercenary, items -Changes -Restart {
  $start = Hired
  OpenRow
  OpenPopup
  $named = Choose 'Merc_Name' -shown $start.Name
  Kept "the name"
  $now = Hired
  Check ($now.Name -eq $named -and $now.NameId -ne $start.NameId) "another name of its type's list: $($now.Name) (the file's place $($now.NameId))"
  Check ((Shown 'Merc_Name') -eq $named) "and the box shows it"

  # The name is written already, so the step is there: Undo itself, not UndoLast, which waits for a write.
  ClickEl (WaitUntil { $u = ByTip 'Undo ' -Prefix; if ($u -and $u.Current.IsEnabled) { $u } } "Undo") "Undo"
  Kept "the undo"
  $back = Hired
  Check ($back.NameId -eq $start.NameId -and $back.Type -eq $start.Type -and $back.Level -eq $start.Level) "Undo: $($back.Name) again, the variant and level as they were"
  Check ([bool](WaitUntil { (Shown 'Merc_Name') -eq $back.Name } "the box back at $($back.Name)" 4000)) "and the box follows the undo"
}

Case 'merc-edit.dead-wears-the-badge-with-revive-after-it' -Tags mercenary, items -Before {
  & $script:Cli merc "$pad\saves\PhySpearZon.d2s" --dead | Out-Null
} {
  Check ((Hired).Line -match 'dead') "made dead with the CLI: $((Hired).Line)"
  OpenRow
  Check ((ById 'Merc_LevelLine').Current.Name -eq 'Level 86 Ascendant' -and (ById 'Merc_Dead').Current.Name -eq 'DEAD') "dead: the line as it was, and the DEAD badge after it"
  $revive = WaitUntil { ById 'Merc_Revive' } "Revive"
  $badge = (ById 'Merc_Dead').Current.BoundingRectangle; $at = $revive.Current.BoundingRectangle
  Check ($revive.Current.IsEnabled -and $at.X -ge $badge.Right - 1 -and [Math]::Abs(($at.Y + $at.Height / 2) - ($badge.Y + $badge.Height / 2)) -le 4) "Revive is there, after the DEAD badge on the level's line - where a dead character's is"
  ClickEl $revive "Revive"
  Kept "the revive"
  $now = Hired
  Check ($now.Line -notmatch 'dead' -and $now.Level -eq 86 -and $now.Type -eq 34) "Revive: alive in the file, nothing else moved ($($now.Line), level $($now.Level))"
  Check (-not (Any 'Merc_Revive') -and -not (Any 'Merc_Dead')) "and Revive and the badge are gone"
}

Case 'merc-edit.legit-hides-the-pencil-and-revive' -Tags mercenary, items, play-mode -Mode Legit -Before {
  & $script:Cli merc "$pad\saves\PhySpearZon.d2s" --dead | Out-Null
} {
  OpenRow -NoWheel
  WaitUntil { ByName 'Mercenary' -Prefix } "the Mercenary section" | Out-Null
  Check ([bool](Any 'Merc_NameLine') -and -not (Any 'Merc_Edit') -and -not (Any 'Merc_LevelUp')) "Legit: the lines, and no pencil - hidden, not dimmed"
  Check ([bool](Any 'Merc_Dead') -and -not (Any 'Merc_Revive')) "dead in Legit: the badge, and no Revive"
}
