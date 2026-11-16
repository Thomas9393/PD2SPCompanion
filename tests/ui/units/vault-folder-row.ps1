# The vault's path row and its sheet's picking, as only a window shows them: that the list of folders under the path goes to a folder and says the whole way
# to it; that it folds by a chevron without the row being
# gone to, and Esc shuts it; that Ctrl + click picks on the sheet and a drag of one picked carries them; that
# Ctrl + A and a letter typed reach the sheet; that the folder's name box takes what is typed where the path
# stood; that the folder's tools and the move picker are there to press; and that a drag let go of on the path
# opens the picker.
#
# What each of them then does - what the row counts, which folders fold, what a pick selects, where the carried
# lie, what a name, a move and a delete leave in the vault - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Vault/VaultFolderRowTests.
#
# On the pad's vault: a Tome of Town Portal at its top; Charms holding Grand (one charm) and Small (two: Small
# Charm of Fortune, Garnet Small Charm of Life); Jewels (one item) holding Magic (one).
Add-Type -AssemblyName System.Windows.Forms

function Count { $t = ById 'Vault_Count'; if ($t) { $t.Current.Name } }
# What the path says: the folder shown, the whole way to it.
function Shown { VaultShown }
# A row of the list of folders open under the path, by the folder's name.
function Entry($name) { VaultRow $name -Listed }
function GoTo($name) {
  VaultList $name
  ClickEl (Entry $name) "folder '$name'"
  WaitUntil { (Shown) -like "*$name" } "the path says $name" 3000 | Out-Null
}
# A row of the move picker: its button (MoveTarget) holding the folder's name. By the id, not any button: the
# vault's top folder is "Vault", and so are the bookmark and the sidebar's button.
function Target($name) { foreach ($b in (All (Cond $AE::AutomationIdProperty 'MoveTarget'))) { if (-not $b.Current.IsOffscreen -and $b.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $b } } }
function CtrlClick($el, [string]$what) {
  if ($null -eq $el) { throw "not found, not clicked: $what" }
  Front; $b = $el.Current.BoundingRectangle
  [Fast]::keybd_event(0x11, 0, 0, 0); [Fast]::Click([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)); [Fast]::keybd_event(0x11, 0, 2, 0)
}
function ShiftClick($el, [string]$what) {
  if ($null -eq $el) { throw "not found, not clicked: $what" }
  Front; $b = $el.Current.BoundingRectangle
  [Fast]::keybd_event(0x10, 0, 0, 0); [Fast]::Click([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)); [Fast]::keybd_event(0x10, 0, 2, 0)
}
function Spot($name) { $b = (Tile $name).Current.BoundingRectangle; @([int]$b.X, [int]$b.Y) }
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }

# The vault on the left page, at its top, the grail on the right: where every case here begins.
function OpenVault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's pages" | Out-Null
  ClickEl (ById 'Bookmark_Grail') "grail (right)"
  WaitUntil { -not (ById 'Item_Name') } "the Edit page gone" | Out-Null
  VaultGoTo 'Vault'
}
# In Small, its two charms drawn.
function InSmall {
  OpenVault
  GoTo 'Small'
  WaitUntil { Tile 'Small Charm of Fortune' } "the charms" | Out-Null
}

Case 'vault-folder-row.list-chevron-folds-the-list-stays' -Tags vault, vault-folder-row {
  OpenVault
  GoTo 'Small'
  Check ((Shown) -eq 'Vault\Charms\Small') "the list of folders goes to a folder two deep, and the path says the whole way ('$(Shown)')"

  # The list folds as the tree does, and by the same folds (folded as it opens):
  # a folder's chevron brings its folders in and puts them away, the list staying open and the folder
  # shown the folder shown - whose own folder's chevron works as any other's.
  ClickUntil { ClickEl (ById 'Vault_Folder') "all folders" } { VaultRow 'Jewels' -Listed -AsIs -Id 'FoldPlace' } "the list of folders, its chevrons" 800 | Out-Null
  if (VaultRow 'Magic' -Listed -AsIs) { InvokeEl (VaultRow 'Jewels' -Listed -AsIs -Id 'FoldPlace') "Jewels folded, to begin from"; Start-Sleep -Milliseconds 300 }
  Check ((VaultRow 'Charms' -Listed -AsIs -Id 'FoldPlace').Current.IsEnabled) "in Small, the chevron of Charms is live as any other"
  Check ($null -eq (VaultRow 'Magic' -Listed -AsIs)) "Magic is folded away under Jewels"
  ClickEl (VaultRow 'Jewels' -Listed -AsIs -Id 'FoldPlace') "the chevron of Jewels"
  Check ([bool](WaitUntil { VaultRow 'Magic' -Listed -AsIs } "Magic in the list" 3000)) "Jewels unfolded: Magic is in the list"
  Check ($null -ne (VaultRow 'Jewels' -Listed -AsIs) -and (Shown) -eq 'Vault\Charms\Small') "the list is still open, and the folder shown still shown ('$(Shown)')"
  ClickEl (VaultRow 'Jewels' -Listed -AsIs -Id 'FoldPlace') "the chevron of Jewels, again"
  Check ([bool](WaitUntil { if (-not (VaultRow 'Magic' -Listed -AsIs)) { 'gone' } } "Magic out of the list" 3000)) "folded: Magic is out of the list"
  Keys '{ESC}'
  Check ([bool](WaitUntil { if (-not (VaultRow 'Jewels' -Listed -AsIs)) { 'shut' } } "the list shut" 3000)) "Esc shuts the list"
}

# Shift+click picks the run from the item clicked before, in the folder's reading order - across its stretches
# (tests/PD2SPCompanion.Shell.Tests/Vault/VaultShiftRunTests): here the two charms, side by side.
Case 'vault-folder-row.shift-click-picks-the-run' -Tags vault, vault-folder-row {
  InSmall
  $a = 'Small Charm of Fortune'; $b = 'Garnet Small Charm of Life'
  ClickEl (Tile $a) $a; Start-Sleep -Milliseconds 300
  ClickUntil { ShiftClick (Tile $b) $b } { (Count) -match '2 selected' } "the run picked" 800 | Out-Null
  Check ((Count) -match '^2 items . 2 selected$') "click, then Shift+click: the run from one to the other ('$(Count)')"
  Keys '{ESC}'
}

Case 'vault-folder-row.ctrl-click-picks-drag-carries-both' -Tags vault, vault-folder-row, drag -Changes {
  InSmall

  # Ctrl+click picks on the sheet; a drag of one carries both. Let go of on their own sheet they stay where the
  # rule has them (no item has a cell to be moved to); on a folder's row of the tree, both go.
  $a = 'Small Charm of Fortune'; $b = 'Garnet Small Charm of Life'
  CtrlClick (Tile $a) $a; Start-Sleep -Milliseconds 300
  ClickUntil { CtrlClick (Tile $b) $b } { (Count) -match '2 selected' } "two picked" 800 | Out-Null
  Check ((Count) -match '^2 items . 2 selected$') "Ctrl+click picks on the sheet ('$(Count)')"
  $wasA = Spot $a; $wasB = Spot $b
  DragTo (Tile $a) @(([int]($wasA[0] + 10 * (CellPx (Tile $a)) + 10 - $script:r.X)), ([int]($wasA[1] + 10 - $script:r.Y)))
  Start-Sleep -Milliseconds 700
  $nowA = Spot $a; $nowB = Spot $b
  Check ($nowA[0] -eq $wasA[0] -and $nowB[0] -eq $wasB[0] -and (Count) -match '^2 items') "dragged ten cells along their own sheet: both lie where they lay"

  DragTo (Tile $a) (VaultRow 'Grand')
  Check ([bool](WaitUntil { if ((Count) -match '^0 items' -and (VaultRowCount (VaultRow 'Grand')) -eq '3') { 'moved' } } "both charms in Grand" 4000)) "a drag of one of two picked onto a folder carries both into it ('$(Count)', Grand $(VaultRowCount (VaultRow 'Grand')))"
  Keys '{ESC}'
}

Case 'vault-folder-row.ctrl-a-picks-the-folders-items' -Tags vault, vault-folder-row, keys -Typing {
  InSmall

  # Ctrl+A picks the folder's items - beside the grail: beside the Edit page one item is selected at a time.
  ClickEl (ById 'Vault_Pages') "the pages (the keys are theirs)"
  Keys '^a'
  Check ((Count) -match '^2 items . 2 selected$') "Ctrl+A picks the folder's items ('$(Count)')"
  Keys '{ESC}'
}

# Explorer's Rename (it renamed the folder shown, not the one selected; the
# same behavior as Windows Explorer): the tool names the folder SELECTED, on its tile - and is dimmed with none.
# It named the folder shown, where the path stood, until then.
Case 'vault-folder-row.rename-folder-names-the-selected-folder-on-its-tile' -Tags vault, vault-folder-row, folder-tiles, keys -Changes -Typing {
  OpenVault
  GoTo 'Charms'
  WaitUntil { VaultFolderTile 'Small' } "the tile Small" 3000 | Out-Null
  Check (-not (ById 'Vault_RenameFolder').Current.IsEnabled) "nothing selected: Rename folder is dimmed"

  ClickEl (VaultFolderTile 'Small') "the tile Small"
  Check ([bool](WaitUntil { if ((ById 'Vault_RenameFolder').Current.IsEnabled) { 'lit' } } "Rename folder lit" 3000)) "a folder's tile selected: Rename folder is lit"
  ClickEl (ById 'Vault_RenameFolder') "rename folder"
  WaitUntil { ById 'SheetFolder_Name' } "the name box on the tile" 3000 | Out-Null
  Check ((Shown) -eq 'Vault\Charms') "the name is typed on the tile: the page goes nowhere ('$(Shown)')"
  Start-Sleep -Milliseconds 300
  Keys '^aTiny{ENTER}'
  Check ([bool](WaitUntil { if ((VaultFolderTile 'Tiny') -and -not (VaultFolderTile 'Small') -and (Shown) -eq 'Vault\Charms') { 'named' } } "the folder renamed" 3000)) "Rename folder: the selected folder is named, where it stands ('$(Shown)')"
}

Case 'vault-folder-row.new-folder-is-named-then-deleted' -Tags vault, vault-folder-row, keys -Changes -Typing {
  InSmall

  # A new folder is a tile of the folder shown, its name box open on it - Explorer's.
  ClickEl (ByTip 'New folder') "new folder"
  WaitUntil { ById 'SheetFolder_Name' } "the new folder's name box, on its tile" 3000 | Out-Null
  Check ((Shown) -eq 'Vault\Charms\Small') "New folder: made here, the page staying ('$(Shown)')"
  Start-Sleep -Milliseconds 300
  Keys '^aInner{ENTER}'
  Check ([bool](WaitUntil { if ((VaultFolderTile 'Inner') -and (Shown) -eq 'Vault\Charms\Small') { 'in' } } "the tile Inner" 3000)) "and named on its tile ('$(Shown)')"

  # Asked about as an item is, though empty - a bin always asks:
  # Delete arms the bin, and Delete again deletes. Its tile is the one selected.
  Keys '{DELETE}'
  Start-Sleep -Milliseconds 400
  Check ($null -ne (VaultFolderTile 'Inner')) "Delete, an empty folder selected: still there at the first press"
  Keys '{DELETE}'
  Check ([bool](WaitUntil { if (-not (VaultFolderTile 'Inner')) { 'gone' } } "Inner gone" 3000)) "and gone at the second"
  Check ((Shown) -eq 'Vault\Charms\Small') "and the page is where it was ('$(Shown)')"
}

# The folder shown, moved: by the menu of its step of the path (the tools line has no Move folder
# for the folder shown - a tool acts on what is selected, and Move to folder takes a selected folder).
Case 'vault-folder-row.folder-shown-is-moved-by-its-menu' -Tags vault, vault-folder-row, menu -Changes {
  InSmall

  # Moved by the picker: still the folder shown, under its new path.
  $step = @((All (Cond $AE::AutomationIdProperty 'Vault_Crumb')) | Where-Object { $_.Current.Name -eq 'Small' -and -not $_.Current.IsOffscreen })[0]
  MenuOf $step "the path's Small"
  MenuPick 'Move to folder...'
  ClickEl (WaitUntil { Target 'Jewels' } "the picker's Jewels") "Jewels"
  Check ([bool](WaitUntil { if ((Shown) -eq 'Vault\Jewels\Small') { 'moved' } } "Small under Jewels" 3000)) "Move to..., in the folder's menu: under Jewels, and still shown ('$(Shown)')"
}

Case 'vault-folder-row.drop-on-the-path-opens-the-picker' -Tags vault, vault-folder-row, drag -Changes {
  InSmall

  # A drag let go of on the path opens the move picker for it.
  DragTo (Tile 'Small Charm of Fortune') (ById 'Vault_Folder')
  ClickEl (WaitUntil { Target 'Vault' } "the picker's top") "Vault"
  Check ([bool](WaitUntil { if ((Count) -eq '1 item') { 'one' } } "one charm left" 3000)) "a drag let go of on the path: the picker, and the item moved ('$(Count)')"
}
