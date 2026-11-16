# Selecting items edits nothing (the item panel's rows are recycled from one item to the next): a
# weapon, a charm, a body armor and an amulet - different properties, different ranges - chosen in turn,
# five rounds, quickly. A number box handed a new value under an old range used to clamp it and write the
# clamp back (a spear's durability of 56 landing on a body armor's 32): the boxes' own doing, which only real
# boxes show. After the rounds the character's file has not been written and Undo has nothing to undo.
#
# The pages' half of it - a selection is no change, and leaves nothing to save - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Inventory/SelectNoWritesTests.

Case 'select-no-writes.twenty-selects-write-nothing' -Tags select-no-writes, items, edit {
  $names = @('Stone Barb', 'Annihilus', "Duriel's Shell", 'Beast Necklace')
  $was = WrittenAt "$pad\saves\PhySpearZon.d2s"

  # The Annihilus lies in the inventory: the first pane on it, whatever the case before left there.
  Pane 0 'Inventory'
  ClickEl (WaitUntil { ById 'Bookmark_Edit' } "Edit") "Edit"
  $points = @(); foreach ($n in $names) { $script:n = $n; $points += , (CellOf (WaitUntil { if (Tiles $script:n) { Tile $script:n } } "the $n tile")) }
  for ($round = 0; $round -lt 5; $round++) {
    foreach ($p in $points) { ClickAt $p[0] $p[1]; Start-Sleep -Milliseconds 250 }
  }
  # The last one chosen, settled: its heading on the panel.
  WaitUntil { OnEdit 'Beast Necklace' } "the last item on the panel" | Out-Null
  Start-Sleep -Seconds 2
  # A step to undo names itself on the button: "Undo ..." (fast.ps1, UndoLast).
  $undo = ByTip 'Undo ' -Prefix
  Check (-not $undo -or -not $undo.Current.IsEnabled) "nothing to undo after 20 selects"
  Check ((WrittenAt "$pad\saves\PhySpearZon.d2s") -eq $was) "the character's file not written"
}
