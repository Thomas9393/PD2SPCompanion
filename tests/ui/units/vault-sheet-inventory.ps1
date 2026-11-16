# From the vault's sheet to the inventory, as only a window shows it: that a drag of the mouse from the sheet finds the inventory's cell. The
# other way - an item let go of on the sheet stored into the folder shown, on no cell of the player's choosing -
# is drag-landing-marks.ps1's (the sheet framed, the item stored), drag-between-pages.ps1's (two picked) and
# group-shape.ps1's.
#
# What the two pages do once told - stored, withdrawn there, the counts either side - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Vault/VaultSheetInventoryTests. The menu of the sheet's item is the right
# click's (units/right-click.ps1, Items/RightClickTests).
#
# The inventory on the left page, the vault on the right; Casual, as the script these came from ran. Types no text.

$saves = "$pad\saves"
function Held { if ((& $script:Cli dump "$saves\PhySpearZon.d2s" 2>&1 | Out-String) -match 'items \((\d+)\)') { [int]$Matches[1] } }
function Pages { ById 'Vault_Pages' }
# A tile of that name on the vault's pages, not the inventory's.
function OnPages($name) { $area = (Pages).Current.BoundingRectangle; foreach ($t in @(Tiles $name)) { $b = $t.Current.BoundingRectangle; if ($b.X -ge $area.X -and $b.X -lt $area.X + $area.Width) { return $t } } }
# The inventory in the first pane, the vault on the right page at its top.
function VaultBesideInventory {
  Pane 0 'Inventory'
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Right' } "vault bookmark (right)") "vault (right)"
  WaitUntil { Pages } "the vault's pages" | Out-Null
  VaultGoTo 'Vault'
}

Case 'vault-sheet-inventory.dragged-off-the-sheet-is-withdrawn' -Tags vault, drag -Mode Casual -Changes {
  $count = VaultCount; $held = Held
  VaultBesideInventory
  $tome = WaitUntil { OnPages 'Tome of Town Portal' } "the vault's own tome"

  # Out of the vault: from its page onto an empty inventory cell, right of the Horadric Cube's top row.
  $empty = CellOf (WaitUntil { Tile 'Horadric Cube' } "the cube") 2 0 2 2
  DragTo $tome $empty
  WaitUntil { (VaultCount) -eq $count - 1 } "the row gone from the vault" 15000 | Out-Null
  Check ($null -eq (OnPages 'Tome of Town Portal')) "dragged onto the inventory: gone from the page"
  Check ((Held) -eq $held + 1) "and with the character ($(Held))"
}
