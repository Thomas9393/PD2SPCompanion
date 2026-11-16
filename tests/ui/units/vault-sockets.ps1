# What the Items page had of sockets and the vault's page had not, as only a window shows it (opaque sockets and the socket
# guard were missing in the vault; both in sync with the inventory, for no frustrating inconsistency): that the vault's tools line has the two socket buttons and a press on
# either page is seen on both; that X fades the sockets on the vault's page; that a gem dragged onto a card with an
# empty socket goes into it; and that Equip stands on the vault's tools line for the card selected.
#
# What the pages decide - one of a stack, the guard, what is no socketable, why Equip is dimmed - is proven without a
# window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultSocketingTests.
#
# On the pad's PhySpearZon: Stone Barb worn, a Perfect Skull in its third socket; a Steel Ring of Skill in the
# personal stash; a Tome of Town Portal at the vault's top; the inventory's second row's seventh cell bare.
Add-Type -AssemblyName System.Windows.Forms

function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }
function Says([string]$text, [int]$ms = 5000) { try { [bool](WaitUntil { ByName $text } "the strip: $text" $ms) } catch { $false } }
function Cells { @((All (Cond $AE::AutomationIdProperty 'Socket_Cell')) | Where-Object { -not $_.Current.IsOffscreen }) }
# The first line of every button's tooltip in sight that begins so: the two pages' buttons for one thing.
function TipsLike([string]$like) {
  @((All (Cond $AE::ControlTypeProperty $CT::Button)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.HelpText -like $like } |
    ForEach-Object { ($_.Current.HelpText -split "`r?`n")[0] })
}

Case 'vault-sockets.the-two-buttons-are-one-with-the-items-pages' -Tags vault, sockets, items -Changes {
  ClickEl (ById 'Bookmark_Vault_Right') "the vault, on the right page"
  WaitUntil { ById 'Vault_Sockets' } "the vault's sockets button" | Out-Null
  try {
    # Shown or faded: both pages' buttons say the same, before a press and after it - on either page.
    $was = TipsLike '* sockets (X)'
    Check ($was.Count -eq 2 -and $was[0] -eq $was[1]) "both pages have the sockets button, and say the same ('$($was -join "' / '")')"
    ClickEl (ById 'Vault_Sockets') "the vault's sockets button"
    $now = WaitUntil { $t = TipsLike '* sockets (X)'; if ($t.Count -eq 2 -and $t[0] -ne $was[0]) { , $t } } "the buttons turned" 3000
    Check ($now[0] -eq $now[1]) "pressed on the vault's page: both turned ('$($now -join "' / '")')"
    $items = @((All (Cond $AE::ControlTypeProperty $CT::Button)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.HelpText -like '* sockets (X)' -and $_.Current.AutomationId -ne 'Vault_Sockets' })[0]
    ClickEl $items "the Items page's sockets button"
    $back = WaitUntil { $t = TipsLike '* sockets (X)'; if ($t.Count -eq 2 -and $t[0] -eq $was[0]) { , $t } } "the buttons turned back" 3000
    Check ($back[0] -eq $back[1]) "pressed on the Items page: both back ('$($back -join "' / '")')"

    # The guard: the same.
    $off = TipsLike 'Socket guard *'
    Check ($off.Count -eq 2 -and $off[0] -eq 'Socket guard off' -and $off[1] -eq $off[0]) "both pages have the guard, off ('$($off -join "' / '")')"
    ClickEl (ById 'Vault_SocketGuard') "the vault's socket guard"
    $on = WaitUntil { $t = TipsLike 'Socket guard *'; if ($t.Count -eq 2 -and $t[0] -eq 'Socket guard on') { , $t } } "the guard on" 3000
    Check ($on[1] -eq 'Socket guard on') "pressed on the vault's page: on, on both"
  }
  finally {
    # Off again, as it was met: it is kept in the settings.
    if ((TipsLike 'Socket guard *')[0] -eq 'Socket guard on') { ClickEl (ById 'Vault_SocketGuard') "the guard, off again"; Start-Sleep -Milliseconds 300 }
    if ((TipsLike '* sockets (X)')[0] -ne $was[0]) { ClickEl (ById 'Vault_Sockets') "the sockets, as they were"; Start-Sleep -Milliseconds 300 }
  }
}

Case 'vault-sockets.x-fades-the-sockets-on-the-vaults-page' -Tags vault, sockets, keys -Typing {
  ClickEl (ById 'Bookmark_Vault_Left') "the vault, on the left page"
  $button = WaitUntil { ById 'Vault_Sockets' } "the vault's sockets button"
  $was = ($button.Current.HelpText -split "`r?`n")[0]
  ClickEl (WaitUntil { ById 'Vault_Pages' } "the sheet") "the sheet (the keys are the page's)"
  Keys 'x'
  Check ([bool](WaitUntil { if (((ById 'Vault_Sockets').Current.HelpText -split "`r?`n")[0] -ne $was) { 'turned' } } "the button turned" 3000)) "X on the vault's page turns the sockets ('$was' -> '$(((ById 'Vault_Sockets').Current.HelpText -split "`r?`n")[0])')"
  Check ($null -ne (VaultTile 'Tome of Town Portal')) "and goes to no item by its name: the tome is where it was, nothing selected by the letter"
  Keys 'x'
  Check ([bool](WaitUntil { if (((ById 'Vault_Sockets').Current.HelpText -split "`r?`n")[0] -eq $was) { 'back' } } "the button back" 3000)) "X again: as it was"
  ClickEl (ById 'Bookmark_Inventory') "the inventory, on the left page"
}

Case 'vault-sockets.gem-dragged-onto-a-card-goes-into-it' -Tags vault, sockets, drag -Changes {
  # A card with an empty socket, and a gem's card: Stone Barb less its skull, and the skull - both stored.
  ClickEl (WaitUntil { Tile 'Stone Barb' } "the worn Stone Barb") "Stone Barb"
  WaitUntil { if (@(Cells).Count -eq 4) { 'four' } } "its four sockets" 4000 | Out-Null
  $p = PaneCell 0 6 1 (CellPx (WaitUntil { Tile 'Annihilus' } "the Annihilus"))
  DragTo (Cells)[2] $p
  WaitUntil { Tile 'Perfect Skull' } "the skull in the inventory" 5000 | Out-Null

  ClickEl (ById 'Bookmark_Vault_Right') "the vault, on the right page"
  WaitUntil { VaultTile 'Tome of Town Portal' } "the vault's cards" | Out-Null
  MenuOf (Tile 'Stone Barb') 'Stone Barb'
  MenuPick 'Store in vault'
  WaitUntil { VaultTile 'Stone Barb' } "Stone Barb in the vault" 6000 | Out-Null
  MenuOf (Tile 'Perfect Skull') 'the skull'
  MenuPick 'Store in vault'
  WaitUntil { VaultTile 'Perfect Skull' } "the skull in the vault" 6000 | Out-Null

  DragTo (VaultTile 'Perfect Skull') (VaultTile 'Stone Barb')
  Check (Says 'Perfect Skull socketed into Stone Barb.') "a gem's card dragged onto a card with an empty socket: 'Perfect Skull socketed into Stone Barb.'"
  Check ([bool](WaitUntil { if (-not (VaultTile 'Perfect Skull')) { 'gone' } } "the skull's card gone" 5000)) "the gem's card is gone: it is in the item"
  Check ($null -ne (VaultTile 'Stone Barb')) "and the item's card is there"
}

Case 'vault-sockets.equip-stands-on-the-tools-line' -Tags vault, items -Changes {
  $ring = 'Steel Ring of Skill'
  Pane 0 'Personal Stash'
  ClickEl (ById 'Bookmark_Vault_Right') "the vault, on the right page"
  WaitUntil { VaultTile 'Tome of Town Portal' } "the vault's cards" | Out-Null

  # What nobody wears: dimmed, and saying why.
  ClickEl (VaultTile 'Tome of Town Portal') "the tome"
  $equip = WaitUntil { ById 'Vault_Equip' } "the vault's Equip"
  Check (-not $equip.Current.IsEnabled -and $equip.Current.HelpText -like 'Equip selected item*cannot be equipped.') "a tome selected: Equip is dimmed and says why ('$($equip.Current.HelpText -replace "`r?`n", ' / ')')"
  Check ($null -ne (ById 'Vault_EquipMerc')) "Equip on mercenary stands beside it"

  # A ring: stored, selected in the vault, and put on by the button.
  MenuOf (WaitUntil { Tile $ring } "the ring in the stash") 'the ring'
  MenuPick 'Store in vault'
  ClickEl (WaitUntil { VaultTile $ring } "the ring in the vault" 6000) "the ring's card"
  $live = WaitUntil { $e = ById 'Vault_Equip'; if ($e -and $e.Current.IsEnabled) { $e } } "Equip, live" 4000
  ClickEl $live "Equip"
  Check ([bool](WaitUntil { if (-not (VaultTile $ring) -and (Tile $ring)) { 'worn' } } "the ring out of the vault" 6000)) "the vault's Equip: the ring has left the vault for the character"
  Pane 0 'Inventory'
}
