# The ways about the vault, as only a window shows them: that a click reaches a row of the tree, a step of the path and the three arrows; that a
# double click on a folder's tile on the sheet goes into it; that Alt with an arrow reaches the page; that the
# lists open under the path; that the two toggles take the tree and the map out of the window; that a drag is
# let go of on the folder under the pointer; and how far the tree's edge can be dragged. (The folders are
# tiles of the sheet.)
#
# Where each of them leads - the path, Back and Forward over where the page has been, what a list holds, what a
# drop moves and what the row then counts - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Vault/VaultWaysTests.
#
# On the pad's vault: a Tome of Town Portal at its top; Charms holding Grand (one charm) and Small (two); Jewels
# (one item) holding Magic (one).
Add-Type -AssemblyName System.Windows.Forms

function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }
function Count { $t = ById 'Vault_Count'; if ($t) { $t.Current.Name } }
function Shown { VaultShown }
function Says($path, $what) { [bool](WaitUntil { if ((Shown) -eq $path) { 'there' } } $what 3000) }
function Can($id) { $b = ById $id; $b -and $b.Current.IsEnabled }
function Crumb($name) { @((All (Cond $AE::AutomationIdProperty 'Vault_Crumb')) | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen })[0] }
function CrumbList($name) { @((All (Cond $AE::AutomationIdProperty 'Vault_CrumbList')) | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen })[0] }
# The number beside a folder's name on its row of the tree ('' for none).
function Beside($name) { VaultRowCount (VaultRow $name) }

# The vault on the left page, at its top, the grail on the right: where every case here begins.
function OpenVault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  ClickEl (ById 'Bookmark_Grail') "grail (right)"
  WaitUntil { -not (ById 'Item_Name') } "the Edit page gone" | Out-Null
  VaultGoTo 'Vault'
}

Case 'vault-ways.tree-row-goes-to-its-folder' -Tags vault, vault-ways {
  OpenVault
  VaultGoTo 'Small'
  Check ((Shown) -eq 'Vault\Charms\Small') "a row of the tree: its folder is shown ('$(Shown)')"
}

Case 'vault-ways.path-step-goes-to-it' -Tags vault, vault-ways {
  OpenVault
  VaultGoTo 'Small'
  ClickEl (Crumb 'Charms') "the path's Charms"
  Check (Says 'Vault\Charms' "Charms shown") "a step of the path: gone to ('$(Shown)')"
}

Case 'vault-ways.folder-tile-double-clicked-goes-into-its-folder' -Tags vault, vault-ways, folder-tiles {
  OpenVault
  VaultGoTo 'Charms'
  Check ([bool](WaitUntil { if ((VaultFolderTile 'Grand') -and (VaultFolderTile 'Small')) { 'both' } } "the folders' tiles" 3000)) "the sheet has the folder's folders: Grand and Small"
  DoubleClickEl (VaultFolderTile 'Grand') "the tile Grand"
  Check (Says 'Vault\Charms\Grand' "Grand shown") "a folder's tile double clicked: gone into ('$(Shown)')"
  Check ($null -eq (ById 'SheetFolder')) "a folder holding no folders has no tiles"
}

Case 'vault-ways.back-forward-up-buttons-reach-it' -Tags vault, vault-ways {
  OpenVault
  VaultGoTo 'Small'
  ClickEl (Crumb 'Charms') "the path's Charms"
  Says 'Vault\Charms' "Charms" | Out-Null
  Check ((Can 'Vault_Back') -and (Can 'Vault_Up') -and -not (Can 'Vault_Forward')) "in a folder gone to: Back and Up are there, Forward is not"
  ClickEl (ById 'Vault_Back') "back"
  Check (Says 'Vault\Charms\Small' "back in Small") "Back: where the page was ('$(Shown)')"
  ClickEl (ById 'Vault_Forward') "forward"
  Check (Says 'Vault\Charms' "forward in Charms") "Forward: where Back came from ('$(Shown)')"
  ClickEl (ById 'Vault_Up') "up"
  Check (Says 'Vault' "Up") "Up: the folder above ('$(Shown)')"
  Check (-not (Can 'Vault_Up')) "at the top Up is dead"
}

Case 'vault-ways.alt-arrows-reach-the-page' -Tags vault, vault-ways, keys {
  OpenVault
  VaultGoTo 'Small'
  ClickEl (Crumb 'Charms') "the path's Charms"
  Says 'Vault\Charms' "Charms" | Out-Null
  ClickEl (ById 'Vault_Pages') "the sheet (the keys are its)"
  Keys '%{LEFT}'
  Check (Says 'Vault\Charms\Small' "Alt+Left") "Alt+Left: back ('$(Shown)')"
  Keys '%{RIGHT}'
  Check (Says 'Vault\Charms' "Alt+Right") "Alt+Right: forward ('$(Shown)')"
  Keys '%{UP}'
  Check (Says 'Vault' "Alt+Up") "Alt+Up: up one folder ('$(Shown)')"
}

Case 'vault-ways.chevron-after-a-step-lists-its-folders' -Tags vault, vault-ways {
  OpenVault
  VaultGoTo 'Jewels'
  ClickUntil { ClickEl (CrumbList 'Vault') "the chevron after Vault" } { VaultRow 'Charms' -Listed } "the folders of the vault's top" 800 | Out-Null
  Check ($null -ne (VaultRow 'Unique Weapons' -Listed) -and $null -eq (VaultRow 'Magic' -Listed)) "the chevron after a step lists that step's folders - not theirs"
  if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-ways-list.png') }
  ClickEl (VaultRow 'Charms' -Listed) "the list's Charms"
  Check (Says 'Vault\Charms' "Charms from the list") "a folder of the list: gone to, and the list shut ('$(Shown)')"
  Check ($null -eq (VaultRow 'Unique Weapons' -Listed)) "the list is shut"
}

Case 'vault-ways.tree-away-the-path-end-lists-all' -Tags vault, vault-ways {
  OpenVault
  VaultGoTo 'Jewels'
  ClickEl (ById 'Vault_TreeToggle') "folder tree off"
  Check ([bool](WaitUntil { if (-not (ById 'Vault_Tree')) { 'gone' } } "the tree gone" 3000)) "Folder tree: the tree is put away"
  VaultList 'Magic'
  ClickEl (VaultRow 'Magic' -Listed) "the list's Magic"
  Check (Says 'Vault\Jewels\Magic' "Magic from the list") "with the tree away, the list at the path's end goes anywhere ('$(Shown)')"
  ClickEl (ById 'Vault_TreeToggle') "folder tree on"
  Check ([bool](WaitUntil { ById 'Vault_Tree' } "the tree back" 3000)) "Folder tree again: the tree is back"
}

Case 'vault-ways.map-button-puts-it-away' -Tags vault, vault-ways {
  OpenVault
  VaultGoTo 'Jewels'
  Check ([bool](WaitUntil { VaultFolderTile 'Magic' } "the tile Magic" 3000)) "Jewels holds Magic: its tile is on the sheet"

  ClickEl (ById 'Vault_Minimap') "minimap off"
  Check ([bool](WaitUntil { if (-not (ById 'Vault_Map')) { 'gone' } } "the map gone" 3000)) "Minimap: the map is put away"
  ClickEl (ById 'Vault_Minimap') "minimap on"
  Check ([bool](WaitUntil { ById 'Vault_Map' } "the map back" 3000)) "Minimap again: the map is back"
}

Case 'vault-ways.drag-onto-a-tree-row-moves-there' -Tags vault, vault-ways, drag -Changes {
  OpenVault
  VaultGoTo 'Small'
  WaitUntil { Tile 'Small Charm of Fortune' } "the charms" | Out-Null
  Check ((Count) -eq '2 items' -and (Beside 'Grand') -eq '1') "Small holds two, Grand one ('$(Count)', Grand $(Beside 'Grand'))"
  DragTo (Tile 'Small Charm of Fortune') (VaultRow 'Grand')
  Check ([bool](WaitUntil { if ((Count) -eq '1 item' -and (Beside 'Grand') -eq '2') { 'moved' } } "the charm in Grand" 4000)) "a drag let go of on a row of the tree: moved there ('$(Count)', Grand $(Beside 'Grand'))"
  Check ((Shown) -eq 'Vault\Charms\Small') "and the page stays where it was ('$(Shown)')"
}

Case 'vault-ways.drag-onto-a-folder-tile-moves-there' -Tags vault, vault-ways, folder-tiles, drag -Changes {
  OpenVault
  WaitUntil { Tile 'Tome of Town Portal' } "the tome at the top" | Out-Null
  Check ((Count) -eq '1 item' -and (Beside 'Jewels') -eq '1') "the top holds one, Jewels one ('$(Count)', Jewels $(Beside 'Jewels'))"
  DragTo (Tile 'Tome of Town Portal') (VaultFolderTile 'Jewels')
  Check ([bool](WaitUntil { if ((Count) -eq '0 items' -and (Beside 'Jewels') -eq '2') { 'moved' } } "the tome in Jewels" 4000)) "a drag let go of on a folder's tile: moved there ('$(Count)', Jewels $(Beside 'Jewels'))"
}

# The tree stays as wide as it was dragged while the program runs: a start to itself after this one.
Case 'vault-ways.tree-edge-drags-to-half' -Tags vault, vault-ways, drag, layout -Changes {
  OpenVault
  # The pane and its edge by their own ids: the rows' width is the pane's less the column of counts and locks.
  $tree = (ById 'Vault_TreePane').Current.BoundingRectangle
  $edge = (ById 'Vault_TreeEdge').Current.BoundingRectangle
  $edgeX = [int]($edge.X + $edge.Width / 2 - $script:r.X); $edgeY = [int]($tree.Y + $tree.Height / 2 - $script:r.Y)
  DragTo @($edgeX, $edgeY) @(($edgeX + 60), $edgeY)
  $wider = WaitUntil { $now = (ById 'Vault_TreePane').Current.BoundingRectangle; if ([math]::Abs($now.Width - $tree.Width - 60) -le 8) { $now } } "the tree wider" 3000
  Check ([bool]$wider) "the tree's edge dragged sixty along: the tree is that much wider ($([int]$tree.Width) -> $([int](ById 'Vault_TreePane').Current.BoundingRectangle.Width))"

  # And no wider than half the page, however far its edge is dragged.
  $tree = (ById 'Vault_TreePane').Current.BoundingRectangle
  $edge = (ById 'Vault_TreeEdge').Current.BoundingRectangle
  $edgeX = [int]($edge.X + $edge.Width / 2 - $script:r.X)
  DragTo @($edgeX, $edgeY) @(($edgeX + 700), $edgeY)
  Start-Sleep -Milliseconds 400
  $most = (ById 'Vault_TreePane').Current.BoundingRectangle
  Check ($most.Width -gt $tree.Width -and $most.Width -le $script:r.Width / 4) "dragged far across: the tree stops at half the page ($([int]$most.Width) of a page some $([int]($script:r.Width / 2)) wide)"
  Check ($null -ne (ById 'Vault_Pages') -and (ById 'Vault_Pages').Current.BoundingRectangle.Width -gt $most.Width * 0.6) "and the sheet keeps its room"
}
