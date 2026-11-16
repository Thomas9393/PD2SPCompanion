# Several items carried together, as only a window shows it: that a drag of the mouse with two picked reaches the vault's
# sheet - where both are stored into the folder and laid out by the rule, with no shape to keep -
# and, back, a shared page, where they keep the shape they are carried in: as far apart as the sheet had them.
# Two rings of the stash, picked together and carried by one of them.
#
# What the pages decide - which cell each lands on in the inventory, the stash and the vault as they are
# afterwards - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Window/GroupShapeTests.
#
# The rings are moved: the pad is put back after.
#
# Three things differ from the old script's lines, each for a way it could fail with nothing wrong:
#   - both drags wait on their target before letting go (-Hold, as the other scripts' drags of what a page
#     rebuilds for do): lifting a group rebuilds the pages, and a release 60 ms after the last step could come
#     before the page under the pointer had a cell to give - nothing took it, and the rings went back;
#   - the second drag presses on the first ring as it is found then, not on an element kept from before two clicks;
#   - a cell's size is read off a ring's own tile (CellPx), not written down as 35 pixels - which it is only at
#     Windows' 125% and a UI scale of 1.

function Half($el) { PageOf $el }
function TileOn([string]$side, $name) { @(Tiles $name | Where-Object { (Half $_) -eq $side })[0] }
function On([string]$side, [string]$id, $name = $null) {
  Steady {
    foreach ($e in (All (Cond $AE::AutomationIdProperty $id))) {
      if (-not $e.Current.IsOffscreen -and (Half $e) -eq $side -and (-not $name -or $e.Current.Name -eq $name)) { return $e }
    }
  }
}
# How far one tile lies from another, in pixels: (across, down).
function Apart($from, $to) { $a = $from.Current.BoundingRectangle; $b = $to.Current.BoundingRectangle; @([int]($b.X - $a.X), [int]($b.Y - $a.Y)) }
function Same($one, $other) { [math]::Abs($one[0] - $other[0]) -le 2 -and [math]::Abs($one[1] - $other[1]) -le 2 }
function CtrlClick($el, [string]$what) {
  Front
  [Fast]::keybd_event(0x11, 0, 0, 0); Start-Sleep -Milliseconds 60
  try { ClickEl $el $what } finally { Start-Sleep -Milliseconds 60; [Fast]::keybd_event(0x11, 0, 2, 0) }
  Start-Sleep -Milliseconds 250
}

Case 'group-shape.two-dragged-to-the-vault-are-laid-by-the-rule-and-come-back-as-they-lay' -Tags window, drag, vault, items -Changes {
  Pane 0 'Personal Stash'
  Start-Sleep -Milliseconds 400
  ClickEl (ById 'Bookmark_Vault_Right') "vault (right)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  ClickEl (WaitUntil { On 'Right' 'FolderRow' 'New folder' } "the tree's New folder") "New folder"
  Start-Sleep -Milliseconds 500

  # ---- two rings of the stash, some cells apart, picked together
  $one = 'Steel Ring of Skill'; $two = 'Entropy Gyre'
  $lay = Apart (WaitUntil { TileOn 'Left' $one } "the first ring") (WaitUntil { TileOn 'Left' $two } "the second ring")
  # A grid's cell in screen pixels, and half of one: a ring is one cell.
  $cell = CellPx (TileOn 'Left' $one); $half = $cell / 2
  Check ([math]::Abs($lay[0]) + [math]::Abs($lay[1]) -ge $cell) "two rings of the stash lie apart ($($lay -join ', ') px)"
  ClickEl (TileOn 'Left' $one) $one
  CtrlClick (TileOn 'Left' $two) $two

  # ---- carried by the first onto the vault's sheet, a few cells in: both stored into the folder, side by side
  # from its first cell - the rule's layout, not their shape in the stash
  $sheet = (ById 'Vault_Pages').Current.BoundingRectangle
  $toX = [int]($sheet.X + 12 + 6 * $cell + $half - $script:r.X); $toY = [int]($sheet.Y + 14 + 5 * $cell + $half - $script:r.Y)
  DragTo (TileOn 'Left' $one) @($toX, $toY) -Hold 400
  $there = try { WaitUntil { if ((TileOn 'Right' $one) -and (TileOn 'Right' $two)) { 'both' } } "both rings in the vault" 5000 } catch { $null }
  Check ([bool]$there) "both rings are in the vault's folder"
  if ($there) {
    $lay = Apart (TileOn 'Right' $one) (TileOn 'Right' $two)
    Check ([math]::Abs([math]::Abs($lay[0]) - $cell) -le 4 -and $lay[1] -eq 0) "laid side by side by the rule, whatever lay between them in the stash ($($lay -join ', ') px; a cell is $([math]::Round($cell, 1)))"
  }

  # ---- and back: picked together in the vault, carried by the first onto an empty shared page
  Pane 0 'Shared Page 5'
  Start-Sleep -Milliseconds 500
  # Both found before either is pressed: a tooltip up over the first can leave the tree unreadable for a moment.
  $first = WaitUntil { TileOn 'Right' $one } "the first ring, in the vault"; $second = WaitUntil { TileOn 'Right' $two } "the second ring, in the vault"
  ClickEl $first $one
  CtrlClick $second $two
  $picker = (PaneLine 0).Current.BoundingRectangle
  # Near the page's top left, a column in: the two lie a column apart on the sheet, either way round.
  $backX = [int]($picker.X + 2 * $cell + $half - $script:r.X); $backY = [int]($picker.Y + $picker.Height + 12 + 2 * $cell + $half - $script:r.Y)
  DragTo (WaitUntil { TileOn 'Right' $one } "the first ring, to carry") @($backX, $backY) -Hold 400
  $back = try { WaitUntil { if ((TileOn 'Left' $one) -and (TileOn 'Left' $two)) { 'both' } } "both rings on the shared page" 5000 } catch { $null }
  Check ([bool]$back) "both rings are on the shared page"
  if ($back) {
    $again = Apart (TileOn 'Left' $one) (TileOn 'Left' $two)
    Check (Same $again $lay) "and lie there as the sheet had them: carried together, they keep their shape ($($again -join ', ') against $($lay -join ', '))"
  }
}
