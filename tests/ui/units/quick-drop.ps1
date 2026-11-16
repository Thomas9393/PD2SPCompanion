# Quick in-grid drops are not put back (grids kept across rebuilds): a ring in the personal
# stash dragged a cell over and released at once, ten times, back and forth. After each, the landing cell is
# clicked: the ring is there, or the drop was put back. The old race put a release that came before the grid
# redraw back where it started - a race of the window's, which only a real drag shows.
#
# The page's half of it - every such drop lands, on a grid that stays the same object - is proven without a
# window: tests/PD2SPCompanion.Shell.Tests/Inventory/QuickDropTests.

Case 'quick-drop.ten-quick-drops-all-land' -Tags quick-drop, items, drag -Changes {
  $Drops = 10; $Hold = 0
  $landed = 0

  Pane 0 'Personal Stash'
  # The ring's cell and the one left of it, read off its tile.
  $tile = WaitUntil { Tile 'Steel Ring of Skill' } "the ring's tile"
  $cells = @((CellOf $tile), (CellOf $tile -1 0))
  ClickUntil { ClickAt $cells[0][0] $cells[0][1] } { OnEdit 'Steel Ring of Skill' } "the ring on the Edit page" 800 | Out-Null

  $at = 0   # which of the two cells the ring is in
  for ($n = 0; $n -lt $Drops; $n++) {
    $from = $cells[$at]; $to = $cells[1 - $at]
    DragTo $from $to -Hold $Hold
    # Settled: the page answers again before the landing is looked at.
    WaitUntil { ItemsPage } "the page after drop $n" | Out-Null
    # Past the double-click time: a click on the landing cell right after the release was read as
    # a double-click on the ring, which is an action of its own, not a look.
    Start-Sleep -Milliseconds 700
    ClickAt $to[0] $to[1]
    $there = WaitUntil { if (OnEdit 'Steel Ring of Skill') { 'yes' } elseif (ByName 'Please select an item.') { 'no' } } "the landing cell answered ($n)"
    if ($there -eq 'yes') { $landed++; $at = 1 - $at } else { Step "drop $n put back" }
  }
  Check ($landed -eq $Drops) "every quick drop landed ($landed of $Drops, released $Hold ms after the move)"
}
