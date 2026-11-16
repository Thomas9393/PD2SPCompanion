# The landing mark between the inventory and the vault, as only a window shows it (between these two the green
# landing mark once went missing): under a real drag to the inventory the
# cells a carried item would take are drawn lit - green where it would lie there, red where it would not - and
# the release finds the cell that was lit. To the vault there is no cell to light:
# the sheet is framed in green, and the item is stored into its folder. Read off the screen mid-drag, since the
# mark is all there is to it: the carried picture lies over the cells, so the pointer is held at a cell's far
# corner and the strip left clear is what is read.
#
# Which cells are lit, allowed or refused, and where the item then lies, is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Inventory/LandingMarksTests.
#
# From the personal stash - the test pad's inventory is locked.
Add-Type -AssemblyName System.Drawing

# Press on a point, go to another in steps, and stay there with the button down; then on to another; let go.
function Press($a) { Front; [Fast]::SetCursorPos($a[0], $a[1]) | Out-Null; Start-Sleep -Milliseconds 80; [Fast]::mouse_event(2, 0, 0, 0, 0) }
function GoTo($a, $b) {
  for ($i = 1; $i -le 14; $i++) { [Fast]::SetCursorPos([int]($a[0] + ($b[0] - $a[0]) * $i / 14), [int]($a[1] + ($b[1] - $a[1]) * $i / 14)) | Out-Null; Start-Sleep -Milliseconds 20 }
  [Fast]::SetCursorPos($b[0] + 1, $b[1]) | Out-Null; Start-Sleep -Milliseconds 60
  [Fast]::SetCursorPos($b[0], $b[1]) | Out-Null; Start-Sleep -Milliseconds 450
}
function Drop { [Fast]::mouse_event(4, 0, 0, 0, 0); Start-Sleep -Milliseconds 800 }
# How much of a strip of the screen is the mark's green, or its red, in hundredths.
function Lit($x, $y, $w, $h) {
  $bmp = New-Object System.Drawing.Bitmap ([int]$w), ([int]$h)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$x, [int]$y, 0, 0, $bmp.Size); $g.Dispose()
  $green = 0; $red = 0; $n = 0
  for ($i = 0; $i -lt $bmp.Width; $i++) { for ($j = 0; $j -lt $bmp.Height; $j++) {
    $c = $bmp.GetPixel($i, $j); $n++
    if ($c.G -gt $c.R + 20 -and $c.G -gt $c.B + 12) { $green++ } elseif ($c.R -gt $c.G + 35 -and $c.R -gt $c.B + 35) { $red++ }
  } }
  $bmp.Dispose()
  @{ Green = [math]::Round(100.0 * $green / $n); Red = [math]::Round(100.0 * $red / $n) }
}
# A cell by its top left corner: where the pointer is held in it (its far corner), and the strip down its near side.
# A cell is as wide as the ring's tile (StashBesideVault reads it), not 35 written down.
$script:LandingCell = 35
function Hold($corner) { @(($corner[0] + $script:LandingCell - 5), ($corner[1] + $script:LandingCell - 5)) }
function Strip($corner) { Lit ($corner[0] + 3) ($corner[1] + 3) 9 ($script:LandingCell - 8) }
# The stash's ring lies on its cell 1,1 and the vault's tome on the sheet's first: the grids are counted from them.
function Stash([int]$c, [int]$r) { @([int]($script:ringAt.X + ($c - 1) * $script:LandingCell), [int]($script:ringAt.Y + ($r - 1) * $script:LandingCell)) }
function Sheet([int]$c, [int]$r) { @([int]($script:tomeAt.X + $c * $script:LandingCell), [int]($script:tomeAt.Y + $r * $script:LandingCell)) }

# The personal stash in the pane, the vault beside it, and where the ring and the tome lie.
function StashBesideVault {
  Pane 0 'Personal Stash'
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Right' } "vault bookmark (right)") "vault (right)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  Start-Sleep -Milliseconds 700
  $script:ringAt = (WaitUntil { Tile 'Steel Ring of Skill' } "the ring").Current.BoundingRectangle
  $script:LandingCell = [int]$script:ringAt.Width
  $script:tomeAt = (WaitUntil { VaultTile 'Tome of Town Portal' } "the tome").Current.BoundingRectangle
}
# The ring carried from the stash onto the sheet, some cells in, and let go of there.
function RingToTheSheet {
  Press @([int]($script:ringAt.X + $script:ringAt.Width / 2), [int]($script:ringAt.Y + $script:ringAt.Height / 2))
  GoTo @([int]$script:ringAt.X, [int]$script:ringAt.Y) (Hold (Sheet 6 5))
  Drop
}

Case 'drag-landing-marks.stash-to-vault-lights-its-footprint-and-stores' -Tags drag-landing-marks, items, vault, drag -Mode Casual -Changes {
  try {
    StashBesideVault

    # From the stash onto the vault's sheet: the cells under it lit green - a cue of where it is let go of, though
    # the folder's rule lays it out - and the sheet no longer framed as a whole, read off
    # its left edge.
    $pages = (ById 'Vault_Pages').Current.BoundingRectangle
    $edge = { Lit ($pages.X + 3) ($pages.Y + 80) 6 120 }
    $idle = & $edge
    Press @([int]($script:ringAt.X + $script:ringAt.Width / 2), [int]($script:ringAt.Y + $script:ringAt.Height / 2))
    GoTo @([int]$script:ringAt.X, [int]$script:ringAt.Y) (Hold (Sheet 6 5))
    $cell = Strip (Sheet 6 5); $framed = & $edge
    if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'landing-to-vault.png') }
    Drop
    Check ($cell.Green -ge 60) "an item carried from the stash over the vault's sheet: the cell under it is lit green ($($cell.Green) in a hundred)"
    Check ($idle.Green -lt 10 -and $framed.Green -lt 10) "the sheet is not framed as a whole ($($idle.Green) -> $($framed.Green) in a hundred)"

    # Let go of on the sheet: stored into the folder shown, laid beside what it holds.
    $stored = WaitUntil { $t = VaultTile 'Steel Ring of Skill'; if ($t) { $t.Current.BoundingRectangle } } "the ring in the vault" 6000
    Check ([math]::Abs($stored.Y - $script:tomeAt.Y) -le 6 -and $stored.X - $script:tomeAt.X -le 3 * $script:LandingCell) "let go of there, it is stored and laid beside the folder's tome"
  }
  finally { [Fast]::mouse_event(4, 0, 0, 0, 0) }
}

Case 'drag-landing-marks.vault-to-stash-lights-red-then-green' -Tags drag-landing-marks, items, vault, drag -Mode Casual -Changes {
  try {
    StashBesideVault

    # The ring out of the way first, as the case above carries it: its cell 1,1 is then free, and a mark on it
    # is not under an item's picture.
    RingToTheSheet
    WaitUntil { VaultTile 'Steel Ring of Skill' } "the ring in the vault" 6000 | Out-Null

    # From the vault over the stash. A tome is one cell by two: on 1,1 it would lie over the second ring, on 1,2 -
    # red; on 8,4 and the cell under it nothing lies - green.
    $tome = (VaultTile 'Tome of Town Portal').Current.BoundingRectangle
    Press @([int]($tome.X + $tome.Width / 2), [int]($tome.Y + 12))
    GoTo @([int]$tome.X, [int]$tome.Y) (Hold (Stash 1 1))
    $blocked = Strip (Stash 1 1)
    if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'landing-to-stash-red.png') }
    Check ($blocked.Red -ge 60) "an item carried from the vault over the stash, where it would lie over another: lit red ($($blocked.Red) in a hundred)"

    GoTo (Hold (Stash 1 1)) (Hold (Stash 8 4))
    $free = Strip (Stash 8 4); $left = Strip (Stash 1 1)
    if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'landing-to-stash-green.png') }
    Check ($free.Green -ge 60) "and where it has room: lit green ($($free.Green) in a hundred)"
    Check ($left.Red -lt 10 -and $left.Green -lt 10) "the cells it has left are no longer lit"

    Drop
    $landed = WaitUntil { foreach ($t in (Tiles 'Tome of Town Portal')) { $b = $t.Current.BoundingRectangle; if ($b.X -lt (BookMiddle)) { return $b } } } "the tome in the stash" 6000
    $at = Stash 8 4
    Check ([math]::Abs($landed.X - $at[0]) -le 6 -and [math]::Abs($landed.Y - $at[1]) -le 6) "let go of there, it lies on that cell of the stash"
    $after = Strip (Stash 1 1)
    Check ($after.Red -lt 10 -and $after.Green -lt 10) "nothing stays lit once it is put down"
  }
  finally { [Fast]::mouse_event(4, 0, 0, 0, 0) }
}
