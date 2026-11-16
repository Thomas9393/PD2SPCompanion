# The Edit page's Basic section, as only a window shows it (BasicEditor): that a click on a
# stepper, a click with Ctrl held and a click with Shift held each reach the section - on the worn rare amulet,
# the item level raised one step and then one more with Ctrl, the picture sent to its last with Shift, and the
# file read back.
#
# What each of the three then does - a step, Ctrl's step above the least the affixes need, Shift's jump to the
# field's end, each kept as it is made - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Items/BasicSectionTests.
#
# On the pad's PhySpearZon, whose worn amulet (Beast Necklace) has item level 81 and its second picture.

function Amulet { (& $script:Cli dump "$pad\saves\PhySpearZon.d2s" 2>&1 | Out-String) -split '(?=\n  #)' | Where-Object { $_ -match 'Beast Necklace' } | Select-Object -First 1 }

# The section's "max" steppers, in order: the picture's, then the item level's (the amulet has no defence,
# durability or quantity).
function Ups { WaitUntil { $all = @((All (Cond $AE::ControlTypeProperty $CT::Button)) |
    Where-Object { $_.Current.HelpText -like '*Shift + LMB: max value' }); if ($all.Count -ge 2) { , $all } } "the Basic steppers" }

Case 'basic.held-keys-reach-the-basic-steppers' -Tags items, edit, basic -Changes {
  $start = Amulet
  Check ($start -match 'ilvl 81' -and $start -match 'invamu2 ') "start: item level 81, the second picture"

  ClickUntil { ClickEl (WaitUntil { Tile 'Beast Necklace' } "the amulet") "the amulet" } { OnEdit 'Beast Necklace' } "the amulet on the Edit page" | Out-Null

  ClickEl (Ups)[1] "item level +"
  # Ctrl held: ten at a time - but this amulet's affixes need less than its 81, and above
  # what they need ctrl steps one (item-level-floor checks the floor itself).
  Front
  [Fast]::keybd_event(0x11, 0, 0, 0)
  try { ClickEl (Ups)[1] "item level + (ctrl)" } finally { [Fast]::keybd_event(0x11, 0, 2, 0) }
  # Shift held through the click: the modifier press sends the field to its end.
  Front
  [Fast]::keybd_event(0x10, 0, 0, 0)
  try { ClickEl (Ups)[0] "picture + (shift)" } finally { [Fast]::keybd_event(0x10, 0, 2, 0) }

  Kept "the save written"

  $amulet = Amulet
  Check ($amulet -match 'ilvl 83') "written: item level 83 (a step, then one with ctrl above the affixes' floor)"
  Check ($amulet -match 'invamu3 ') "written: the last picture (shift-click jumped)"
}
