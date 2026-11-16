# Shift tapped turns a socketed item's reading over - its sockets apart under "Socketed (n)", or summed into its
# own lines as the game reads it - and it stays (a toggle, kept within the
# session; holding Shift felt like extra effort). A tap: Shift down and up with nothing between.
#
# What either reading says is proven without a window: tests/PD2SPCompanion.Tests/Items/ItemLinesTests.
#
# On the pad's PhySpearZon: the worn Stone Barb, two Shael runes, a Perfect Skull and a Ruby Jewel of Envy.

function TapShift { [Fast]::keybd_event(0x10, 0, 0, 0); Start-Sleep -Milliseconds 60; [Fast]::keybd_event(0x10, 0, 2, 0); Start-Sleep -Milliseconds 200 }

Case 'socket-view.shift-tap-turns-it-over' -Tags sockets, keys {
  $tile = WaitUntil { Tile 'Stone Barb' } "the worn Stone Barb"
  Hover $tile
  Check (-not ((TipTexts) -contains 'Shael Rune')) "summed at first: no socket named in the tooltip"

  TapShift
  Hover $tile
  Check ((TipTexts) -contains 'Shael Rune') "a tap of Shift: each socket named, under its line"

  # And it stays: the pointer away and back, nothing held.
  Hover (WaitUntil { ByTip 'Items' -Prefix } "somewhere else")
  Hover $tile
  Check ((TipTexts) -contains 'Shael Rune') "it stays, Shift not held"

  TapShift
  Hover $tile
  Check (-not ((TipTexts) -contains 'Shael Rune')) "another tap: summed again"
}
