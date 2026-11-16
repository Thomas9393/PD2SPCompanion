# The right click, as only a window shows it (no
# chords): that a right click of the mouse
# opens the menu of what it is on - a worn tile, a tile in a grid, a vault card - and selects it; that the keys
# held with it change nothing; that a row picked does what it says; that a row which cannot be done is dimmed;
# that Esc and a click beside it close it, as the window left and a turn of the wheel do; and that the Menu key
# opens it for what is selected, Down and Enter picking a row.
#
# What each row then does - which slot, what comes off and where it lands, the belt's potions, the vault's swap,
# every refusal's words - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Items/RightClickTests.
#
# On the pad's PhySpearZon: Laying of Hands on its hands, an Annihilus in its inventory, a Tome of Town Portal in
# the vault's first folder.
Add-Type -AssemblyName System.Windows.Forms

# A right click on an element's middle, with keys held: 'Shift', 'Ctrl', 'Alt', or several joined by '+'.
function RightClick($el, [string]$what, [string]$with = '') {
  if ($null -eq $el) { throw "not found, not right-clicked: $what" }
  Front
  $b = $el.Current.BoundingRectangle
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 120
  $keys = @($with -split '\+' | Where-Object { $_ } | ForEach-Object { @{ Shift = 0x10; Ctrl = 0x11; Alt = 0x12 }[$_] })
  foreach ($key in $keys) { [Fast]::keybd_event($key, 0, 0, 0); Start-Sleep -Milliseconds 40 }
  try { [Fast]::mouse_event(8, 0, 0, 0, 0); Start-Sleep -Milliseconds 40; [Fast]::mouse_event(16, 0, 0, 0, 0) }
  finally { foreach ($key in $keys) { Start-Sleep -Milliseconds 40; [Fast]::keybd_event($key, 0, 2, 0) } }
  Start-Sleep -Milliseconds 500
}
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }
# What a strip says, waited for.
function Says([string]$text, [int]$ms = 5000) { try { [bool](WaitUntil { ByName $text } "the strip: $text" $ms) } catch { $false } }

Case 'right-click.opens-the-items-menu-and-selects-it' -Tags right-click, items, menu {
  MenuOf (WaitUntil { Tile 'Annihilus' } "the Annihilus in the inventory") 'the Annihilus'
  Check ((MenuWords) -eq 'Store in vault; Store sorted; Store as crafting base; Store in compendium' -and (MenuIcons) -eq 'Cut; Copy; Paste; Delete') "a charm's menu: the clipboard's three and the bin at its head, then what is done to what nobody wears ('$(MenuIcons)' / '$(MenuWords)')"
  Check ([bool](WaitUntil { ByName 'Annihilus' } "the Edit page's name" 3000)) "and the charm is selected: the Edit page shows it"
  Keys '{ESC}'
  Check (-not (MenuUp)) "Esc closes the menu"
  Check ($null -ne (Tile 'Annihilus')) "and the Annihilus is where it was"

  # The keys held with it are no chords any more: the same menu, and nothing done.
  foreach ($with in 'Shift', 'Ctrl', 'Ctrl+Shift') {
    RightClick (Tile 'Annihilus') 'the Annihilus' $with
    Check ((MenuWords) -eq 'Store in vault; Store sorted; Store as crafting base; Store in compendium') "$with + right click: the same menu ('$(MenuWords)')"
    Keys '{ESC}'
  }

  # A click beside it closes it, and is a click: the gloves are selected by it.
  MenuOf (Tile 'Annihilus') 'the Annihilus'
  ClickEl (Tile 'Laying of Hands') "the worn gloves, the menu up"
  Check (-not (MenuUp)) "a click beside the menu closes it"
  try { WaitUntil { if ((ById 'Inv_Equip').Current.HelpText -eq 'Take off selected item') { 'so' } } "the button turned to Take off" 3000 | Out-Null } catch { }
  Check ((ById 'Inv_Equip').Current.HelpText -eq 'Take off selected item') "and went on to what it was made on: the gloves selected ('$((ById 'Inv_Equip').Current.HelpText)')"

  # By the keys, for what is selected. Worn, it goes into the compendium only once taken off (CompendiumMoves.WhyNot).
  Keys '+{F10}'
  Check ((MenuWords) -eq 'Take off; Store in vault; Store sorted; Store as crafting base; [Store in compendium]') "Shift + F10: the menu of what is selected ('$(MenuWords)')"
  Keys '{ESC}'
}

Case 'right-click.vault-card-has-a-menu-of-its-own' -Tags right-click, vault, menu {
  ClickEl (ById 'Bookmark_Vault_Right') "the vault, on the right page"
  MenuOf (WaitUntil { VaultTile 'Tome of Town Portal' } "the tome's card") 'the tome'
  Check ((MenuWords) -eq 'Withdraw; [To other page]; Move to folder...' -and (MenuIcons) -eq 'Cut; Copy; Paste; Delete') "a card nothing wears: '$(MenuIcons)' / '$(MenuWords)'"
  Keys '{ESC}'
}

# A worn item's Store in vault: off and into the vault in one, as its chord did - whichever
# page is beside: the row is the tools' Store.
Case 'right-click.worn-item-is-stored-from-its-menu' -Tags right-click, items, vault, menu -Changes {
  $gloves = 'Laying of Hands'
  ClickEl (ById 'Bookmark_Vault_Right') "the vault, on the right page"
  MenuOf (WaitUntil { Tile $gloves } "the worn gloves") $gloves
  MenuPick 'Store in vault'
  Check ([bool](WaitUntil { VaultTile $gloves } "the gloves in the vault" 5000)) "Store in vault on a worn item: the gloves are in the vault"
  # One tile of that name left: the vault's card - none worn.
  Check (@(Tiles $gloves).Count -eq 1) "and no longer worn ($(@(Tiles $gloves).Count) tile(s) of that name)"
}

Case 'right-click.worn-comes-off-and-goes-back-on' -Tags right-click, items, menu -Changes {
  $gloves = 'Laying of Hands'
  MenuOf (WaitUntil { Tile $gloves } "the worn gloves") $gloves
  Check ((MenuWords) -like 'Take off; *') "a worn item's menu begins with Take off ('$(MenuWords)')"
  MenuPick 'Take off'
  Check (Says "$gloves taken off.") "Take off: '$gloves taken off.'"
  MenuOf (WaitUntil { Tile $gloves } "the gloves in the grid") $gloves
  Check ((MenuWords) -like 'Equip; Equip on mercenary; *') "in the grid its menu begins with Equip ('$(MenuWords)')"
  MenuPick 'Equip'
  Check (Says "Equipped $gloves.") "Equip: 'Equipped $gloves.'"
}

# The tools line says in sight what the menu holds: one button that equips, or takes off what is worn.
Case 'right-click.tools-line-equips-and-takes-off' -Tags right-click, items -Changes {
  $gloves = 'Laying of Hands'
  ClickEl (WaitUntil { Tile $gloves } "the worn gloves") "the worn gloves"
  # Waited for: the selection reaches the tools a moment after the click.
  try { WaitUntil { if ((ById 'Inv_Equip').Current.HelpText -eq 'Take off selected item') { 'so' } } "the button turned to Take off" 3000 | Out-Null } catch { }
  Check ((ById 'Inv_Equip').Current.HelpText -eq 'Take off selected item' -and -not (ById 'Inv_EquipMerc').Current.IsEnabled) "a worn item selected: the button takes off, and the mercenary's is dead ('$((ById 'Inv_Equip').Current.HelpText)')"
  ClickEl (ById 'Inv_Equip') "the tools' Equip, as Take off"
  Check (Says "$gloves taken off.") "'$gloves taken off.'"
  ClickEl (WaitUntil { Tile $gloves } "the gloves in the grid") "the gloves"
  Check ([bool](WaitUntil { if ((ById 'Inv_Equip').Current.HelpText -eq 'Equip selected item') { 'equip' } } "the button turned to Equip" 3000)) "in the grid: the button equips"
  ClickEl (ById 'Inv_Equip') "the tools' Equip"
  Check (Says "Equipped $gloves.") "'Equipped $gloves.'"
  ClickEl (Tile 'Annihilus') "the Annihilus"
  Check ([bool](WaitUntil { if (-not (ById 'Inv_Equip').Current.IsEnabled) { 'dead' } } "the button dead" 3000)) "what nobody wears: the button is dead"
}

# What is no item has a menu too: a pane's empty cell, the vault's sheet. And Store all's other ways are listed
# under the arrow beside it, where its tooltip alone told them.
function RightAt([int]$x, [int]$y) {
  Front; [Fast]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 150
  [Fast]::mouse_event(8, 0, 0, 0, 0); Start-Sleep -Milliseconds 60; [Fast]::mouse_event(16, 0, 0, 0, 0)
  try { WaitUntil { if (MenuUp) { 'up' } } "a menu at $x, $y" 3000 | Out-Null } catch { }
  Start-Sleep -Milliseconds 150
}

Case 'right-click.empty-room-and-the-sheet-have-menus' -Tags right-click, items, vault, menu {
  # An empty cell of the inventory: the second row's seventh, on the pad.
  $p = PaneCell 0 6 1 (CellPx (WaitUntil { Tile 'Annihilus' } "the Annihilus"))
  RightAt ([int]($script:r.X + $p[0])) ([int]($script:r.Y + $p[1]))
  Check ((MenuIcons) -eq 'Paste' -and (MenuWords) -eq 'Select all') "a pane's empty cell: Paste, and Select all ('$(MenuIcons)' / '$(MenuWords)')"
  Keys '{ESC}'

  ClickEl (ById 'Bookmark_Vault_Right') "the vault, on the right page"
  WaitUntil { VaultTile 'Tome of Town Portal' } "the tome's card" | Out-Null
  $s = (ById 'Vault_Pages').Current.BoundingRectangle
  RightAt ([int]($s.X + $s.Width / 2)) ([int]($s.Y + $s.Height / 2))
  Check ((MenuIcons) -eq 'Paste' -and (MenuWords) -eq 'New folder; Sort by; Select all') "the sheet's empty room: Paste, New folder, Sort by, Select all ('$(MenuIcons)' / '$(MenuWords)')"

  # Sort by opens its list beside the menu: by Right, on the row the arrows lit; Left shuts the list and leaves the menu.
  Keys '{DOWN}'; Keys '{DOWN}'; Keys '{RIGHT}'
  Check ((MenuMoreWords) -eq 'Stored; Name; Rarity; Type; Ascending; Descending') "Right on Sort by: the orders, listed beside it ('$(MenuMoreWords)')"
  $menu = (ById 'ActMenu').Current.BoundingRectangle; $more = (ById 'ActMenu_More').Current.BoundingRectangle
  Check ($more.X -gt $menu.X + $menu.Width / 2) "the list stands to the menu's right ($([int]$more.X) against $([int]$menu.X))"
  Keys '{LEFT}'
  Check ((MenuMoreWords) -eq '' -and (MenuUp)) "Left shuts the list; the menu stays"
  MenuPick 'Select all'
  Check ([bool](WaitUntil { if ((ById 'Vault_Cut').Current.IsEnabled) { 'picked' } } "something selected" 3000)) "its Select all selects the folder's items"
  Keys '{ESC}'
}

Case 'right-click.store-all-lists-its-ways-under-its-arrow' -Tags right-click, items, menu {
  ClickEl (WaitUntil { ById 'Inv_StoreAllMore' } "the arrow beside Store all" 4000) "the arrow beside Store all"
  WaitUntil { if (MenuUp) { 'up' } } "the list of ways" 3000 | Out-Null
  Check ((MenuWords) -eq 'Store all in open folder; Store all, sorted into folders; Store only crafting bases, sorted') "the arrow lists the button's ways, each saying where it stores ('$(MenuWords)')"

  # The arrow is joined to its button: one split button, no gap between the two.
  $arrow = (ById 'Inv_StoreAllMore').Current.BoundingRectangle
  $lead = @((All (Cond $AE::ControlTypeProperty $CT::Button)) | Where-Object {
      $b = $_.Current.BoundingRectangle
      -not $_.Current.IsOffscreen -and [math]::Abs($b.Y - $arrow.Y) -le 1 -and $b.X -lt $arrow.X -and $b.X + $b.Width -ge $arrow.X - 3 -and $b.X + $b.Width -le $arrow.X + 3 })[0]
  Check ($null -ne $lead) "the arrow stands hard against its button: one split button"

  # And the list hangs from the button's left edge, under it - not from the arrow.
  $list = (ById 'ActMenu').Current.BoundingRectangle; $b = $lead.Current.BoundingRectangle
  Check ([math]::Abs($list.X - $b.X) -le 1.5 -and [math]::Abs($list.Y - ($b.Y + $b.Height)) -le 2) "the list is left aligned with the button, under it ($([math]::Round($list.X - $b.X, 1)) across, $([math]::Round($list.Y - ($b.Y + $b.Height), 1)) down)"
  MenuPick 'Store all, sorted into folders'
  # The whole line on the strip: how many, in which way, where from, and the tick to press again.
  $asked = $null
  try { $asked = WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -match '^Store \d+ items? in vault, sorted\? .*Click green tick again to confirm\.$' })[0] } "the strip's question" 5000 } catch { }
  Check ($null -ne $asked) "a way picked asks as the button's own press does ('$(if ($asked) { $asked.Current.Name })')"
  # Taken back by a press elsewhere, as any asked question of the button's.
  ClickEl (Tile 'Laying of Hands') "elsewhere"
}

Case 'right-click.character-card-has-a-menu' -Tags right-click, home, menu -Start Home {
  $card = @((All (Cond $AE::ControlTypeProperty $CT::ListItem)) | Where-Object { -not $_.Current.IsOffscreen })[0]
  MenuOf $card "a character's card"
  Check ((MenuWords) -eq 'Open; Rename; Clone; Backups' -and (MenuIcons) -eq 'Delete') "a card's menu: Open, Rename, Clone, Backups, and Delete at its head ('$(MenuIcons)' / '$(MenuWords)')"
  MenuPick 'Rename'
  Check ([bool](WaitUntil { ById 'NameBox' } "the name box" 3000)) "its Rename opens the name box"
  Keys '{ESC}'

  # Its Backups lists the card's earlier copies, the character not opened.
  MenuOf $card "the card again"
  MenuPick 'Backups'
  Check ([bool](WaitUntil { ByName 'Restore replaces current character.' } "the list of backups" 3000)) "its Backups opens the list of its earlier copies"
  Keys '{ESC}'
}

# A paste lands where it was asked for: from a menu on the cell the menu was opened on, by the key on the cell under
# the pointer - a cut, and a copy alike (in the first version the key took a
# pane and no cell, and did not paste at the cursor even with room there).
function Mid($el) { $b = $el.Current.BoundingRectangle; @([int]($b.X + $b.Width / 2 - $script:r.X), [int]($b.Y + $b.Height / 2 - $script:r.Y)) }

Case 'right-click.paste-lands-on-the-cell-its-menu-was-opened-on' -Tags right-click, items, menu, clipboard -Changes {
  $ring = 'Steel Ring of Skill'
  Pane 0 'Personal Stash'
  $cell = CellPx (WaitUntil { Tile $ring } "the ring")
  ClickEl (Tile $ring) "the ring"
  Keys '^x'
  # The fifth row's eighth cell: empty, on the pad's stash.
  $p = PaneCell 0 7 4 $cell
  RightAt ([int]($script:r.X + $p[0])) ([int]($script:r.Y + $p[1]))
  Check ((MenuIcons) -eq 'Paste' -and (MenuWords) -eq 'Sort by; Close gaps; Select all') "the stash's empty cell: Paste; Select all, Sort by, Close gaps ('$(MenuIcons)' / '$(MenuWords)')"
  MenuPick 'Paste'
  Start-Sleep -Milliseconds 400
  $at = Mid (Tile $ring)
  Check ([math]::Abs($at[0] - $p[0]) -le 4 -and [math]::Abs($at[1] - $p[1]) -le 4) "the menu's Paste: the ring lies on that cell ($($at -join ',') against $($p -join ','))"

  # By the key: the cell the pointer is over, on the page that pane shows.
  ClickEl (Tile $ring) "the ring"
  Keys '^x'
  Pane 0 'Shared Page 5'
  $q = PaneCell 0 5 5 $cell
  Front; [Fast]::SetCursorPos([int]($script:r.X + $q[0]), [int]($script:r.Y + $q[1])) | Out-Null; Start-Sleep -Milliseconds 300
  Keys '^v'
  $at = Mid (WaitUntil { Tile $ring } "the ring on the shared page")
  Check ([math]::Abs($at[0] - $q[0]) -le 4 -and [math]::Abs($at[1] - $q[1]) -le 4) "Ctrl + V: the cell under the pointer ($($at -join ',') against $($q -join ','))"

  # A copy lands there too - it went to the page's head, whatever the pointer was over.
  ClickEl (Tile $ring) "the ring"
  Keys '^c'
  $c = PaneCell 0 8 2 $cell
  Front; [Fast]::SetCursorPos([int]($script:r.X + $c[0]), [int]($script:r.Y + $c[1])) | Out-Null; Start-Sleep -Milliseconds 300
  Keys '^v'
  $two = WaitUntil { $t = @(Tiles $ring); if ($t.Count -eq 2) { , $t } } "the ring and its copy" 4000
  $near = @($two | ForEach-Object { $m = Mid $_; [math]::Abs($m[0] - $c[0]) + [math]::Abs($m[1] - $c[1]) } | Sort-Object)[0]
  Check ($near -le 8) "Ctrl + V of a copy: the copy lies on the cell under the pointer (off by $near)"
  Pane 0 'Inventory'
}

# A find on the Search page has its item's menu, Go to its place first.
Case 'right-click.find-has-its-items-menu' -Tags right-click, search, menu {
  $box = WaitUntil { ById 'Quick_Box' } "the window's search box" 3000
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('stone')
  Start-Sleep -Milliseconds 800
  ClickEl (ById 'Quick_Box') "the box"
  Keys '^{ENTER}'
  $find = WaitUntil { @((All (Cond $AE::AutomationIdProperty 'Found_Row')) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -eq 'Stone Barb' })[0] } "the finds, on the Search page" 6000
  MenuOf $find 'the find Stone Barb'
  Check ((MenuWords) -like 'Go to its place; Take off; *') "a worn find: Go to its place, then its item's rows ('$(MenuWords)')"
  Keys '{ESC}'
  # What was asked is emptied: it is kept on the bookmark, and the next case to take it would begin on this search.
  ItemSearchEnd
}

# The menu by the keyboard alone: the Menu key opens it for what is selected, the first Down lights its first
# row, and Enter picks it.
Case 'right-click.menu-key-opens-it-and-enter-picks-a-row' -Tags right-click, items, menu, keys -Changes {
  $gloves = 'Laying of Hands'
  ClickEl (WaitUntil { Tile $gloves } "the worn gloves") $gloves
  try { WaitUntil { if ((ById 'Inv_Equip').Current.HelpText -eq 'Take off selected item') { 'so' } } "the gloves selected" 3000 | Out-Null } catch { }
  Front; [Fast]::keybd_event(0x5D, 0, 0, 0); [Fast]::keybd_event(0x5D, 0, 2, 0)
  $up = $false
  try { WaitUntil { if (MenuUp) { 'up' } } "the menu, by the Menu key" 3000 | Out-Null; $up = $true } catch { }
  Check ($up -and (MenuWords) -like 'Take off; *') "the Menu key: the menu of what is selected ('$(MenuWords)')"
  Keys '{DOWN}'; Keys '{ENTER}'
  Check (Says "$gloves taken off.") "Down lights the first row and Enter picks it: '$gloves taken off.'"
  Check (-not (MenuUp)) "and the menu is closed"
}

# The menu does not wait for a window that is left, and does not ride a page that is scrolled under it.
Case 'right-click.menu-closes-as-the-window-is-left-and-at-a-turn-of-the-wheel' -Tags right-click, items, menu {
  MenuOf (WaitUntil { Tile 'Annihilus' } "the Annihilus in the inventory") 'the Annihilus'
  [Fast]::ShowWindow($script:proc.MainWindowHandle, 6) | Out-Null; Start-Sleep -Milliseconds 500
  Front; Start-Sleep -Milliseconds 400
  Check (-not (MenuUp) -and $null -ne (Tile 'Annihilus')) "the window put away and brought back: the menu is not there any more"

  # The wheel, beside the menu: over the worn gloves, with the menu opened on the charm.
  MenuOf (Tile 'Annihilus') 'the Annihilus'
  $b = (Tile 'Laying of Hands').Current.BoundingRectangle
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 150
  [Fast]::mouse_event(0x0800, 0, 0, -120, 0); Start-Sleep -Milliseconds 400
  Check (-not (MenuUp)) "a turn of the wheel beside the menu closes it"
}

# A menu's Delete asks in the menu, the second press on the menu and not on the tool row:
# the icon turns to Confirm and the menu stays; pressed again, the item is gone.
Case 'right-click.delete-asks-in-the-menu-and-the-second-press-deletes' -Tags right-click, items, menu -Changes {
  $ring = 'Steel Ring of Skill'
  Pane 0 'Personal Stash'
  MenuOf (WaitUntil { Tile $ring } "the ring") 'the ring'
  Check ((MenuIcons) -like '*Delete') "the ring's menu has Delete at its head ('$(MenuIcons)')"
  # The menu, every icon and every row, to the pixel: before the first press, and while it asks.
  function Laid {
    $m = (ById 'ActMenu').Current.BoundingRectangle
    $parts = @("menu $($m.X),$($m.Y) $($m.Width)x$($m.Height)")
    foreach ($id in 'ActMenu_Icon', 'ActMenu_Row') {
      foreach ($el in @((All (Cond $AE::AutomationIdProperty $id)) | Where-Object { -not $_.Current.IsOffscreen })) { $b = $el.Current.BoundingRectangle; $parts += "$($b.X),$($b.Y) $($b.Width)x$($b.Height)" }
    }
    $parts -join ' | '
  }
  $before = Laid
  MenuPick 'Delete'
  Start-Sleep -Milliseconds 300
  Check ((MenuUp) -and (MenuIcons) -like '*Confirm' -and $null -ne (Tile $ring)) "pressed once: the menu stays, the icon says Confirm, the ring is there ('$(MenuIcons)')"
  $asking = Laid
  Check ($asking -eq $before) "and nothing of the menu moved or grew for it, to the pixel ($(($before -split ' \| ')[0]) before, $(($asking -split ' \| ')[0]) asking)"
  MenuPick 'Confirm'
  Check ([bool](WaitUntil { if (-not (Tile $ring)) { 'gone' } } "the ring gone" 4000)) "pressed again: the ring is deleted"
  Check (-not (MenuUp)) "and the menu is closed"
  Pane 0 'Inventory'
}

# A card's Backups button: what its menu's row does, in sight.
Case 'right-click.character-card-has-a-backups-button' -Tags right-click, home -Start Home {
  $card = @((All (Cond $AE::ControlTypeProperty $CT::ListItem)) | Where-Object { -not $_.Current.IsOffscreen })[0]
  ClickEl $card "a character's card"
  ClickEl (WaitUntil { ById 'Card_Backups' } "the card's Backups button" 3000) "Backups"
  Check ([bool](WaitUntil { ByName 'Restore replaces current character.' } "the list of backups" 3000)) "the card's Backups button opens the list of its earlier copies"
  Keys '{ESC}'
}
