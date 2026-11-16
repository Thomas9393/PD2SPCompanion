# Acting on what a search found, as only a window shows it: that a click reaches the list's two buttons - Select found,
# and Move found to folder with the vault's own move list opening over the vault page - that Move found is not
# lit while the vault holds none of the finds, and that Esc and Ctrl + A reach the page under the pointer while
# the Search page is up beside it.
#
# What the two tools decide - which finds become the selection, what is said where none lies in what is shown,
# the two charms of a vault folder, the Edit page's count, every find of the vault's moved whichever folder it
# lies in, Ctrl + A taking the finds and not the rest - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Search/SearchFindsTests.
#
# The keys are sent through the keyboard: those cases are not run under another input language.

Add-Type -AssemblyName System.Windows.Forms
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }

function Texts([string]$like) { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -like $like } | ForEach-Object { $_.Current.Name }) }
function Strip([string]$like, [int]$ms = 4000) { try { [bool](WaitUntil { if (@(Texts $like).Count) { 'said' } } "the strip: $like" $ms) } catch { $false } }
# How many the Store button says are selected (its tooltip), 0 with one or none.
function Picked {
  foreach ($b in (ByTip 'Store ' -Prefix)) { if ($b.Current.HelpText -match '^Store (\d+) selected items') { return [int]$Matches[1] } }
  0
}
function Listed($name) { foreach ($b in (All (Cond $AE::AutomationIdProperty 'MoveTarget'))) { if (-not $b.Current.IsOffscreen -and $b.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $b } } }
function Beside($name) { VaultRowCount (VaultRow $name) }

# The inventory's first pane on the personal stash, and a search for maps standing: three of them lie there.
function MapsInTheStash {
  Pane 0 'Personal Stash'
  Start-Sleep -Milliseconds 500
  ItemSearchFor 'map'
}
# The search emptied and the pane back on the inventory, where the next case looks for it.
function BackToTheInventory {
  ItemSearchEnd
  Start-Sleep -Milliseconds 300
  Pane 0 'Inventory'
}

Case 'search-finds.select-found-selects-and-esc-lets-go' -Tags search, select, keys -Typing {
  MapsInTheStash
  Check ((ItemSearchCount) -match '^\d+ found$') "a search for 'map': '$(ItemSearchCount)' - three of them in the stash shown"
  Check ((ById 'Found_Select').Current.IsEnabled -and -not (ById 'Found_Move').Current.IsEnabled) "Select found is lit; Move found to folder is not - the vault holds none of them"
  ClickEl (ById 'Found_Select') "select found"
  Check ([bool](WaitUntil { if ((Picked) -eq 3) { 'picked' } } "three picked" 3000)) "Select found: the three maps are the selection ($(Picked) selected)"
  Keys '{ESC}'
  Check ([bool](WaitUntil { if ((Picked) -eq 0) { 'none' } } "none picked" 3000)) "Esc: nothing selected"
  BackToTheInventory
}

Case 'search-finds.ctrl-a-reaches-the-page' -Tags search, select, keys -Typing {
  MapsInTheStash
  # As the script this came from reached it: the finds selected by the list's button, which hands the keys back
  # to the page, and let go of with Esc - the search keeps the keys while the focus is in it.
  ClickEl (ById 'Found_Select') "select found"
  WaitUntil { if ((Picked) -eq 3) { 'picked' } } "three picked" 3000 | Out-Null
  Keys '{ESC}'
  WaitUntil { if ((Picked) -eq 0) { 'none' } } "none picked" 3000 | Out-Null
  $map = WaitUntil { Tile 'Torajan Jungle Map' } "a map's tile"
  $b = $map.Current.BoundingRectangle
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 200
  Keys '^a'
  Check ([bool](WaitUntil { if ((Picked) -eq 3) { 'picked' } } "three picked" 3000)) "Ctrl + A while the search stands: the three finds, not the rest ($(Picked) selected)"
  Keys '{ESC}'
  BackToTheInventory
}

Case 'search-finds.move-found-opens-the-vaults-move-list' -Tags search, vault -Changes {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  Start-Sleep -Milliseconds 500
  ItemSearchFor 'charm'
  Check ((ItemSearchCount) -match '^\d+ found$' -and (ById 'Found_Move').Current.IsEnabled) "a search for 'charm' beside the vault page: '$(ItemSearchCount)', and Move found to folder is lit"
  VaultGoTo 'Small'
  Start-Sleep -Milliseconds 500

  # ---- every find of the vault's moved to one folder
  $charms = [int](Beside 'Grand') + [int](Beside 'Small') + [int](Beside 'Jewels')
  ClickEl (ById 'Found_Move') "move found to folder"
  $row = WaitUntil { Listed 'New folder' } "the move picker's New folder" 4000
  $title = @(Texts 'Move * found item*')[0]
  Check ($title -eq 'Move 4 found items') "the move picker is for all of them: '$title'"
  ClickEl $row "New folder"
  Check (Strip '4 items moved to New folder.') "moved: '4 items moved to New folder.'"
  Check ([bool](WaitUntil { if ((Beside 'New folder') -eq '4') { 'there' } } "four in New folder" 4000)) "New folder holds the four (the tree says $(Beside 'New folder')); the three they came from held $charms items before"
  ItemSearchEnd
}
