# The vault's folder as one sheet, as only a window shows it: a folder laid out by a rule from its first cell, as wide as
# the page has room for - so with no way sideways, and laid out again as the page is made narrower; the wheel moving
# by a row of cells, Ctrl + wheel and PageUp/PageDown by a stretch of fifteen rows, Home/End to the ends; a tooltip
# gone the moment the sheet moves; the layout held while the pointer is on the sheet; and the rectangle scrolling
# it at its edge.
#
# How wide the sheet is for a room, what a card let go of on its sheet does, and what a click selects, are proven
# without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultSheetTests. The rule itself:
# tests/PD2SPCompanion.Tests/Collection/VaultLayoutTests. A drag about its own sheet, with two picked:
# units/vault-folder-row.ps1.
#
# No case here types text. The vault on the left, Edit on the right.
Add-Type -AssemblyName System.Windows.Forms

function Pages { ById 'Vault_Pages' }
function Across { try { (Pages).GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current.HorizontallyScrollable } catch { "?" } }
function At { try { [math]::Round((Pages).GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current.VerticalScrollPercent, 1) } catch { "?" } }
function Wheel([int]$notches, [byte]$with = 0) {
  $b = (Pages).Current.BoundingRectangle
  Front; [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 150
  if ($with) { [Fast]::keybd_event($with, 0, 0, 0) }
  [Fast]::mouse_event(0x0800, 0, 0, -120 * $notches, 0) | Out-Null; Start-Sleep -Milliseconds 250
  if ($with) { [Fast]::keybd_event($with, 0, 2, 0) }
  Start-Sleep -Milliseconds 250
}
function TipUp { try { @((All (Cond $AE::ControlTypeProperty $CT::ToolTip))).Count -gt 0 } catch { $false } }
# A folder long enough to scroll: the character's items copied into the vault's top four times over, before the
# program starts - a sheet ends where its items do, and the pad's top folder holds one.
$long = {
  foreach ($n in 1..4) { & $script:Cli vault --file (OpenVaultFile) --add "$pad\saves\PhySpearZon.d2s" | Out-Null }
}

# The vault on the left page, at its top and scrolled to its top: another folder gone to and back, since a
# folder's sheet begins at its top when it is come to.
function OpenVault([switch]$Long) {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { Pages } "the sheet" | Out-Null
  Start-Sleep -Milliseconds 600
  VaultGoTo 'Charms'
  VaultGoTo 'Vault'
  # A long folder's tome is wherever its order puts it, in sight or not: the sheet scrolling is what is waited for.
  if ($Long) { WaitUntil { if ((At) -is [double] -or (At) -is [decimal] -or (At) -eq 0) { 'laid' } } "the long folder laid out" | Out-Null; Start-Sleep -Milliseconds 600 }
  else { WaitUntil { Tile 'Tome of Town Portal' } "the tome on the sheet" | Out-Null }
}

Case 'vault-sheet.first-item-lies-top-left' -Tags vault, vault-sheet, layout {
  OpenVault

  # The folder's one item lies where the rule lays a folder's first: the sheet's first column, on the first row
  # under the folder's own folders - tiles of the sheet, first on it (the sheet's first cell,
  # until then).
  $tome = WaitUntil { Tile 'Tome of Town Portal' } "the tome on the sheet"
  $area = (Pages).Current.BoundingRectangle; $at = $tome.Current.BoundingRectangle; $cell = CellPx $tome
  $folder = (WaitUntil { VaultFolderTile 'Charms' } "the tile Charms").Current.BoundingRectangle
  $under = $at.Y - ($folder.Y + $folder.Height)
  Check ($folder.Y - $area.Y -lt $cell -and $at.X - $area.X -lt $cell -and [math]::Abs($under) -le 1.5) "the first item lies in the sheet's first column, on the first row under its folders' tiles ($([int]($at.X - $area.X)) from the sheet's side, $([math]::Round($under, 1)) under the tiles)"
  Check (-not (Across)) "and the sheet is as wide as its room: no way sideways ($(Across))"
}

Case 'vault-sheet.wheel-moves-by-rows-and-stretches' -Tags vault, vault-sheet, layout -Before $long {
  OpenVault -Long

  # The wheel: a row of cells a notch; with Ctrl a stretch of fifteen rows; with Shift sideways - which a sheet
  # as wide as its room has none of, so nothing moves.
  Check ((At) -eq 0) "the sheet opens at the top ($(At))"
  Check (-not (Across)) "the sheet is as wide as its room: no way sideways ($(Across))"
  Wheel 1;        $one = At; Check ($one -gt 0 -and $one -lt 100) "wheel down a notch: a row of cells ($one)"
  Wheel -1;       Check ((At) -eq 0) "wheel up a notch: back at the top ($(At))"
  Wheel 1 0x11;   $many = At; Check ($many -gt 5 * $one) "Ctrl + wheel down: fifteen rows on ($many, a row is $one)"
  Wheel -1 0x11;  Check ((At) -eq 0) "Ctrl + wheel up: fifteen rows back ($(At))"
  Wheel 1 0x10;   Check ((At) -eq 0) "Shift + wheel: sideways, so the sheet stays at the top ($(At))"
}

Case 'vault-sheet.tooltip-goes-as-the-sheet-moves' -Tags vault, vault-sheet, tooltip {
  OpenVault

  # A tooltip up over an item goes the moment the pages move: it would name an item no
  # longer under it - and one opened and closed at every notch was what the scroll stuttered on.
  $t = (Tile 'Tome of Town Portal').Current.BoundingRectangle
  Front; [Fast]::SetCursorPos([int]($t.X + $t.Width / 2) - 4, [int]($t.Y + $t.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 80
  [Fast]::SetCursorPos([int]($t.X + $t.Width / 2), [int]($t.Y + $t.Height / 2)) | Out-Null
  Check ([bool](WaitUntil { if (TipUp) { 'up' } } "the tome's tooltip" 4000)) "an item's tooltip comes up over it"
  [Fast]::mouse_event(0x0800, 0, 0, -120, 0) | Out-Null
  Start-Sleep -Milliseconds 120
  Check (-not (TipUp)) "the wheel turned: the tooltip is gone at once"
  [Fast]::mouse_event(0x0800, 0, 0, 120, 0) | Out-Null
  Start-Sleep -Milliseconds 500
}

Case 'vault-sheet.keys-scroll' -Tags vault, vault-sheet, keys -Before $long {
  OpenVault -Long

  # The keys scroll as the wheel does. The sheet has the keyboard once something on it is pressed: its foot.
  $b = (Pages).Current.BoundingRectangle
  Front; [Fast]::Click([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)); Start-Sleep -Milliseconds 300
  Front; [System.Windows.Forms.SendKeys]::SendWait('{END}'); Start-Sleep -Milliseconds 300;  Check ((At) -eq 100) "End: the end ($(At))"
  Front; [System.Windows.Forms.SendKeys]::SendWait('{HOME}'); Start-Sleep -Milliseconds 300; Check ((At) -eq 0) "Home: the top ($(At))"
  Front; [System.Windows.Forms.SendKeys]::SendWait('{PGDN}'); Start-Sleep -Milliseconds 300; $on = At; Check ($on -gt 0) "PageDown: fifteen rows on ($on)"
  Front; [System.Windows.Forms.SendKeys]::SendWait('{PGUP}'); Start-Sleep -Milliseconds 300; Check ((At) -eq 0) "PageUp: fifteen rows back ($(At))"
}

# The sheet follows the page: narrower, the folder is laid out again in fewer columns - an even number
# of them, with no way sideways - and wide again it is as it was. The columns are counted off the tiles in sight: how
# far the rightmost reaches from the leftmost, in cells of 28 units.
function Columns {
  $area = (Pages).Current.BoundingRectangle
  $tiles = @((All (Cond $AE::AutomationIdProperty 'Tile')) | Where-Object { $b = $_.Current.BoundingRectangle; -not $_.Current.IsOffscreen -and $b.X -ge $area.X -and $b.X -lt $area.X + $area.Width } | ForEach-Object { $_.Current.BoundingRectangle })
  if ($tiles.Count -eq 0) { return 0 }
  $left = ($tiles | ForEach-Object { $_.X } | Measure-Object -Minimum).Minimum; $right = ($tiles | ForEach-Object { $_.X + $_.Width } | Measure-Object -Maximum).Maximum
  [int][math]::Round(($right - $left) / (28 * (Unit)))
}
Case 'vault-sheet.narrower-page-lays-the-folder-out-again' -Tags vault, vault-sheet, layout -Before $long {
  OpenVault -Long
  $wide = Columns
  Check ($wide -ge 12 -and $wide % 4 -eq 0 -and -not (Across)) "wide: the folder lies in $wide columns, a whole number of fours, with no way sideways"
  try {
    Size 1600 900
    Start-Sleep -Milliseconds 900
    $narrow = WaitUntil { $c = Columns; if ($c -gt 0 -and $c -lt $wide) { $c } } "the folder laid out again in fewer columns" 4000
    Check ($narrow -ge 4 -and $narrow % 4 -eq 0) "the page made narrower: laid out again in $narrow columns, in fours and four at the least"
    Check (-not (Across)) "and still no way sideways ($(Across))"
    if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-sheet-narrower.png') }
  }
  finally {
    $script:show = 3
    try { Wide } catch { }
  }
  Check ([bool](WaitUntil { if ((Columns) -eq $wide) { 'back' } } "the folder in its columns of before" 4000)) "wide again: $wide columns, as it was"
}

# While the pointer is on the sheet an item that leaves leaves its place empty (clicked out one
# after another, the next was to be still under the pointer); the pointer gone, the folder closes up. The first of
# Small's two charms deleted by the keys, the pointer where it clicked.
Case 'vault-sheet.layout-held-while-the-pointer-is-on-the-sheet' -Tags vault, vault-sheet, keys -Changes {
  OpenVault
  VaultGoTo 'Small'
  $a = 'Small Charm of Fortune'; $b = 'Garnet Small Charm of Life'
  WaitUntil { if ((Tile $a) -and (Tile $b)) { 'both' } } "Small's two charms" | Out-Null
  $ax = [int](Tile $a).Current.BoundingRectangle.X; $bx = [int](Tile $b).Current.BoundingRectangle.X
  if ($ax -lt $bx) { $first = $a; $second = $b; $firstX = $ax; $secondX = $bx } else { $first = $b; $second = $a; $firstX = $bx; $secondX = $ax }

  ClickEl (Tile $first) $first
  WaitUntil { OnEdit $first } "$first selected" 3000 | Out-Null
  Front; [System.Windows.Forms.SendKeys]::SendWait('{DEL}')
  WaitUntil { ByTip 'Confirm deletion' -Prefix } "the bin armed" 3000 | Out-Null
  Front; [System.Windows.Forms.SendKeys]::SendWait('{DEL}')
  WaitUntil { if (-not (Tile $first)) { 'gone' } } "$first deleted" 4000 | Out-Null
  Start-Sleep -Milliseconds 500
  Check ([math]::Abs([int](Tile $second).Current.BoundingRectangle.X - $secondX) -le 2) "the first deleted, the pointer on the sheet: the second lies where it lay"

  # The pointer off the sheet, onto the path's row: the folder closes up.
  $path = (ById 'Vault_Folder').Current.BoundingRectangle
  [Fast]::SetCursorPos([int]($path.X + $path.Width / 2), [int]($path.Y + $path.Height / 2)) | Out-Null
  Check ([bool](WaitUntil { if ([math]::Abs([int](Tile $second).Current.BoundingRectangle.X - $firstX) -le 2) { 'closed' } } "the folder closed up" 3000)) "the pointer gone from the sheet: the second is in the first cell"
}

# The rectangle held at the sheet's edge scrolls it (it never did): begun in the sheet's left
# margin - the one strip of a full folder that is no item - and held at its foot, the sheet moves on under it.
Case 'vault-sheet.rectangle-at-the-edge-scrolls-the-sheet' -Tags vault, vault-sheet, drag -Before $long {
  OpenVault -Long
  Check ((At) -eq 0) "the sheet at its top ($(At))"
  $b = (Pages).Current.BoundingRectangle
  $x0 = [int]($b.X + 3); $y0 = [int]($b.Y + $b.Height / 2)
  Front; [Fast]::SetCursorPos($x0, $y0) | Out-Null; Start-Sleep -Milliseconds 150
  [Fast]::mouse_event(0x0002, 0, 0, 0, 0) | Out-Null; Start-Sleep -Milliseconds 100
  foreach ($i in 1..6) { [Fast]::SetCursorPos($x0 + 30 * $i, $y0 + 20 * $i) | Out-Null; Start-Sleep -Milliseconds 40 }
  [Fast]::SetCursorPos($x0 + 200, [int]($b.Y + $b.Height - 6)) | Out-Null
  Start-Sleep -Milliseconds 900
  if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-rectangle-edge.png') }
  $down = At
  [Fast]::mouse_event(0x0004, 0, 0, 0, 0) | Out-Null; Start-Sleep -Milliseconds 300
  Check ($down -gt 0) "held at the foot, the sheet scrolls on under the rectangle ($down)"
  Front; [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
}
