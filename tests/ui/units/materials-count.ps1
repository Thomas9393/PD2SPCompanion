# The materials tab's count, set on the Edit page with nothing to press after, as only a window shows it
# (keeping at once): that digits typed in the Quantity box, and a press of the stepper's plus,
# reach the tab's count. The tab's El Rune is chosen - the pad's shared stash holds eleven.
#
# What the count then is - the tab's at once, the file's once saved, the tile saying it, Undo taking the last
# change back - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Inventory/MaterialsCountTests.
#
# The count read back from the shared stash's file with the command line tool.
Add-Type -AssemblyName System.Windows.Forms

# The El Runes the file's materials tab holds.
function InFile {
  $text = & $script:Cli materials "$pad\saves\pd2_shared.stash" 2>&1 | Out-String
  if ($text -match '(?m)^\s*\d+\s+r01s\s+El Rune\s+(\d+)') { [int]$Matches[1] } else { -1 }
}
# El Rune tiles on screen, each with the count drawn inside it; the tab's is the one in the second pane.
function Runes {
  @((All (Cond $AE::NameProperty 'El Rune')) | Where-Object { $_.Current.AutomationId -eq 'Tile' -and -not $_.Current.IsOffscreen } | ForEach-Object {
      $count = @($_.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match '^\d+$' })[0]
      [pscustomobject]@{ Tile = $_; Count = $(if ($null -ne $count) { [int]$count } else { 1 }); X = $_.Current.BoundingRectangle.X } })
}
function TabRune { Runes | Where-Object { $_.X -gt $script:tabFrom } | Select-Object -First 1 }
# The Edit page's Quantity row: its box, level with the word, and the stepper's plus right of the box.
function QuantityBox {
  $word = ByName 'Quantity'
  if (-not $word) { return }
  $w = $word.Current.BoundingRectangle; $mid = $w.Y + $w.Height / 2
  @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object {
      $b = $_.Current.BoundingRectangle; -not $_.Current.IsOffscreen -and $b.X -gt $w.X -and [Math]::Abs($b.Y + $b.Height / 2 - $mid) -lt 24 })[0]
}
function Plus {
  $box = (QuantityBox).Current.BoundingRectangle
  @((All (Cond $AE::ControlTypeProperty $CT::Button)) | Where-Object {
      $b = $_.Current.BoundingRectangle; -not $_.Current.IsOffscreen -and $b.X -ge $box.Right -and [Math]::Abs($b.Y + $b.Height / 2 - ($box.Y + $box.Height / 2)) -lt 12 } |
    Sort-Object { $_.Current.BoundingRectangle.X })[0]
}
function Val($e) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }

Case 'materials-count.box-and-stepper-reach-the-tabs-count' -Tags materials-count, materials, items -Changes -Typing {
  $had = InFile
  Check ($had -eq 11) "start: the file's tab holds $had El Runes"

  Pane 0 'Inventory'
  if ((PaneCount) -lt 2) { ToggleSecondPane }
  Pane 1 'Materials'
  $script:tabFrom = (PaneLine 1).Current.BoundingRectangle.X - 20
  $rune = WaitUntil { TabRune } "the tab's El Rune"
  Check ($rune.Count -eq $had) "the tab's tile says $($rune.Count)"

  ClickUntil { ClickEl (TabRune).Tile "the tab's El Rune" } { QuantityBox } "the Quantity row on the Edit page" 800 | Out-Null
  Check ((Val (QuantityBox)) -eq "$had") "the Edit page's Quantity box says $(Val (QuantityBox))"

  # ---- typed
  ClickEl (QuantityBox) "the Quantity box"
  [System.Windows.Forms.SendKeys]::SendWait("^a25{ENTER}")
  Kept "the count typed"
  Check ((InFile) -eq 25) "25 typed, nothing pressed after: the file's tab holds $(InFile)"
  $tile = WaitUntil { $r = TabRune; if ($r -and $r.Count -eq 25) { $r } } "the tab's tile at 25" 4000
  Check ($tile.Count -eq 25) "and the tab's tile says $($tile.Count)"

  # ---- stepped
  ClickEl (Plus) "the stepper's plus"
  WaitUntil { (Val (QuantityBox)) -eq '26' } "the box at 26" 4000 | Out-Null
  Kept "the count stepped"
  Check ((InFile) -eq 26) "plus: the file's tab holds $(InFile)"
}
