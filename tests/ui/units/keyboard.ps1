# The keyboard, as only a window shows it: where
# a key goes. A question owns the keyboard - Esc answers no, and the page behind hears nothing; Esc closes the
# picker on top, as a click beside it does. A picker opening takes the focus - its search box, else a row - Up and
# Down walk its rows, Enter in the search box steps into them and on a row picks it. Tab walks only the window's
# three and the side bar, or a section's boxes, or round an open picker; menus and the Forge's and Search page's finds
# answer the arrows (ListKeys); Ctrl and a digit, Ctrl+Tab and Ctrl+PageUp/PageDown press the side bar and the
# bookmarks; the arrows move a selection; F2 names a vault folder and Backspace is back; Ctrl+F is the search
# box; Ctrl+V duplicates, Ctrl+Z undoes.
#
# What those keys' commands then do - a question answered, the selection let go, the copy made and taken back, a
# number box's steps and its range, a number written by itself, the folder renamed and the way back - is proven
# without a window: tests/PD2SPCompanion.Shell.Tests/Keys/KeyEffectsTests.
#
# The cases that type words through the keyboard wear -Typing.

Add-Type -AssemblyName System.Windows.Forms

function Key([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }
function Focused { try { $f = [System.Windows.Automation.AutomationElement]::FocusedElement; $null = $f.Current.ControlType; $f } catch { $null } }
function Header { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { $_.Current.Name -like 'Properties*(*' } | ForEach-Object { $_.Current.Name })[0] }
# Text typed into a box, and what the box then holds looked at: a key that did not arrive is said, with where the
# keys stood (Standing), and the text typed again - a space was lost from "absorbs cold" once.
function TypeInto($box, [string]$text) {
  Key $text
  $got = try { $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { $text }
  if ($got -ne $text) { "  note  typed '$text', the box held '$got' [$(Standing)]; typed again"; Key "^a$text" }
}
# Not the window's own search box, which says "Search" too and is always there.
# A box: the Search bookmark's tooltip is the same word, and it stands on the Items page.
function PickerSearch { @((All (Cond $AE::HelpTextProperty 'Search')) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.ControlType -eq $CT::Edit -and $_.Current.AutomationId -ne 'Quick_Box' })[0] }
# The right page shows the Grail: its count at the title's right.
function Found { ById 'Grail_Count' }
function LeftHas([string]$tip) {
  $mid = $script:win.Current.BoundingRectangle.X + $script:win.Current.BoundingRectangle.Width / 2
  @((All (Cond $AE::HelpTextProperty $tip)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -lt $mid })[0]
}
# An item chosen by a click on its tile: the Edit page shows it.
function Choose([string]$name) {
  ClickUntil { ClickEl (WaitUntil { Tile $name } $name) $name } { OnEdit $name } "$name on the Edit page" | Out-Null
}
# The Edit page's item level box, by what it holds: the amulet's is 81.
function LevelBox {
  WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen -and $_.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '81' })[0] } "the item level box"
}

Case 'keyboard.tab-walks-the-windows-three-and-the-side-bar' -Tags keys -Start Home {
  # Tab walks the window's three buttons and the side bar, and nothing else.
  Key '{TAB}'
  Check ((Focused).Current.AutomationId -eq 'MinimizeButton') "Tab: the window's minimize first ('$((Focused).Current.AutomationId)')"
  Key '{TAB}{TAB}{TAB}'
  Check ((Focused).Current.AutomationId -eq 'Logo') "Tab past close: the side bar's first, the logo ('$((Focused).Current.AutomationId)')"
  Key '+{TAB}'
  Check ((Focused).Current.AutomationId -eq 'CloseButton') "Shift+Tab: back to close ('$((Focused).Current.AutomationId)')"
  Key '{TAB}'
}

Case 'keyboard.esc-answers-a-question-no' -Tags keys -Start Home {
  # The question: the card's Delete asks; Esc is CANCEL, and the character stays.
  ClickEl (WaitUntil { $d = ByTip 'Delete (Del)'; if ($d -and -not $d.Current.IsOffscreen) { $d } } "the card's delete") "Delete"
  WaitUntil { ByName 'DELETE' } "the question" | Out-Null
  Key '{ESC}'
  Check ($null -eq (ByName 'DELETE') -and (Test-Path "$pad\saves\PhySpearZon.d2s")) "Esc answers no: the question gone, the character kept"
}

Case 'keyboard.esc-closes-the-picker-on-top' -Tags keys, items {
  Choose 'Stone Barb'

  # The affix picker: Esc closes it.
  InvokeEl (WaitUntil { @((All (Cond $AE::NameProperty 'Viridian')) | Where-Object { $_.Current.AutomationId -ne 'Tile' } | Sort-Object { $_.Current.BoundingRectangle.Y })[-1] } "the affix line") "affixes"
  WaitUntil { ByName 'Prefixes' } "the affix picker" | Out-Null
  Key '{ESC}'
  Check ([bool](WaitUntil { if (-not (ByName 'Prefixes')) { 'closed' } } "the affix picker closed" 3000)) "Esc closes the affix picker"

  # The property picker, its search box focused or not: Esc closes it.
  InvokeEl (WaitUntil { ByTip 'Add property' } "Add property") "add property"
  WaitUntil { PickerSearch } "the property picker" | Out-Null
  Key '{ESC}'
  Check ([bool](WaitUntil { if (-not (PickerSearch)) { 'closed' } } "the property picker closed" 3000)) "Esc closes the property picker"
  Check ([bool](OnEdit 'Stone Barb')) "and the one Esc did not reach the page behind: Stone Barb is still chosen"
}

Case 'keyboard.property-picker-by-the-keys-alone' -Tags keys, items -Changes -Typing {
  Choose 'Stone Barb'

  # The property picker by the keys alone: open, its search box has the focus; typed, Down into the rows, Enter picks.
  $before = Header
  InvokeEl (WaitUntil { ByTip 'Add property' } "Add property") "add property"
  $search = WaitUntil { PickerSearch } "the property picker"
  Start-Sleep -Milliseconds 300
  Check ((Focused).Current.HelpText -eq 'Search' -and (Focused).Current.AutomationId -ne 'Quick_Box') "the picker opens with its search box focused"
  TypeInto $search 'absorbs cold'
  Key '{DOWN}'
  Check ((Focused).Current.ControlType -eq $CT::Button) "Down steps from the search box into the rows"
  Key '{ENTER}'
  Check ([bool](WaitUntil { if ((Header) -ne $before) { 'added' } } "a property added" 3000)) "Enter on a row picks it: '$before' -> '$(Header)'"
}

Case 'keyboard.corruption-picker-by-the-keys' -Tags keys, items -Changes {
  # The corruption picker has no search box: it opens on a row, and Down then Enter picks one. On an item not
  # corrupted yet, so the pick is a plain one (Stone Barb is: a pick there cleans first).
  Choose 'Beast Necklace'
  $corrupt = WaitUntil { $b = ByTip 'Corrupt'; if ($b -and $b.Current.IsEnabled) { $b } } "Corrupt"
  InvokeEl $corrupt "corrupt"
  WaitUntil { ByName 'Low rarity' } "the corruption picker" | Out-Null
  Start-Sleep -Milliseconds 500
  $f = Focused
  Check ($f.Current.ControlType -eq $CT::Button -and $f.Current.BoundingRectangle -ne $corrupt.Current.BoundingRectangle) "the corruption picker opens with a row focused, not its opener"
  Key '{DOWN}'
  Key '{ENTER}'
  Key '{ESC}'
  Check ($null -eq (ByName 'Low rarity')) "Esc closes the corruption picker"
  $picked = $false
  try { UndoLast "the corruption"; $picked = $true } catch { }
  Check $picked "Down and Enter pick a corruption: written when the picker closes, and taken back by the undo"
  WaitUntil { $b = ByTip 'Corrupt'; if ($b -and $b.Current.IsEnabled) { $b } } "not corrupted again" | Out-Null
}

Case 'keyboard.esc-lets-the-selection-go' -Tags keys, items {
  # Esc with nothing open is the page's own: the selection let go (Explorer's).
  Choose 'Beast Necklace'
  Key '{ESC}'
  Check ([bool](WaitUntil { if (-not (OnEdit 'Beast Necklace')) { 'cleared' } } "the selection cleared" 3000)) "Esc with no picker open lets the selection go"
}

Case 'keyboard.ctrl-digits-go-to-the-side-bars-pages' -Tags keys {
  # A press on the page first, so the keys are not a box's.
  Choose 'Beast Necklace'
  Key '^2'
  Check ([bool](WaitUntil { ByName 'Strength' } "the Character page" 3000)) "Ctrl+2: the Character page"
  Key '^3'
  Check ([bool](WaitUntil { ItemsPage } "the Items page" 3000)) "Ctrl+3: the Items page"
}

# Tab among a section's boxes, and inside an open picker: it walked the window's buttons only.
Case 'keyboard.tab-walks-a-sections-boxes-and-stays-in-a-picker' -Tags keys, items {
  # The Character page's boxes, those in sight: the leftmost, its section's first, and Tab from it to the next.
  ClickEl (ById 'SideBar_Char') "Character"
  $bottom = $script:win.Current.BoundingRectangle.Bottom - 20
  $boxes = WaitUntil { $b = @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.AutomationId -ne 'Quick_Box' -and $_.Current.BoundingRectangle.Bottom -lt $bottom }); if ($b.Count -ge 2) { , $b } } "the Character page's boxes"
  $first = $boxes | Sort-Object { $_.Current.BoundingRectangle.X } | Select-Object -First 1
  ClickEl $first "the first box"
  $was = (Focused).Current.BoundingRectangle
  Key '{TAB}'
  $now = (Focused)
  Check ($now.Current.ControlType -eq $CT::Edit -and $now.Current.BoundingRectangle -ne $was) "Tab from a box: the next box of its section ('$($now.Current.ControlType.ProgrammaticName)')"

  # The affix picker: Tab goes round its own, not the side bar's.
  ClickEl (ById 'SideBar_Inventory') "Items"
  Choose 'Beast Necklace'
  InvokeEl (WaitUntil { @((All (Cond $AE::NameProperty "Wyrm's")) | Where-Object { $_.Current.AutomationId -ne 'Tile' })[-1] } "the affix line") "affixes"
  WaitUntil { ByName 'Prefixes' } "the affix picker" | Out-Null
  Start-Sleep -Milliseconds 300
  foreach ($n in 1..4) { Key '{TAB}' }
  $id = (Focused).Current.AutomationId
  Check ($id -notlike 'SideBar_*' -and $id -notin @('MinimizeButton', 'MaximizeButton', 'CloseButton')) "four Tabs in the picker stay in it ('$id')"
  Key '{ESC}'
}

# And from a number's box, which keeps the keyboard after a number is typed: they did nothing there.
Case 'keyboard.ctrl-digits-reach-from-a-number-box' -Tags keys, items {
  Choose 'Beast Necklace'
  ClickEl (LevelBox) "the item level box"
  Check ((Focused).Current.ControlType -eq $CT::Edit) "the caret in the item level box"
  Key '^2'
  Check ([bool](WaitUntil { ByName 'Strength' } "the Character page" 3000)) "Ctrl+2 from the box: the Character page"
}

Case 'keyboard.ctrl-tab-and-ctrl-page-keys-reach-from-a-number-box' -Tags keys, items {
  Choose 'Beast Necklace'
  ClickEl (LevelBox) "the item level box"
  Check ((Focused).Current.ControlType -eq $CT::Edit) "the caret in the item level box"
  Key '^+{TAB}'
  Check ([bool](WaitUntil { Found } "the Grail" 3000)) "Ctrl+Shift+Tab from the box: the right page round to the Grail"
  Key '^{TAB}'
  Check ([bool](WaitUntil { OnEdit 'Beast Necklace' } "Edit again" 3000)) "and Ctrl+Tab back to Edit"

  $level = LevelBox
  ClickEl $level "the item level box, again"
  $was = $level.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
  Key '^{PGDN}'
  Check ([bool](WaitUntil { ById 'Vault_Pages' } "the vault on the left page" 3000)) "Ctrl+PageDown from the box: the left page's Vault bookmark"
  Key '^{PGUP}'
  Check ([bool](WaitUntil { if (-not (ById 'Vault_Pages')) { 'inventory' } } "the inventory on the left page" 3000)) "and Ctrl+PageUp back to the inventory"
  Check ((LevelBox).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $was) "the level is as it was: the page keys with Ctrl are not the box's ($was)"
}

Case 'keyboard.ctrl-tab-goes-round-the-right-pages-bookmarks' -Tags keys, items {
  Choose 'Beast Necklace'
  Key '^+{TAB}'
  Check ([bool](WaitUntil { Found } "the Grail" 3000)) "Ctrl+Shift+Tab from Edit: round to the Grail, the last bookmark"
  Key '^{TAB}'
  Check ([bool](WaitUntil { OnEdit 'Beast Necklace' } "Edit again" 3000)) "Ctrl+Tab from the Grail: round to Edit"
}

Case 'keyboard.number-box-steps-by-the-keys-and-is-written' -Tags keys, items -Changes {
  # A number box steps by the keys: Up one, PageDown ten (the amulet's item level, 81).
  Choose 'Beast Necklace'
  # The panel at its top: the level's box stands under the item's holder.
  Front; [Fast]::SetCursorPos([int]($script:r.X + $script:r.Width * 0.75), [int]($script:r.Y + $script:r.Height * 0.6)) | Out-Null
  1..30 | ForEach-Object { [Fast]::mouse_event(0x0800, 0, 0, 120, 0); Start-Sleep -Milliseconds 30 }
  Start-Sleep -Milliseconds 400
  $level = LevelBox
  ClickEl $level "the item level box"
  $written = (Get-Item "$pad\saves\PhySpearZon.d2s").LastWriteTimeUtc
  Key '{UP}'
  $was = $level.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
  # No key keeps (Ctrl+S was KEEP): a level stepped is written by itself.
  Check ([bool](WaitUntil { if ((WrittenAt "$pad\saves\PhySpearZon.d2s") -ne $written) { 'kept' } } "the save written" 8000)) "a level stepped is written with no key pressed"
  Key '{PGDN}'
  $now = $level.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
  Check ($was -eq '82' -and $now -eq '72') "Up steps a number box by one, PageDown by ten: 81 -> $was -> $now"
}

Case 'keyboard.arrows-move-the-selection' -Tags keys, items {
  # The arrows move the one selected to the nearest item that way - a plain click, nothing lifted: the inventory's
  # two tomes side by side, Town Portal then Identify.
  Choose 'Tome of Town Portal'
  $first = (ById 'Item_Name').Current.Name
  Key '{RIGHT}'
  $moved = (ById 'Item_Name').Current.Name
  Key '{LEFT}'
  $back = (ById 'Item_Name').Current.Name
  Check ($moved -and $moved -ne $first -and $back -eq $first) "the arrows move the selection, and back: $first -> $moved -> $back"
}

Case 'keyboard.ctrl-page-keys-switch-the-left-page' -Tags keys, vault {
  Choose 'Beast Necklace'
  Key '^{PGDN}'
  Check ([bool](WaitUntil { LeftHas 'Up one folder (Alt + Up)' } "the left vault" 3000)) "Ctrl+PageDown: the left page's Vault"
  Key '^{PGUP}'
  Check ([bool](WaitUntil { ItemsPage } "the inventory again" 3000)) "Ctrl+PageUp: the Inventory again"
}

Case 'keyboard.vault-f2-names-the-folder-and-backspace-is-back' -Tags keys, vault -Changes -Typing {
  Choose 'Beast Necklace'
  Key '^{PGDN}'
  WaitUntil { LeftHas 'Up one folder (Alt + Up)' } "the left vault" 3000 | Out-Null

  # The vault, a folder's sheet: F2 names the folder SELECTED, on its tile, as Explorer's does - Enter keeping the
  # name. And Backspace is back, as Explorer's,
  # which from a folder just gone into is the folder above.
  WaitUntil { VaultPages } "the vault's pages" | Out-Null
  VaultGoTo 'Vault'
  # A click on a folder's tile selects it, and gives the sheet the keys.
  ClickEl (WaitUntil { VaultFolderTile 'Charms' } "the tile Charms" 3000) "Charms' tile"
  Key '{F2}'
  WaitUntil { ById 'SheetFolder_Name' } "the name box on the tile" 3000 | Out-Null
  Key '^aCharmz{ENTER}'
  Check ([bool](WaitUntil { VaultFolderTile 'Charmz' } "the tile renamed" 3000)) "F2: the selected folder's name box, on its tile - typed, Enter keeps it"
  Check ((VaultShown) -eq 'Vault') "and the page stays where it was ('$(VaultShown)')"
  Key '{F2}'
  WaitUntil { ById 'SheetFolder_Name' } "the name box again" 3000 | Out-Null
  Key '^aCharms{ENTER}'
  WaitUntil { VaultFolderTile 'Charms' } "the name put back" 3000 | Out-Null

  VaultGoTo 'Charms'
  ClickEl (VaultPages) "the pages"
  Key '{BACKSPACE}'
  Check ([bool](WaitUntil { if ((VaultShown) -eq 'Vault') { 'up' } } "back where it was" 3000)) "Backspace: back to where the page was ('$(VaultShown)')"
}

Case 'keyboard.ctrl-f-puts-the-pointer-in-the-search-box' -Tags keys, search {
  # Ctrl+F is the window's search box: the pointer goes into it, from any page.
  Choose 'Beast Necklace'
  Key '^f'
  Check ([bool](WaitUntil { if ((Focused).Current.AutomationId -eq 'Quick_Box') { 'in' } } "the search box" 3000)) "Ctrl+F: the pointer is in the window's search box"
  Key '{ESC}'
  Check ((ById 'Quick_Box').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '') "Esc leaves the box empty"
}

Case 'keyboard.ctrl-c-ctrl-v-copy-and-ctrl-z-undoes' -Tags keys, items, clipboard -Changes {
  # Ctrl+C then Ctrl+V make a second of what is selected;
  # Ctrl+Z takes it back.
  Choose 'Beast Necklace'
  $count = @(Tiles 'Beast Necklace').Count
  Key '^v'
  Start-Sleep -Milliseconds 400
  Check (@(Tiles 'Beast Necklace').Count -eq $count) "Ctrl+V with nothing copied: no copy"
  Key '^c'
  Key '^v'
  Check ([bool](WaitUntil { if (@(Tiles 'Beast Necklace').Count -eq $count + 1) { 'two' } } "a copy" 5000)) "Ctrl+C, Ctrl+V: a copy beside it ($count -> $(@(Tiles 'Beast Necklace').Count))"
  # The copy is a step once it is written: the undo has it then.
  Kept "the copy"
  Key '^z'
  Check ([bool](WaitUntil { if (@(Tiles 'Beast Necklace').Count -eq $count) { 'one' } } "the copy undone" 8000)) "Ctrl+Z takes the copy back"
}

# Rows by the keys where a list is no picker's (ListKeys): a menu, the Forge's finds, the Search
# page's results. The text a focused row shows: its first text.
function RowText($el) { if ($el) { $t = $el.FindFirst($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text)); if ($t) { $t.Current.Name } } }

Case 'keyboard.a-menu-by-the-keys' -Tags keys, vault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's pages" | Out-Null
  # UI Automation does not see the focus inside a menu's own window: what the keys pick is read off the strip.
  # Name in force first, by the pointer; then the menu by the keys - the first Down lights Name, the next Rarity.
  function SortMenu { ClickUntil { ClickEl (ByTip 'Sort' -Prefix) "the sort menu" } { ByName 'Ascending' } "the sort menu" 800 | Out-Null }
  SortMenu
  ClickEl (@((All (Cond $AE::NameProperty 'Name')) | Where-Object { -not $_.Current.IsOffscreen } | Sort-Object { $_.Current.BoundingRectangle.Y })[-1]) "Name"
  WaitUntil { ByName 'Sorted by Name.' } "sorted by Name" | Out-Null
  SortMenu
  Key '{DOWN}'; Key '{DOWN}'; Key '{ENTER}'
  Check ([bool](WaitUntil { ByName 'Sorted by Rarity.' } "sorted by Rarity" 3000)) "opened on Name: Down lights it, Down again Rarity, Enter sorts by it"
  SortMenu
  Key '{END}'; Key '{UP}'; Key '{HOME}'; Key '{ENTER}'
  Check ([bool](WaitUntil { ByName 'Sorted by Stored.' } "sorted by Stored" 3000)) "End, Up, Home: the first row, Stored"
  SortMenu
  Key '{ESC}'
  Check ([bool](WaitUntil { if (-not (ByName 'Ascending')) { 'closed' } } "the menu closed" 3000)) "Esc closes the menu"
}

# The list of folders under the vault's path is a menu as the Sort menu is: the first Down lights the folder shown,
# the next the one under it, and Enter goes to it.
Case 'keyboard.folder-list-by-the-keys' -Tags keys, vault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  VaultGoTo 'Vault'
  ClickUntil { ClickEl (ById 'Vault_Folder') "all folders" } { VaultRow 'Charms' -Listed } "the list of folders" 800 | Out-Null
  Key '{DOWN}'; Key '{DOWN}'; Key '{ENTER}'
  Check ([bool](WaitUntil { if ((VaultShown) -eq 'Vault\Charms') { 'there' } } "Charms shown" 3000)) "Down, Down, Enter in the list of folders: the folder under the one shown is gone to ('$(VaultShown)')"
  Check (-not (VaultRow 'Jewels' -Listed)) "and the list is put away"
  VaultGoTo 'Vault'
}

Case 'keyboard.forge-finds-by-the-keys' -Tags keys, forge -Typing {
  ClickEl (WaitUntil { ById 'Bookmark_Forge' } "the Forge bookmark") "Forge"
  $box = WaitUntil { ById 'Forge_Search' } "the Forge's search box"
  ClickEl $box "the Forge's search box"
  Key 'shako'
  Start-Sleep -Milliseconds 400
  Key '{DOWN}'
  $one = RowText (Focused)
  Check ((Focused).Current.ControlType -eq $CT::Button -and $one) "Down from the search box: its first find ('$one')"
  Key '{DOWN}'
  $two = RowText (Focused)
  Check ($two -and $two -ne $one) "Down again: the next ('$two')"
  Key '{UP}'; Key '{UP}'
  Check ((Focused).Current.AutomationId -eq 'Forge_Search') "Up from the first: back to the search box"
  Key '{ENTER}'
  Check ((RowText (Focused)) -eq $one) "Enter in the box: into the finds, as a picker's does"
}

Case 'keyboard.search-results-by-the-keys' -Tags keys, search {
  try {
    ItemSearchFor 'shael'
    ClickEl (ItemSearchBox) "the keyword box"
    Key '{DOWN}'
    $one = (Focused)
    Check ($one.Current.AutomationId -eq 'Found_Row') "Down from the keyword box: the first find ('$($one.Current.Name)')"
    Key '{DOWN}'
    Check ((Focused).Current.AutomationId -eq 'Found_Row' -and (Focused).Current.Name -ne $one.Current.Name -or (ItemSearchRows).Count -eq 1) "Down again: the next ('$((Focused).Current.Name)')"
    Key '{UP}'; Key '{UP}'
    Check ((Focused).Current.AutomationId -eq 'Search_Keyword') "Up from the first: back to the keyword box"
  }
  finally { ItemSearchEnd }
}
