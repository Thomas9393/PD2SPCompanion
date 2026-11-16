# Ctrl on the item level's steppers, as only a window shows it: that Ctrl held on a click of
# - or + reaches the Basic section's box - the page watches for the key from above the buttons, which mark their
# own press handled - and that the - says in its tooltip where Ctrl goes.
#
# Where Ctrl goes from either side of the least the affixes need, and what the die rolls, is proven without a
# window: tests/PD2SPCompanion.Shell.Tests/Edit/BasicsTests.
#
# On the pad's PhySpearZon: the magic Steel Ring of Skill in the personal stash.

# The item level's line: its box, and its - and + by where they stand.
function LevelLine {
  $label = (ByName 'Item level').Current.BoundingRectangle
  $mid = { param($e) $b = $e.Current.BoundingRectangle; $b.Y + $b.Height / 2 }
  # The first box centred below the label's top, right of it: the line grows under the label when the red
  # note shows, and the nearest box was then the graphic's, above.
  $box = @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt $label.X + 100 -and (& $mid $_) -gt $label.Y } | Sort-Object { & $mid $_ })[0]
  $y = & $mid $box
  $buttons = @((All (Cond $AE::ControlTypeProperty $CT::Button)) | Where-Object { -not $_.Current.IsOffscreen -and [math]::Abs((& $mid $_) - $y) -lt 6 -and $_.Current.BoundingRectangle.X -gt $label.X + 100 } | Sort-Object { $_.Current.BoundingRectangle.X })
  [pscustomobject]@{ Box = $box; Minus = $buttons[0]; Plus = $buttons[1]; Die = $buttons[2] }
}
function Level { [int](LevelLine).Box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function SetLevel([int]$to) { (LevelLine).Box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("$to"); Start-Sleep -Milliseconds 300 }
function CtrlClick($button, [string]$what) {
  Front; [Fast]::keybd_event(0x11, 0, 0, 0)
  try { ClickEl $button $what } finally { [Fast]::keybd_event(0x11, 0, 2, 0) }
  Start-Sleep -Milliseconds 300
}

# Every level set is the ring's at once: the pad is put back after.
Case 'item-level-floor.ctrl-click-reaches-the-item-level' -Tags edit, keys -Changes {
  Pane 0 'Personal Stash'
  Start-Sleep -Milliseconds 500
  $name = 'Steel Ring of Skill'
  ClickUntil { ClickEl (Tile $name) $name } { OnEdit $name } "$name on the Edit page" 800 | Out-Null

  # The floor, as the line under the field says it while the level is past it: "affixes 23+ - max 99" (a dot between).
  $said = (WaitUntil { ByName 'affixes ' -Prefix } "the affixes' floor under the field").Current.Name
  $floor = [int]($said -replace '^affixes (\d+)\+.*$', '$1')
  Check ($floor -gt 1 -and $floor -lt (Level)) "the floor is said under the field ('$said')"

  SetLevel ($floor - 5)
  CtrlClick (LevelLine).Plus "ctrl + below the floor"
  Check ((Level) -eq $floor) "below the floor, ctrl + goes to it ($(Level))"

  SetLevel 90
  CtrlClick (LevelLine).Minus "ctrl - above the floor"
  Check ((Level) -eq $floor) "above the floor, ctrl - goes to it ($(Level))"

  SetLevel 90
  Check ($null -ne (ByTip "Ctrl + LMB: to $floor" -Prefix)) "the - says where ctrl goes"
}
