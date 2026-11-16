# The vault's folder tree, as only a window shows it: that it opens on the vault's own folders, each folded, and a chevron brings a
# folder's folders in; that a row says under its name how many items its folder holds, with a small lock before
# that while it is locked; that going to a folder inside a folded one leaves the tree folded and lights the folded
# one; quick access - the tools' star pinning the folder shown to a list over the tree, which stays where it is
# while the tree scrolls, a row of it going to its folder; the arrows moving a mark from row to row without going
# anywhere, Enter going to the row marked, Right and Left unfolding and folding; and a double click on a row
# folding its folder or unfolding it.
#
# What the page decides - which rows are in the tree, which is lit, what a row's line says, what is pinned - is
# proven without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultTreeFoldsTests and VaultQuickAccessTests.
#
# On the pad's vault: Charms holding Grand (one charm) and Small (two); Jewels (one item) holding Magic (one).
Add-Type -AssemblyName System.Windows.Forms

function Key([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 350 }
function Focused { try { ([System.Windows.Automation.AutomationElement]::FocusedElement).Current.Name } catch { '?' } }
# A row of the tree as it stands - folded folders left folded.
function InTree($name) { [bool](VaultRow $name -AsIs) }
# A row of quick access: a folder's row that is not the tree's, nor a list's - over the tree.
function Quick($name) {
  $tree = (ById 'Vault_Tree').Current.BoundingRectangle
  @((All (Cond $AE::AutomationIdProperty 'FolderRow')) | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen -and -not (InVaultTree $_) -and $_.Current.BoundingRectangle.Y -lt $tree.Y })[0]
}
function Locked($row) {
  $r = $row.Current.BoundingRectangle
  [bool](@((All (Cond $AE::AutomationIdProperty 'FolderRow_Locked')) | Where-Object { $b = $_.Current.BoundingRectangle; -not $_.Current.IsOffscreen -and $b.Y -ge $r.Y -and $b.Y + $b.Height -le $r.Y + $r.Height + 1 -and $b.X -ge $r.X }).Count)
}
# The tree folded, every folder of it, whatever a case before unfolded: a fold is kept while the program runs.
function FoldTree {
  for ($pass = 0; $pass -lt 6; $pass++) {
    $open = @((All (Cond $AE::AutomationIdProperty 'FoldPlace')) | Where-Object { $_.Current.HelpText -eq 'Fold' -and (InVaultTree $_) })
    if ($open.Count -eq 0) { break }
    foreach ($chevron in $open) { try { InvokeEl $chevron "fold $($chevron.Current.Name)" } catch { } }
    Start-Sleep -Milliseconds 200
  }
}
# The vault on the left page, at its top, the grail on the right.
function OpenVault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  ClickEl (ById 'Bookmark_Grail') "grail (right)"
  WaitUntil { -not (ById 'Item_Name') } "the Edit page gone" | Out-Null
  VaultGoTo 'Vault'
}

# A start of the program to itself (the empty Before): the tree as it opens, no case before having unfolded any of it.
Case 'vault-tree.opens-folded-and-a-row-says-what-it-holds' -Tags vault, vault-tree, layout -Before { } {
  OpenVault
  Check ((InTree 'Charms') -and (InTree 'Jewels') -and -not (InTree 'Small') -and -not (InTree 'Magic')) "the tree opens on the vault's own folders, each folded"
  Check ((VaultRowLine (VaultRow 'Charms' -AsIs)) -eq '2 folders') "a folder of folders says how many ('$(VaultRowLine (VaultRow 'Charms' -AsIs))')"

  # Into a folder inside a folded one, by its tile on the sheet: the tree stays folded, the folded one lit.
  VaultGoTo 'Charms'
  DoubleClickEl (WaitUntil { VaultFolderTile 'Small' } "the tile Small") "the tile Small"
  WaitUntil { if ((VaultShown) -eq 'Vault\Charms\Small') { 'there' } } "Small shown" 3000 | Out-Null
  Check (-not (InTree 'Small')) "gone into by its tile: the tree is not unfolded to it"

  # Unfolded by its chevron: the rows under it, each with its line.
  InvokeEl (VaultRow 'Charms' -AsIs -Id 'FoldPlace') "the chevron of Charms"
  Check ([bool](WaitUntil { if (InTree 'Small') { 'in' } } "Small in the tree" 3000)) "its chevron brings its folders into the tree"
  Check ((VaultRowLine (VaultRow 'Small' -AsIs)) -eq '2 items' -and (VaultRowLine (VaultRow 'Grand' -AsIs)) -eq '1 item') "each says how many items it holds ('$(VaultRowLine (VaultRow 'Small' -AsIs))', '$(VaultRowLine (VaultRow 'Grand' -AsIs))')"
  $name = (VaultRow 'Small' -AsIs).Current.BoundingRectangle
  $line = @((All (Cond $AE::AutomationIdProperty 'FolderRow_Count')) | Where-Object { $_.Current.Name -eq '2 items' -and -not $_.Current.IsOffscreen })[0].Current.BoundingRectangle
  Check ($line.Y -gt $name.Y + $name.Height / 2 - 2 -and $line.Y + $line.Height -le $name.Y + $name.Height + 1) "on a small line of its own, in the row's lower half"
  if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-tree.png') }
}

Case 'vault-tree.locked-folder-wears-a-lock-before-its-count' -Tags vault, vault-tree, vault-folder-lock -Changes {
  OpenVault
  VaultGoTo 'Small'
  $row = VaultRow 'Small'
  Check (-not (Locked $row)) "not locked: no lock on its row"
  ClickEl (ById 'Vault_FolderLock') "the tools' lock"
  Check ([bool](WaitUntil { if (Locked (VaultRow 'Small')) { 'locked' } } "the lock on the row" 3000)) "locked by the tools' button: a small lock stands on its row"
  $r = (VaultRow 'Small').Current.BoundingRectangle
  $lock = @((All (Cond $AE::AutomationIdProperty 'FolderRow_Locked')) | Where-Object { -not $_.Current.IsOffscreen })[0].Current.BoundingRectangle
  $count = @((All (Cond $AE::AutomationIdProperty 'FolderRow_Count')) | Where-Object { $_.Current.Name -eq '2 items' -and -not $_.Current.IsOffscreen })[0].Current.BoundingRectangle
  Check ($lock.X + $lock.Width -le $count.X + 1 -and [math]::Abs(($lock.Y + $lock.Height / 2) - ($count.Y + $count.Height / 2)) -le 4) "at the head of the line that says its count"
  ClickEl (ById 'Vault_FolderLock') "the tools' lock, off"
  Check ([bool](WaitUntil { if (-not (Locked (VaultRow 'Small'))) { 'off' } } "the lock gone" 3000)) "let go: the lock is gone from the row"
}

Case 'vault-tree.star-pins-the-folder-shown-to-quick-access' -Tags vault, vault-tree -Changes {
  OpenVault
  Check (-not (ById 'Vault_Quick').Current.IsEnabled) "at the vault's top the star is dead: the top is the tree's first row"
  Check ((ById 'Vault_Quick').Current.HelpText -like 'Pin to quick access*Top level cannot be pinned.') "and says why ('$(((ById 'Vault_Quick').Current.HelpText -split "`n") -join ' / ')')"
  $move = ById 'Vault_MoveItems'
  Check (-not $move.Current.IsEnabled -and $move.Current.HelpText -like 'Move to folder*Nothing selected.') "nothing selected: Move to folder is dimmed and says so ('$(($move.Current.HelpText -split "`n") -join ' / ')')"
  VaultGoTo 'Small'
  Check ((ById 'Vault_Quick').Current.HelpText -eq 'Pin to quick access') "in a folder the star says what it does ('$((ById 'Vault_Quick').Current.HelpText)')"
  ClickEl (ById 'Vault_Quick') "the star"
  Check ([bool](WaitUntil { Quick 'Small' } "Small in quick access" 3000)) "pressed: the folder shown is listed over the tree"
  Check ([bool](ByName 'Quick access') -and (ById 'Vault_Quick').Current.HelpText -eq 'Unpin from quick access') "under its heading; the star says it takes it off"
  Check ((VaultRowLine (Quick 'Small')) -eq '2 items') "its row as the tree's: its count under its name"

  # From anywhere, its row goes to it.
  VaultGoTo 'Jewels'
  ClickEl (Quick 'Small') "quick access's Small"
  Check ([bool](WaitUntil { if ((VaultShown) -eq 'Vault\Charms\Small') { 'there' } } "Small shown" 3000)) "its row goes to its folder ('$(VaultShown)')"

  ClickEl (ById 'Vault_Quick') "the star, again"
  Check ([bool](WaitUntil { if (-not (Quick 'Small')) { 'gone' } } "Small off quick access" 3000)) "pressed again: taken off"
  Check (-not (ByName 'Quick access')) "and with nothing pinned the section is not there"
}

Case 'vault-tree.arrows-move-a-mark-enter-goes-right-and-left-fold' -Tags vault, vault-tree, keys {
  OpenVault
  FoldTree
  VaultGoTo 'Charms'
  Key '{DOWN}'
  $under = Focused
  Check ($under -ne 'Charms' -and (VaultShown) -eq 'Vault\Charms') "Down from the row clicked: the row under it is marked, and nothing gone to ('$under', '$(VaultShown)')"
  Key '{UP}'
  Check ((Focused) -eq 'Charms') "Up: back on Charms ('$(Focused)')"
  Key '{RIGHT}'
  Check ((InTree 'Small') -and (Focused) -eq 'Charms') "Right on a folded folder: it unfolds, the mark where it was"
  Key '{RIGHT}'
  Check ((Focused) -eq 'Grand') "Right again: the first folder in it ('$(Focused)')"
  Key '{DOWN}'
  Check ((Focused) -eq 'Small' -and (VaultShown) -eq 'Vault\Charms') "Down: Small marked, Charms still shown"
  Key '{ENTER}'
  Check ([bool](WaitUntil { if ((VaultShown) -eq 'Vault\Charms\Small') { 'there' } } "Small shown" 3000)) "Enter goes to the folder of the row marked ('$(VaultShown)')"
  Key '{LEFT}'
  Check ((Focused) -eq 'Charms') "Left on a folder that holds none: the folder it is in ('$(Focused)')"
  Key '{LEFT}'
  Check (-not (InTree 'Small') -and (VaultShown) -eq 'Vault\Charms\Small') "Left on an unfolded folder folds it; the folder shown is not left"
}

Case 'vault-tree.double-click-folds-and-unfolds' -Tags vault, vault-tree {
  OpenVault
  FoldTree
  $b = (VaultRow 'Charms' -AsIs).Current.BoundingRectangle
  $x = [int]($b.X + $b.Width / 2); $y = [int]($b.Y + $b.Height / 2)
  Front; [Fast]::Click($x, $y); Start-Sleep -Milliseconds 120; [Fast]::Click($x, $y); Start-Sleep -Milliseconds 600
  Check ((InTree 'Small') -and (VaultShown) -eq 'Vault\Charms') "a double click on a row: gone to, and unfolded ('$(VaultShown)')"
  Front; [Fast]::Click($x, $y); Start-Sleep -Milliseconds 120; [Fast]::Click($x, $y); Start-Sleep -Milliseconds 600
  Check (-not (InTree 'Small')) "and again: folded"
  Start-Sleep -Milliseconds 700
  Front; [Fast]::Click($x, $y); Start-Sleep -Milliseconds 900
  Check (-not (InTree 'Small')) "one click after a pause folds nothing"
}
