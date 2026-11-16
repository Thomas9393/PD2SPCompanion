# A search on the vault's sheet, and the folder's map, as only a window shows them: that the folder's sheet stays under a search, its items on it, the Search page
# beside it; that a click on a find of the vault's reaches it; and the folder's map - there where the scroll bar
# would be, a press on it scrolling the sheet, its button swapping it for the plain bar.
#
# What the search decides on the page - what the row counts, that the tree goes on counting items, which folder
# a find shows and what it selects, what the search's brush ends - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Vault/VaultSearchSheetTests.
#
# The vault on the left. Nothing here changes what is saved.
function Count { $t = ById 'Vault_Count'; if ($t) { $t.Current.Name } }
function At { try { [math]::Round((VaultPages).GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current.VerticalScrollPercent, 1) } catch { "?" } }
# The vault on the left page, at its top: where every case here begins.
function OpenVault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { VaultPages } "the vault's sheet" | Out-Null
  VaultGoTo 'Vault'
}

Case 'vault-search-sheet.find-clicked-shows-its-folder' -Tags vault, vault-search-sheet, search {
  OpenVault

  # A narrower search, from the top: its find in the vault is a row under its folder; clicked, the folder is
  # shown and the item selected - and the Search page stays.
  ItemSearchFor 'fortune'
  # The vault's row, the last of that name: the character holds one too, listed first.
  $rows = ItemSearchRows 'Small Charm of Fortune'
  Check ($rows.Count -ge 2) "the search is listed: $($rows.Count) rows of that name"
  ClickEl $rows[$rows.Count - 1] "the vault's find"
  Check ([bool](WaitUntil { if ((VaultShown) -like '*Small') { 'there' } } "Small shown" 3000)) "a find clicked: its folder is shown ('$(VaultShown)')"
  Check ($null -ne (VaultTile 'Small Charm of Fortune')) "on its sheet, the search standing: the item is drawn there"
  Check ((Count) -match '^2 items . 1 found$') "the row says the folder's own: '$(Count)'"
  Check ([bool](WaitUntil { if ((ById 'Vault_MoveItems').Current.IsEnabled) { 'selected' } } "the item selected" 3000)) "and the item is selected"
  Check ((ItemSearchCount) -match 'found' -and (ItemSearchRows).Count -gt 0) "the Search page stays, its list with it"
  ItemSearchEnd
}

Case 'vault-search-sheet.map-press-scrolls-the-sheet' -Tags vault, vault-search-sheet, layout {
  OpenVault
  # Another folder's sheet begins at its top.
  VaultGoTo 'Small'
  Start-Sleep -Milliseconds 300

  # The folder's map stands where the scroll bar would, to begin with; a press on it scrolls the pages, and the
  # Minimap button gives the plain scroll bar back.
  $map = WaitUntil { ById 'Vault_Map' } "the folder's map" 3000
  Check ([bool]$map) "the folder's map is there to begin with"
  Check ((At) -eq 0) "the pages are at the top ($(At))"
  $m = $map.Current.BoundingRectangle
  Front; [Fast]::Click([int]($m.X + $m.Width / 2), [int]($m.Y + 130))
  Start-Sleep -Milliseconds 500
  Check ((At) -gt 0) "a press on the map, further down: the pages scroll there ($(At))"
  if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-search-map.png') }
  ClickEl (ById 'Vault_Minimap') "minimap off"
  Check ([bool](WaitUntil { if (-not (ById 'Vault_Map')) { 'gone' } } "the map gone" 3000)) "the Minimap button: the plain scroll bar in its place"
  ClickEl (ById 'Vault_Minimap') "minimap on"
  Check ([bool](WaitUntil { ById 'Vault_Map' } "the map back" 3000)) "and the map again"
}
