# The mercenary's slots under the mouse (the mercenary's belt would not come off, so its slots are
# tested thoroughly): that a drag begun on each of the six pieces it wears lifts the piece off its
# panel, and that a drop on the box it left finds that slot again.
#
# What the page does with such a carry - the piece stored, the mercenary wearing one fewer, the character's belt
# rule not asked of the mercenary's belt, the piece worn again - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Mercenary/MercenarySlotsTests.
#
# The pad character's stash is emptied with the CLI and one ring put in it to read the grid's cells from.

function Stamp { (WrittenAt "$pad\saves\PhySpearZon.d2s") }
function Dump { & $script:Cli dump "$pad\saves\PhySpearZon.d2s" --no-stats 2>&1 | Out-String }
# The mercenary's part of the dump, and how many items it wears.
function Hired { $d = Dump; if ($d -match '(?s)== mercenary \((\d+)\)(.*)$') { @{ Count = [int]$Matches[1]; Text = $Matches[2] } } else { @{ Count = 0; Text = '' } } }

Case 'merc-slots.every-slot-takes-a-drag-off-and-on' -Tags mercenary, items, drag -Before {
  # An empty stash, and one ring at its first cell to count cells from.
  & $script:Cli clear "$pad\saves\PhySpearZon.d2s" | Out-Null
  & $script:Cli give "$pad\saves\PhySpearZon.d2s" --unique 'Nagelring' --backup "$pad\merc-slots-give" | Out-Null
} {
  $start = Hired
  $worn = @([regex]::Matches($start.Text, '(?m)^       \[\w+\s*\] (.+?)   \(') | ForEach-Object { $_.Groups[1].Value })
  Check ($start.Count -eq 6 -and $worn.Count -eq 6) "start: the mercenary wears six ($($worn -join ', '))"
  Check ((Dump) -match '(?s)== belt \(\d+\)') "start: the character's belt holds potions"

  Pane 0 'Personal Stash'
  # The Equipped section folded, so the stash and the mercenary are both on screen.
  ClickEl (WaitUntil { ByName 'Equipped' -Prefix } "the Equipped header") "fold Equipped"
  $ring = WaitUntil { Tile 'Nagelring' } "the ring in the stash"
  # And the page wheeled down until the mercenary's lowest slot is clear of the page's foot: the stash's
  # upper rows stay in sight above it.
  $b = $ring.Current.BoundingRectangle
  [Fast]::SetCursorPos([int]($b.X + 200), [int]($b.Y + 100)) | Out-Null
  $foot = $script:r.Y + $script:r.Height - 140
  for ($n = 0; $n -lt 12; $n++) {
    $low = @($worn | ForEach-Object { $t = @(Tiles $_); if ($t.Count) { $t[0].Current.BoundingRectangle.Bottom } else { [double]::MaxValue } } | Measure-Object -Maximum).Maximum
    if ($low -lt $foot) { break }
    Front; [Fast]::mouse_event(0x0800, 0, 0, -120, 0); Start-Sleep -Milliseconds 250
  }
  WaitUntil { (Tile 'Nagelring') -and (Tile $worn[0]) } "the stash's first row and the mercenary both on screen" | Out-Null
  Step "stash and mercenary on screen (wheeled $n)"

  foreach ($name in $worn) {
    # Off: onto the middle of the empty stash.
    $slot = CellOf (Tile $name)
    $ring = Tile 'Nagelring'
    $was = Stamp; $count = (Hired).Count
    DragTo (Tile $name) (CellOf $ring 4 2) -Hold 500
    $off = $false
    try { WaitUntil { (Stamp) -ne $was -and (Hired).Count -eq $count - 1 } "$name off the mercenary" 6000 | Out-Null; $off = $true } catch { }
    Check $off "$name comes off (the mercenary wears $((Hired).Count))"
    if (-not $off) { Shot "$pad\merc-slots-$($name -replace '\W', '').png"; continue }

    # On again: back onto the slot it left.
    $tile = WaitUntil { Tile $name } "$name in the stash"
    $was = Stamp
    DragTo $tile $slot -Hold 500
    $on = $false
    try { WaitUntil { (Stamp) -ne $was -and (Hired).Count -eq $count -and (Hired).Text.Contains($name) } "$name back on the mercenary" 6000 | Out-Null; $on = $true } catch { }
    Check $on "$name goes back on"
    if (-not $on) { Shot "$pad\merc-slots-$($name -replace '\W', '')-on.png" }
  }

  & $script:Cli verify "$pad\saves\PhySpearZon.d2s" | Out-String | ForEach-Object { Check ($_ -match 'ok') "the save still reads back" }
}
