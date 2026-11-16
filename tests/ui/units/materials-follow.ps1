# The materials tab under real drags (several at once too): an El Rune dragged
# out of the tab finds its cell in the inventory, the tab's tile saying one fewer and - after a move the tab has
# nothing to do with - still saying it and answering a click; and two runes picked in the personal stash and
# dragged onto the tab's El Rune find the tab, which counts both in.
#
# What the page decides at the press and the release - the count, the stack landed, the tab's tiles left as
# they were, the two put in together - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Inventory/MaterialsFollowTests.

# El Rune tiles on screen, each with the count drawn inside it (a stack in the inventory shows one too).
function Runes {
  @((All (Cond $AE::NameProperty 'El Rune')) | Where-Object { $_.Current.AutomationId -eq 'Tile' -and -not $_.Current.IsOffscreen } | ForEach-Object {
      $count = @($_.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match '^\d+$' })[0]
      [pscustomobject]@{ Tile = $_; Count = $(if ($null -ne $count) { [int]$count } else { 1 }); X = $_.Current.BoundingRectangle.X } })
}
# The tab's El Rune: the one in the second pane, right of the inventory's.
function TabRune { Runes | Where-Object { $_.X -gt $script:tabFrom } | Select-Object -First 1 }
function LooseRunes { @(Runes | Where-Object { $_.X -le $script:tabFrom }) }

Case 'materials-follow.rune-dragged-out-of-the-tab' -Tags materials-follow, materials, items, drag -Mode Casual -Changes {
  Pane 0 'Inventory'
  $panes = (PaneCount)
  if ($panes -lt 2) { ToggleSecondPane }
  Pane 1 'Materials'
  # The free cells are found once both panes are there: a second pane opening moves the first, and a point
  # taken before it was a point beside the grid - the rune let go there went nowhere, and said nothing
  # (in a full run, with the second pane closed in the settings the test starts from).
  Start-Sleep -Milliseconds 400
  $free = CellOf (WaitUntil { if (Tiles 'Horadric Cube') { Tile 'Horadric Cube' } } "the cube") 2 0 2 2
  $next = CellOf (Tile 'Horadric Cube') 3 0 2 2
  # The second pane's left edge: its picker's.
  $script:tabFrom = (PaneLine 1).Current.BoundingRectangle.X - 20
  $rune = WaitUntil { TabRune } "the tab's El Rune"
  $had = $rune.Count
  $loose = [int]((LooseRunes) | Measure-Object Count -Sum).Sum
  Step "El Rune in the tab: $had; loose: $loose"

  DragTo $rune.Tile $free
  $after = WaitUntil { $r = TabRune; if ($r -and $r.Count -ne $had) { $r } } "the tab's count to change" 5000
  Check ($after.Count -eq $had - 1) "dragged out: the tab says one fewer ($had -> $($after.Count))"
  Check (((LooseRunes) | Measure-Object Count -Sum).Sum -eq $loose + 1) "and the rune is in the inventory"

  # A move the tab has nothing to do with: the loose rune a cell along.
  DragTo $free $next; Start-Sleep -Milliseconds 900
  $still = WaitUntil { TabRune } "the tab's El Rune after the move"
  Check ($still.Count -eq $had - 1) "after a move elsewhere the tab still says $($had - 1) ($($still.Count))"
  ClickEl $still.Tile "the tab's El Rune"
  Check ([bool](WaitUntil { ByName 'El Rune' -Prefix | Where-Object { $_.Current.AutomationId -ne 'Tile' -and -not $_.Current.IsOffscreen } } "the panel showing it" 3000)) "and its tile still answers a click"
}

Case 'materials-follow.two-picked-dragged-onto-the-tab' -Tags materials-follow, materials, items, drag -Mode Casual -Changes {
  # Several at once back into the tab (it was "Not several at once"). In the personal
  # stash, which no lock keeps: two runes dragged out of the tab onto the two free cells left of the first
  # ring, both picked, the pair dragged onto the tab's El Rune - the tab counts both in again.
  Pane 0 'Personal Stash'
  $panes = (PaneCount)
  if ($panes -lt 2) { ToggleSecondPane }
  Pane 1 'Materials'
  Start-Sleep -Milliseconds 400
  $script:tabFrom = (PaneLine 1).Current.BoundingRectangle.X - 20
  $ring = WaitUntil { if (Tiles 'Steel Ring of Skill') { Tile 'Steel Ring of Skill' } } "the stash's first ring"
  $cells = @((CellOf $ring -1 0), (CellOf $ring -1 1))
  $now = (WaitUntil { TabRune } "the tab's El Rune").Count
  foreach ($cell in $cells) {
    DragTo (TabRune).Tile $cell -Hold 400
    $now--
    WaitUntil { $r = TabRune; if ($r -and $r.Count -eq $now) { $r } } "a rune out of the tab onto the stash ($now left)" 5000 | Out-Null
  }
  $pair = WaitUntil { $l = @(LooseRunes); if ($l.Count -eq 2) { , $l } } "two loose runes in the stash" 4000
  ClickEl $pair[0].Tile "one loose rune"; Start-Sleep -Milliseconds 400
  $b = $pair[1].Tile.Current.BoundingRectangle
  Front; [Fast]::keybd_event(0x11, 0, 0, 0); [Fast]::Click([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)); [Fast]::keybd_event(0x11, 0, 2, 0)
  WaitUntil { ByTip 'Store 2 selected items' -Prefix } "both loose runes picked" 4000 | Out-Null
  DragTo (@(LooseRunes)[0].Tile) (TabRune).Tile -Hold 400
  $back = $null
  try { $back = WaitUntil { $r = TabRune; if ($r -and $r.Count -eq $now + 2) { $r } } "both runes back in the tab" 5000 } catch { }
  Check ($null -ne $back) "two picked, dragged onto the tab: both counted in ($((TabRune).Count), from $now)"
  Check (@(LooseRunes).Count -eq 0) "and neither is left in the stash"
}
