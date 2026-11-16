# Carrying items off the inventory page under a real drag (drags between pages only
# between the inventory and the vault, none to the Edit page, the grail or trade): a ring dragged onto the Edit
# page goes back, and nothing is written; two picked rings dragged onto the vault on the right page are both
# stored. From the personal stash - the test pad's inventory is locked.
#
# What the release decides either way is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Inventory/DragBetweenPagesTests.
#
# In Casual, as the script these cases come from ran.

# A point of the right page, in window pixels, well inside it: the Edit page there, or the vault's sheet under
# its one item - found from the window's size, not written down for a window 2560 wide.
function OnTheRightPage { @([int]($script:r.Width * 0.75), [int]($script:r.Height * 0.65)) }
function Stamp($file) { (WrittenAt "$pad\saves\$file") }
function Held { if ((& $script:Cli dump "$pad\saves\PhySpearZon.d2s" 2>&1 | Out-String) -match 'items \((\d+)\)') { [int]$Matches[1] } }
function CtrlClickAt($at) { Front; [Fast]::keybd_event(0x11, 0, 0, 0); ClickAt $at[0] $at[1]; [Fast]::keybd_event(0x11, 0, 2, 0) }

Case 'drag-between-pages.dropped-on-edit-goes-back' -Tags drag-between-pages, items, drag -Mode Casual {
  $held = Held
  Pane 0 'Personal Stash'
  # Two rings in the personal stash, column 1, rows 1 and 2 - read off their tiles.
  $first = CellOf (WaitUntil { Tile 'Steel Ring of Skill' } "the first ring")

  # onto the Edit page: back where it was, nothing written
  ClickEl (WaitUntil { ById 'Bookmark_Edit' } "Edit bookmark") "Edit"
  Start-Sleep -Milliseconds 600
  $was = Stamp 'PhySpearZon.d2s'
  DragTo $first (OnTheRightPage)
  Start-Sleep -Milliseconds 1500
  Check ((Stamp 'PhySpearZon.d2s') -eq $was -and (Held) -eq $held) "dropped on the Edit page: put back, nothing written"
  Check ($null -ne (Tile 'Steel Ring of Skill')) "and the ring is in the stash"
  # As the next case expects the page.
  Pane 0 'Inventory'
}

Case 'drag-between-pages.two-dropped-on-the-vault-are-stored' -Tags drag-between-pages, items, vault, drag -Mode Casual -Changes {
  $count = VaultCount; $held = Held
  Pane 0 'Personal Stash'
  # Two rings in the personal stash, column 1, rows 1 and 2 - read off their tiles.
  $first = CellOf (WaitUntil { Tile 'Steel Ring of Skill' } "the first ring")
  $second = CellOf (WaitUntil { Tile 'Felicitous Ring of Regrowth' } "the second ring")

  # two picked, onto the vault's sheet: both stored
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Right' } "vault bookmark (right)") "vault (right)"
  Start-Sleep -Milliseconds 600
  # Picked: the Store button's tooltip counts them. A click the page's fade swallows is clicked again.
  ClickUntil { ClickAt $first[0] $first[1] } { ByTip 'Store selected item in vault' -Prefix } "first ring selected" 800 | Out-Null
  ClickUntil { CtrlClickAt $second } { ByTip 'Store 2 selected items' -Prefix } "both rings picked" 800 | Out-Null
  $was = Stamp 'PhySpearZon.d2s'
  DragTo $first (WaitUntil { VaultPages } "the vault's sheet")
  WaitUntil { (Stamp 'PhySpearZon.d2s') -ne $was } "character written" 10000 | Out-Null
  WaitUntil { (VaultCount) -eq $count + 2 } "two rows in the vault" 10000 | Out-Null
  Check ((Held) -eq $held - 2) "two picked, dropped on the vault's sheet: both stored ($held -> $($held - 2))"
}
