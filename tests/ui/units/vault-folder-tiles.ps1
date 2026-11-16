# A folder's own folders as tiles of its sheet (a folder as a fake item in the grid, and
# a user can interact with it as in Windows Explorer),
# as only a window shows them: that a tile is drawn over the sheet's cells - four of them by four, from the sheet's
# first cell, the items beginning under it to the pixel; that a click selects it and goes nowhere, and Enter or a
# double click goes into it; that Ctrl + click picks it with an item; that its right click is its folder's menu;
# that F2 names it where it stands; that one dragged onto another goes into it. And the tree's two buttons, the
# whole of it unfolded or folded.
#
# What the page decides - where the tiles stand at any width and in any order, what is selected and picked, what
# a carry moves, what the bin asks - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Vault/VaultFolderTilesTests.
#
# On the pad's vault: a Tome of Town Portal at its top; Charms holding Grand (one charm) and Small (two); Jewels
# (one item) holding Magic (one).
Add-Type -AssemblyName System.Windows.Forms

function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }
function Shown { VaultShown }
function Says($path, $what) { try { [bool](WaitUntil { if ((Shown) -eq $path) { 'there' } } $what 3000) } catch { $false } }
function Said([string]$text, [int]$ms = 4000) { try { [bool](WaitUntil { ByName $text } "the strip: $text" $ms) } catch { $false } }
function Count { $t = ById 'Vault_Count'; if ($t) { $t.Current.Name } }
function InTree($name) { $null -ne (VaultRow $name -AsIs) }
# What a tile says under its name: its count's line, within the tile.
function TileLine($tile) {
  $r = $tile.Current.BoundingRectangle
  $line = @((All (Cond $AE::AutomationIdProperty 'SheetFolder_Count')) | Where-Object {
      $b = $_.Current.BoundingRectangle
      -not $_.Current.IsOffscreen -and $b.X -ge $r.X - 1 -and $b.X + $b.Width -le $r.X + $r.Width + 1 -and $b.Y -ge $r.Y - 1 -and $b.Y + $b.Height -le $r.Y + $r.Height + 1 })[0]
  if ($line) { $line.Current.Name -replace '\r?\n', ' / ' } else { '' }
}

# The vault on the left page, at its top, the grail on the right.
function OpenVault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  ClickEl (ById 'Bookmark_Grail') "grail (right)"
  WaitUntil { -not (ById 'Item_Name') } "the Edit page gone" | Out-Null
  VaultGoTo 'Vault'
  WaitUntil { VaultFolderTile 'Charms' } "the tile Charms" 4000 | Out-Null
}

Case 'vault-folder-tiles.stand-first-on-the-sheet-four-cells-by-four' -Tags vault, folder-tiles, layout {
  OpenVault
  $tome = WaitUntil { Tile 'Tome of Town Portal' } "the tome at the top"
  $cell = CellPx $tome 1
  $charms = (VaultFolderTile 'Charms').Current.BoundingRectangle
  $jewels = (VaultFolderTile 'Jewels').Current.BoundingRectangle
  $t = $tome.Current.BoundingRectangle
  "  a cell is $cell px; Charms at $([int]$charms.X),$([int]$charms.Y) $([int]$charms.Width) x $([int]$charms.Height); the tome at $([int]$t.X),$([int]$t.Y)"

  Check ([math]::Abs($charms.Width - 4 * $cell) -le 1.5 -and [math]::Abs($charms.Height - 4 * $cell) -le 1.5) "a folder's tile is four cells by four ($([math]::Round($charms.Width / $cell, 2)) x $([math]::Round($charms.Height / $cell, 2)))"

  # Over the cells, to the pixel: the first tile from the sheet's first cell - where the first item's column is -
  # and the items from the fifth row.
  Check ([math]::Abs($charms.X - $t.X) -le 1) "the first tile begins at the sheet's first column, as the first item does ($([math]::Round($charms.X - $t.X, 1)) px off)"
  Check ([math]::Abs(($t.Y - $charms.Y) - 4 * $cell) -le 1) "the items begin under the tiles, four rows down ($([math]::Round(($t.Y - $charms.Y) / $cell, 2)) rows)"
  Check ([math]::Abs($jewels.Y - $charms.Y) -le 1 -and [math]::Abs((($jewels.X - $charms.X) / $cell) % 4) -le 0.05) "the tiles stand side by side, each on a fourth column ($([math]::Round(($jewels.X - $charms.X) / $cell, 2)) cells apart)"

  # A row of them fills the sheet to its end: the sheet is a whole number of tiles wide (it grew by two
  # columns until then, leaving a gap at the end).
  $columns = [math]::Round(((ById 'Vault_FolderTiles').Current.BoundingRectangle.Width) / $cell, 2)
  Check ($columns -ge 12 -and [math]::Abs($columns % 4) -le 0.05) "the sheet is a whole number of tiles wide: no gap at the end of a row of them ($columns columns)"

  # What it holds, as its row of the tree says it.
  Check ((TileLine (VaultFolderTile 'Charms')) -eq '2 folders') "a folder of folders says how many ('$(TileLine (VaultFolderTile 'Charms'))')"
  Check ((TileLine (VaultFolderTile 'Jewels')) -eq '1 item / 1 folder') "items and folders, a line each ('$(TileLine (VaultFolderTile 'Jewels'))')"
}

Case 'vault-folder-tiles.click-selects-enter-and-double-click-open' -Tags vault, folder-tiles, keys {
  OpenVault
  ClickEl (VaultFolderTile 'Charms') "the tile Charms"
  Start-Sleep -Milliseconds 700
  Check ((Shown) -eq 'Vault') "a click on a folder's tile goes nowhere ('$(Shown)')"
  $bin = @((All (Cond $AE::HelpTextProperty 'Delete folder (Del)')) | Where-Object { -not $_.Current.IsOffscreen })[0]
  Check ($null -ne $bin -and $bin.Current.IsEnabled) "it is selected: the bin says Delete folder, and is lit"

  Keys '{ENTER}'
  Check (Says 'Vault\Charms' "Charms shown") "Enter goes into it ('$(Shown)')"

  DoubleClickEl (WaitUntil { VaultFolderTile 'Small' } "the tile Small") "the tile Small"
  Check (Says 'Vault\Charms\Small' "Small shown") "a double click goes into it ('$(Shown)')"
}

Case 'vault-folder-tiles.ctrl-click-picks-it-with-an-item-and-the-arrows-reach-it' -Tags vault, folder-tiles, select, keys {
  OpenVault
  try {
    ClickEl (WaitUntil { Tile 'Tome of Town Portal' } "the tome") "the tome"
    CtrlClickEl (VaultFolderTile 'Jewels') "the tile Jewels"
    Check ([bool](WaitUntil { if ((Count) -like '*2 selected*') { 'two' } } "two selected" 3000)) "Ctrl + click on a tile: picked with the item ('$(Count)')"
    Keys '{ESC}'
    Check ([bool](WaitUntil { if ((Count) -notlike '*selected*') { 'none' } } "none selected" 3000)) "Esc lets both go ('$(Count)')"

    # The arrows: up from the item under the tiles is a tile, and Enter goes into it.
    ClickEl (Tile 'Tome of Town Portal') "the tome"
    Keys '{UP}'
    Keys '{ENTER}'
    Check (Says 'Vault\Charms' "Charms shown") "Up from the first item reaches the tile over it, and Enter opens that ('$(Shown)')"
  }
  finally { Keys '{ESC}' }
}

Case 'vault-folder-tiles.right-click-is-its-folders-menu' -Tags vault, folder-tiles, menu, right-click {
  OpenVault
  try {
    MenuOf (VaultFolderTile 'Jewels') 'the tile Jewels'
    Check ((MenuIcons) -eq 'Cut; Paste; Rename; Delete') "a tile's menu is its folder's: Cut, Paste, Rename, Delete at its head ('$(MenuIcons)')"
    Check ((MenuWords) -like 'Open*' -and (MenuWords) -like '*Unfold all inside; Fold all inside*') "Open first, and the tree under it unfolded or folded ('$(MenuWords)')"
  }
  finally { Keys '{ESC}' }
  Check ((Shown) -eq 'Vault') "and the page is where it was ('$(Shown)')"
}

Case 'vault-folder-tiles.tree-head-unfolds-and-folds-the-whole-tree' -Tags vault, folder-tiles, vault-tree {
  OpenVault
  try {
    # Folded first, whatever a case before left unfolded: a fold is kept while the program runs.
    if (InTree 'Small') { ClickEl (ById 'Vault_FoldTree') "Fold all"; Start-Sleep -Milliseconds 300 }
    Check ((InTree 'Charms') -and -not (InTree 'Small')) "the tree folded: Small is out of it"
    ClickEl (ById 'Vault_UnfoldTree') "Unfold all"
    Check ([bool](WaitUntil { if ((InTree 'Small') -and (InTree 'Grand') -and (InTree 'Magic')) { 'all' } } "every folder in the tree" 3000)) "Unfold all: every folder is in the tree"
    ClickEl (ById 'Vault_FoldTree') "Fold all"
    Check ([bool](WaitUntil { if (-not (InTree 'Small') -and -not (InTree 'Magic') -and (InTree 'Jewels')) { 'top' } } "the vault's own folders alone" 3000)) "Fold all: the vault's own folders alone"
  }
  finally {
    # Folded, as every case finds the tree.
    try { if (InTree 'Small') { ClickEl (ById 'Vault_FoldTree') "Fold all" } } catch { }
  }
}

# Named where it stands: a box over the tile's name, set
# through UI Automation - nothing typed through the keyboard but F2 and Enter.
Case 'vault-folder-tiles.f2-names-it-on-the-tile' -Tags vault, folder-tiles, keys -Changes {
  OpenVault
  ClickEl (VaultFolderTile 'Jewels') "the tile Jewels"
  Keys '{F2}'
  $box = WaitUntil { ById 'SheetFolder_Name' } "the name box on the tile" 3000
  $value = [System.Windows.Automation.ValuePattern]::Pattern
  $tile = (VaultFolderTile 'Jewels').Current.BoundingRectangle; $b = $box.Current.BoundingRectangle
  Check ($box.GetCurrentPattern($value).Current.Value -eq 'Jewels') "F2: a box holding the folder's name ('$($box.GetCurrentPattern($value).Current.Value)')"
  Check ($b.X -ge $tile.X - 1 -and $b.X + $b.Width -le $tile.X + $tile.Width + 1 -and $b.Y -ge $tile.Y -and $b.Y + $b.Height -le $tile.Y + $tile.Height) "it stands on the tile, within it"
  Check ((Shown) -eq 'Vault') "and the page goes nowhere ('$(Shown)')"

  $box.GetCurrentPattern($value).SetValue('Gems')
  Keys '{ENTER}'
  Check ([bool](WaitUntil { if ((VaultFolderTile 'Gems') -and -not (VaultFolderTile 'Jewels') -and -not (ById 'SheetFolder_Name')) { 'named' } } "the tile Gems" 4000)) "Enter keeps it: the tile is Gems, the box gone"
  Check ($null -ne (VaultRow 'Gems' -AsIs)) "and its row of the tree says the same"
}

Case 'vault-folder-tiles.a-tile-dragged-onto-another-goes-into-it' -Tags vault, folder-tiles, drag -Changes {
  OpenVault
  DragTo (VaultFolderTile 'Jewels') (VaultFolderTile 'Charms')
  Check (Said 'Moved to Charms.') "a folder's tile let go of on another: 'Moved to Charms.'"
  Check ([bool](WaitUntil { if (-not (VaultFolderTile 'Jewels')) { 'gone' } } "Jewels off the top" 3000)) "and it is no longer a folder of the top"
  DoubleClickEl (VaultFolderTile 'Charms') "the tile Charms"
  Check ([bool](WaitUntil { VaultFolderTile 'Jewels' } "the tile Jewels in Charms" 3000)) "it is a folder of Charms"
}
