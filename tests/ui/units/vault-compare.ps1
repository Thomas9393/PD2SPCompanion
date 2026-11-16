# The vault on both pages of the book, as only a window shows it: which half of the window each page, its Compare button and its strip of messages
# are on; that a drag crosses from one sheet to the other and onto the other's tree, the item drawn under the
# pointer on the way; and that the keys are the page's that was pressed in last.
#
# What the two pages decide - each its own folder, ways and count, the right page opening where the left is,
# which folder a card let go of comes into, what Compare and the right bookmark do to the book - is proven
# without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultCompareTests.
#
# On the pad's vault: Charms holding Grand (one charm) and Small (two); Jewels (one item) holding Magic (one).
Add-Type -AssemblyName System.Windows.Forms

function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }

# Which page of the book something is on, and the vault page's parts by the page: with the vault on both, every
# one of them is there twice.
function Half($el) { PageOf $el }
function On([string]$side, [string]$id, $name = $null) {
  Steady {
    foreach ($e in (All (Cond $AE::AutomationIdProperty $id))) {
      if (-not $e.Current.IsOffscreen -and (Half $e) -eq $side -and (-not $name -or $e.Current.Name -eq $name)) { return $e }
    }
  }
}
function Sheets { @((All (Cond $AE::AutomationIdProperty 'Vault_Pages')) | Where-Object { -not $_.Current.IsOffscreen }).Count }
function Shown([string]$side) { $b = On $side 'Vault_Folder'; if ($b) { $b.Current.Name } }
function Count([string]$side) { $t = On $side 'Vault_Count'; if ($t) { $t.Current.Name } }
function Row([string]$side, $name) { On $side 'FolderRow' $name }
function TileOn([string]$side, $name) { @(Tiles $name | Where-Object { (Half $_) -eq $side })[0] }
function Says([string]$side, $path, $what) { [bool](WaitUntil { if ((Shown $side) -eq $path) { 'there' } } $what 3000) }
# The number beside a folder's name on its row of a page's tree ('' for none).
function Beside([string]$side, $name) { VaultRowCount (Row $side $name) }
# A page's bin, and which page's strip says something.
function Bin([string]$side) { foreach ($b in (All (Cond $AE::HelpTextProperty 'Delete selected item (Del)'))) { if (-not $b.Current.IsOffscreen -and (Half $b) -eq $side) { return $b } } }
function Strip([string]$like) { foreach ($t in (All (Cond $AE::ControlTypeProperty $CT::Text))) { if (-not $t.Current.IsOffscreen -and $t.Current.Name -like $like) { return (Half $t) } } }

# The vault on the left page alone, in a folder of its tree, the grail on the right: the left page is put where
# a case wants it before the vault is on both pages - after that there are two trees to tell apart.
function OpenVault([string]$folder = 'Vault') {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  ClickEl (ById 'Bookmark_Grail') "grail (right)"
  WaitUntil { -not (ById 'Item_Name') } "the Edit page gone" | Out-Null
  VaultGoTo 'Vault'
  if ($folder -ne 'Vault') { VaultGoTo $folder }
}
# The vault on the right page too, by the left page's button, and the right page in a folder of its own tree.
function Both([string]$folder = '', [string]$path = '') {
  ClickEl (On 'Left' 'Vault_Compare') "compare"
  WaitUntil { if ((Sheets) -eq 2) { 'both' } } "the vault on both pages" 5000 | Out-Null
  # Both trees unfolded: each opens on the vault's own folders, and the cases click the folders in those.
  VaultUnfold | Out-Null
  if ($folder) {
    ClickEl (WaitUntil { Row 'Right' $folder } "the right tree's $folder") "$folder (right)"
    Says 'Right' $path "$folder on the right" | Out-Null
  }
}

Case 'vault-compare.compare-button-and-right-bookmark' -Tags vault, vault-compare {
  OpenVault 'Small'

  # One vault page, the left: it has the Compare button, and the right Vault bookmark is there to press.
  Check ((Sheets) -eq 1 -and $null -ne (On 'Left' 'Vault_Compare')) "the vault on the left page, with its Compare button"
  Check ((ById 'Bookmark_Vault_Right').Current.IsEnabled) "the right Vault bookmark is there while the left page shows the vault"

  ClickEl (On 'Left' 'Vault_Compare') "compare"
  Check ([bool](WaitUntil { if ((Sheets) -eq 2) { 'both' } } "the vault on both pages" 5000)) "Compare: the vault is on both pages"
  Check ($null -eq (On 'Right' 'Vault_Compare')) "the right page has no Compare button"
  VaultUnfold | Out-Null
  if ($env:PD2SPC_SHOT_DIR) { Start-Sleep -Milliseconds 500; Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-compare.png') }
  ClickEl (WaitUntil { Row 'Right' 'Grand' } "the right tree's Grand") "Grand (right)"
  Says 'Right' 'Vault\Charms\Grand' "Grand on the right" | Out-Null

  # Compare off: the right page shows what it did before; on again by the right page's bookmark.
  ClickEl (On 'Left' 'Vault_Compare') "compare off"
  Check ([bool](WaitUntil { if ((Sheets) -eq 1) { 'one' } } "one vault page" 4000)) "Compare again: the vault is on the left page only"
  Check ([bool](WaitUntil { ByName 'Uniques' } "the grail back" 3000)) "and the right page shows what it did before - the grail"
  ClickEl (ById 'Bookmark_Vault_Right') "vault (right)"
  Check ([bool](WaitUntil { if ((Sheets) -eq 2) { 'both' } } "the vault on both pages again" 5000)) "the right Vault bookmark: the vault on both pages, as Compare puts it"
  # Waited for: the page says its folder a moment after its sheet is counted (it read 'Vault\Charms\Grand' in
  # the failure's own message, the check having looked an instant earlier - one run in five).
  try { WaitUntil { if ((Shown 'Right') -eq 'Vault\Charms\Grand') { 'there' } } "the right page's folder" 3000 | Out-Null } catch { }
  Check ((Shown 'Right') -eq 'Vault\Charms\Grand') "the right page is where it was left ('$(Shown 'Right')')"
}

Case 'vault-compare.drag-from-one-sheet-to-the-other' -Tags vault, vault-compare, drag -Changes {
  OpenVault 'Small'
  Both 'Grand' 'Vault\Charms\Grand'
  Check ((Count 'Left') -eq '2 items' -and (Count 'Right') -eq '1 item') "each counts its own folder ('$(Count 'Left')', '$(Count 'Right')')"

  # An item dragged from the left page onto the right page's sheet comes into the right page's folder.
  $charm = 'Small Charm of Fortune'
  $sheet = (On 'Right' 'Vault_Pages').Current.BoundingRectangle; $cell = CellPx (TileOn 'Left' $charm)
  $toX = [int]($sheet.X + 6.5 * $cell - $script:r.X); $toY = [int]($sheet.Y + 4.5 * $cell - $script:r.Y)
  DragTo (TileOn 'Left' $charm) @($toX, $toY)
  Check ([bool](WaitUntil { if ((Count 'Left') -eq '1 item' -and (Count 'Right') -eq '2 items') { 'moved' } } "the charm across" 4000)) "dragged from the left sheet to the right: it is in the right page's folder ('$(Count 'Left')', '$(Count 'Right')')"
  Check ([bool](TileOn 'Right' $charm)) "and is drawn on the right page's sheet"
  Check ((Beside 'Left' 'Grand') -eq '2' -and (Beside 'Right' 'Small') -eq '1') "both trees count it so (Grand $(Beside 'Left' 'Grand'), Small $(Beside 'Right' 'Small'))"
}

Case 'vault-compare.carried-item-drawn-under-the-pointer' -Tags vault, vault-compare, drag -Changes {
  OpenVault 'Small'
  Both 'Small' 'Vault\Charms\Small'
  $charm = 'Small Charm of Fortune'
  WaitUntil { TileOn 'Right' $charm } "the charm on the right page" | Out-Null
  Check ((Count 'Right') -eq '2 items' -and (Beside 'Left' 'Jewels') -eq '1') "the right page's folder holds two, Jewels one ('$(Count 'Right')', Jewels $(Beside 'Left' 'Jewels'))"

  # From the right page onto a row of the left page's tree: into that folder. On the way the item is drawn
  # under the pointer - a drag from the right page was drawn at the window's corner.
  $from = (TileOn 'Right' $charm).Current.BoundingRectangle; $onto = (Row 'Left' 'Jewels').Current.BoundingRectangle
  $ax = [int]($from.X + $from.Width / 2); $ay = [int]($from.Y + $from.Height / 2)
  $bx = [int]($onto.X + $onto.Width / 2); $by = [int]($onto.Y + $onto.Height / 2)
  Front; [Fast]::SetCursorPos($ax, $ay) | Out-Null; [Fast]::mouse_event(2, 0, 0, 0, 0)
  for ($i = 1; $i -le 12; $i++) { [Fast]::SetCursorPos([int]($ax + ($bx - $ax) * $i / 12), [int]($ay + ($by - $ay) * $i / 12)) | Out-Null; Start-Sleep -Milliseconds 15 }
  $drawn = $null
  # The list that draws it has no rectangle of its own; the picture in it has.
  try {
    $drawn = WaitUntil {
      foreach ($g in (All (Cond $AE::AutomationIdProperty 'Vault_Ghost'))) {
        foreach ($picture in $g.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Image))) {
          $at = $picture.Current.BoundingRectangle
          if (-not $at.IsEmpty -and [math]::Abs($at.X - $bx) -lt 90 -and [math]::Abs($at.Y - $by) -lt 90) { return $at }
        }
      }
    } "the carried item under the pointer" 2000
  } catch { }
  [Fast]::mouse_event(4, 0, 0, 0, 0)
  Check ([bool]$drawn) "carried from the right page, the item is drawn under the pointer"
  Check ([bool](WaitUntil { if ((Count 'Right') -eq '1 item' -and (Beside 'Left' 'Jewels') -eq '2') { 'moved' } } "the charm in Jewels" 4000)) "dragged from the right sheet onto a row of the left tree: moved into that folder ('$(Count 'Right')', Jewels $(Beside 'Left' 'Jewels'))"
}

Case 'vault-compare.keys-are-the-page-pressed-in-last' -Tags vault, vault-compare, keys -Changes -Typing {
  OpenVault 'Small'
  Both 'Jewels' 'Vault\Jewels'
  Check ((Count 'Right') -eq '1 item') "the right page's folder holds one ('$(Count 'Right')')"

  # The keys are the page's that was pressed in last: the arrows on the right page's sheet select there - nothing
  # being selected, the sheet's first, which is the folder's one folder as a tile, and right of it
  # the folder's first item, which begins beside the tiles' last row - and not
  # on the left.
  $room = (On 'Right' 'Vault_Pages').Current.BoundingRectangle
  ClickAt ([int]($room.X + $room.Width / 2 - $script:r.X)) ([int]($room.Y + $room.Height - 60 - $script:r.Y))
  Keys '{RIGHT}'
  Keys '{RIGHT}'
  Check ([bool](WaitUntil { $b = Bin 'Right'; if ($b -and $b.Current.IsEnabled) { 'selected' } } "an item selected on the right" 3000)) "an arrow key on the right page's sheet: an item is selected there"
  Check (-not (Bin 'Left').Current.IsEnabled) "and none on the left: the keys were the right page's"

  # What the right page does is said under the right page: a copy of the selected item (Ctrl + C, Ctrl + V).
  Keys '^c'
  Keys '^v'
  Check ([bool](WaitUntil { if ((Count 'Right') -eq '2 items') { 'copied' } } "the copy" 4000)) "Ctrl + C, Ctrl + V on the right page: a copy, in its folder ('$(Count 'Right')')"
  Check ((Strip 'Copied *') -eq 'Right') "and it is said on the right strip, under the page it was done on ('$(Strip 'Copied *')')"
  Keys '{ESC}'
}
