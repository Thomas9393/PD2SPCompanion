# A property's number typed on the keyboard, as only a window shows it (Replenish Life
# "always falls back to the minimum"): what the box itself does with the keys - a first digit under the range left
# as typed so the second can follow, a number left unfinished held to the range when the box is left or Enter is
# pressed, a number past the range held at once.
#
# What the row behind the box does with an unfinished number - it stays as typed, and the item keeps the last
# number that held - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Edit/PropertyRowsTests.
#
# On the pad's PhySpearZon: the Felicitous Ring of Regrowth in the personal stash, whose Replenish Life is 15 and
# rolls 12-18. In Casual: a number is held to its affix's range in every mode but Free.

Add-Type -AssemblyName System.Windows.Forms

# The edit boxes on screen, and what one holds.
function Boxes { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen }) }
function Held($box) { $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 250 }
# The ring's Replenish Life box: the one holding 15 (the graphic, the item level 85 and magic find 7 are the others).
function LifeBox {
  Pane 0 'Personal Stash'
  ClickUntil { ClickEl (WaitUntil { Tile 'Felicitous Ring of Regrowth' } "the ring") "the ring" } { OnEdit 'Felicitous Ring of Regrowth' } "the ring on the Edit page" | Out-Null
  WaitUntil { Boxes | Where-Object { (Held $_) -eq '15' } | Select-Object -First 1 } "the Replenish Life box (15)"
}

# A number that holds is the item's as it is typed: the pad is put back after each.
Case 'type-number.digits-one-by-one-then-box-left' -Tags edit, properties, keys -Mode Casual -Typing -Changes {
  $box = LifeBox
  ClickEl $box "the box"

  Keys '^a{DEL}'
  Keys '1'
  Check ((Held $box) -eq '1') "a first digit under the range stays as typed: '$(Held $box)'"
  Keys '7'
  Check ((Held $box) -eq '17') "17 typed digit by digit: '$(Held $box)'"

  Keys '{BACKSPACE}'
  Check ((Held $box) -eq '1') "a digit taken off leaves the rest: '$(Held $box)'"
  Keys '{TAB}'
  Check ((Held $box) -eq '12') "left unfinished, the box holds the minimum: '$(Held $box)'"
}

Case 'type-number.past-the-range-and-enter' -Tags edit, properties, keys -Mode Casual -Typing -Changes {
  $box = LifeBox
  ClickEl $box "the box"

  Keys '^a{DEL}'
  Keys '99'
  Check ((Held $box) -eq '18') "a number past the range is held at once: '$(Held $box)'"

  Keys '^a{DEL}'
  Keys '1'
  Keys '{ENTER}'
  Check ((Held $box) -eq '12') "Enter finishes the number: '$(Held $box)'"
}
