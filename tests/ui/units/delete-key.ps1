# The Delete key, as only a window shows it (the Delete key once did nothing on the
# personal and shared stash): that the key reaches the inventory's bin while a tile of a pane is selected, and the
# vault's bin while a card of the vault on the right page is - pressed twice it deletes, as the bin's two
# clicks do.
#
# What the bin decides - the question, the delete, "Deleted ..." left on the strip after the shared stash's
# save, two picked at once - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Inventory/DeleteKeyTests.
Add-Type -AssemblyName System.Windows.Forms

function Texts { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) }
function PressDelete { Front; [System.Windows.Forms.SendKeys]::SendWait('{DEL}'); Start-Sleep -Milliseconds 400 }

# The tiles under a pane's picker - the pane's own grid - top to bottom; the first of them.
function TilesInPane([int]$pane) {
  $picker = PaneLine $pane
  $p = $picker.Current.BoundingRectangle
  @((All (Cond $AE::AutomationIdProperty 'Tile')) |
    Where-Object { $b = $_.Current.BoundingRectangle; -not $_.Current.IsOffscreen -and $_.Current.Name -and $b.X -ge $p.X - 40 -and $b.X -lt $p.X + 700 -and $b.Y -gt $p.Y } |
    Sort-Object { $_.Current.BoundingRectangle.Y }, { $_.Current.BoundingRectangle.X })
}
function FirstTileIn([int]$pane) { @(TilesInPane $pane)[0] }

# A tile selected, Delete twice: asked, then gone, said on the strip.
function DeleteTile($tile, [string]$where) {
  $name = $tile.Current.Name
  $count = @(Tiles $name).Count
  try { ClickUntil { ClickEl $tile "$name ($where)" } { OnEdit $name } "$name on the Edit page" 800 | Out-Null }
  catch { Shot "$pad\delete-key-fail.png"; "  Item_Name now: '$((ById 'Item_Name').Current.Name)'"; throw }
  PressDelete
  Check (@(Texts | Where-Object { $_ -like "Delete $name`?*" }).Count -gt 0) "$where`: the first Delete asks"
  PressDelete
  Check (@(Tiles $name).Count -eq $count - 1) "$where`: the second Delete deletes $name"
  Check (@(Texts | Where-Object { $_ -eq "Deleted $name." }).Count -gt 0) "$where`: 'Deleted $name.' on the strip"
}

Case 'delete-key.the-key-is-the-inventorys-bin' -Tags delete-key, items, keys -Changes {
  # The pad's inventory is locked (items there cannot be selected to go), so the stash - which the user named.
  Pane 0 'Personal Stash'
  Start-Sleep -Milliseconds 500
  DeleteTile (WaitUntil { FirstTileIn 0 } "a tile in Personal Stash") 'Personal Stash'
}

Case 'delete-key.the-key-is-the-right-vaults-bin' -Tags delete-key, vault, keys -Changes {
  # A vault card on the right page (the vault-delete test's cards): Charms, Small, a charm - Delete twice.
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Right' } "vault bookmark (right)") "vault (right)"
  WaitUntil { VaultPages } "the vault on the right" | Out-Null
  VaultGoTo 'Small'; WaitUntil { VaultTile 'Small Charm of Fortune' } "Small opened" | Out-Null
  $count = VaultCount
  ClickEl (VaultTile 'Small Charm of Fortune') "Small Charm of Fortune"
  Start-Sleep -Milliseconds 400
  PressDelete
  PressDelete
  $gone = $false
  try { WaitUntil { (VaultCount) -eq $count - 1 } "one row gone" 4000 | Out-Null; $gone = $true } catch { }
  Check $gone "vault on the right page: Delete twice deletes the card ($count -> $(VaultCount))"
}
