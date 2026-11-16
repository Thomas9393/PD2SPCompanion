# The clipboard of items and folders, as only a window shows it: that Ctrl + X, Ctrl + C and Ctrl + V reach the page the pointer was last
# pressed on, and Esc lets a cut go; that the three stand on both pages' tools lines, Paste saying what is held;
# that a cut on the character is pasted into the vault's folder and a cut in the vault onto the character; that a
# folder cut from its menu is pasted into another by that one's; and that there is no Copy in Legit, which makes
# no copies.
#
# What the pages decide - what a paste moves and where, the refusals' words, a copy's own id, what is held and
# for how long - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Items/ItemClipboardTests and
# Vault/VaultCopyTests.
#
# On the pad's PhySpearZon: an Annihilus in its inventory, a Tome of Town Portal at the vault's top, the folders
# Charms and Jewels.
Add-Type -AssemblyName System.Windows.Forms

function Listing { & $script:Cli vault --file (OpenVaultFile) --list 2>&1 | Out-String }
function Tomes { @([regex]::Matches((Listing), 'Tome of Town Portal.*?id (\d+)') | ForEach-Object { $_.Groups[1].Value }) }
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 450 }
function Says([string]$text, [int]$ms = 4000) { try { [bool](WaitUntil { ByName $text } "the strip: $text" $ms) } catch { $false } }
function Hint([string]$id) { (ById $id).Current.HelpText -replace "`r?`n", ' / ' }
# The vault on the left page at its top, its tome selected.
function OpenVault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { VaultPages } "the vault on the left" | Out-Null
  VaultGoTo 'Vault'
  ClickUntil { if ($c = VaultTile 'Tome of Town Portal') { ClickEl $c "the tome" } } { $b = ByTip 'Delete selected item (Del)'; if ($b -and $b.Current.IsEnabled) { $b } } "the tome selected" 800 | Out-Null
}

Case 'clipboard.no-copy-button-in-legit' -Tags clipboard, vault, play-mode -Mode Legit {
  Check ($null -eq (ById 'Inv_Copy') -and $null -ne (ById 'Inv_Cut') -and $null -ne (ById 'Inv_Paste')) "Legit, the Items page: Cut and Paste, no Copy"
  OpenVault
  Check ($null -eq (ById 'Vault_Copy') -and $null -ne (ById 'Vault_Cut') -and $null -ne (ById 'Vault_Paste')) "Legit, the vault page: Cut and Paste, no Copy"
}

# A start of the program to itself (the empty Before): the clipboard is the window's and a copy stays held, so a
# case before this one in a shared program leaves Paste alive.
Case 'clipboard.cut-waits-for-the-paste-and-esc-lets-it-go' -Tags clipboard, items, keys -Changes -Before { } {
  Check (-not (ById 'Inv_Paste').Current.IsEnabled -and (Hint 'Inv_Paste') -eq 'Paste (Ctrl + V)') "nothing held: Paste is dead ('$(Hint 'Inv_Paste')')"
  ClickEl (WaitUntil { Tile 'Annihilus' } "the Annihilus") "the Annihilus"
  Keys '^x'
  Check (Says '1 item cut. Paste to move it.') "Ctrl + X: '1 item cut. Paste to move it.'"
  Check ((ById 'Inv_Paste').Current.IsEnabled -and (Hint 'Inv_Paste') -eq 'Paste (Ctrl + V) / 1 item cut') "Paste says what is held ('$(Hint 'Inv_Paste')')"
  Check ((PaneShows 0) -eq 'Inventory' -and $null -ne (Tile 'Annihilus')) "and nothing has moved"

  Keys '{ESC}'
  Check (-not (ById 'Inv_Paste').Current.IsEnabled -and (ById 'Inv_Cut').Current.IsEnabled) "Esc lets the cut go, and leaves the selection"

  # Cut by the tools' button, another page shown, pasted by the key: there.
  ClickEl (ById 'Inv_Cut') "the tools' Cut"
  Pane 0 'Personal Stash'
  Keys '^v'
  Check (Says 'Moved Annihilus to stash.') "Ctrl + V on another page: 'Moved Annihilus to stash.'"
  Check ($null -ne (Tile 'Annihilus') -and -not (ById 'Inv_Paste').Current.IsEnabled) "it lies there, and the clipboard is empty"
  Pane 0 'Inventory'
}

Case 'clipboard.cut-crosses-between-the-character-and-the-vault' -Tags clipboard, items, vault, keys -Changes {
  ClickEl (ById 'Bookmark_Vault_Right') "the vault, on the right page"
  WaitUntil { VaultTile 'Tome of Town Portal' } "the tome's card" | Out-Null
  $count = VaultCount
  $cell = CellPx (WaitUntil { Tile 'Annihilus' } "the Annihilus")

  # Cut on the character, pasted on the vault page - which a press on it makes the keys' page.
  ClickEl (Tile 'Annihilus') "the Annihilus"
  Keys '^x'
  ClickEl (VaultTile 'Tome of Town Portal') "the tome's card"
  try { WaitUntil { if ((Hint 'Vault_Paste') -like '*cut') { 'held' } } "the cut held" 3000 | Out-Null } catch { }
  Check ((ById 'Vault_Paste').Current.IsEnabled -and (Hint 'Vault_Paste') -eq 'Paste (Ctrl + V) / 1 item cut') "the vault's Paste says what the character's cut holds ('$(Hint 'Vault_Paste')')"
  Keys '^v'
  Check ([bool](WaitUntil { VaultTile 'Annihilus' } "the Annihilus in the vault" 5000)) "pasted on the vault page: stored in the folder shown"

  # And back: cut in the vault, pasted on the character.
  ClickEl (VaultTile 'Annihilus') "the Annihilus's card"
  Keys '^x'
  # A press on the inventory page gives it the keys, and the key lands under the pointer. Over a worn slot that is the
  # slot's (PasteOnSlot), and a charm is not for the hands: so an empty cell, the second row's seventh on the pad.
  $empty = PaneCell 0 6 1 $cell
  ClickAt $empty[0] $empty[1]
  Start-Sleep -Milliseconds 300
  Keys '^v'
  Check ([bool](WaitUntil { if (-not (VaultTile 'Annihilus')) { 'gone' } } "the card gone" 5000)) "pasted on the character: withdrawn from the vault"
  Kept "the withdrawal"
  Check ((VaultCount) -eq $count) "and the vault holds what it held ($count -> $(VaultCount))"
}

Case 'clipboard.folder-cut-from-its-menu-is-pasted-into-another' -Tags clipboard, vault, menu -Changes {
  OpenVault
  MenuOf (VaultRow 'Jewels' -AsIs) 'Jewels'
  Check ((MenuIcons) -eq 'Cut; Paste; Rename; Delete') "a folder's menu is headed by Cut, Paste, Rename, Delete ('$(MenuIcons)')"
  MenuPick 'Cut'
  Check (Says 'Jewels cut. Paste to move it.') "'Jewels cut. Paste to move it.'"
  MenuOf (VaultRow 'Charms' -AsIs) 'Charms'
  MenuPick 'Paste'
  Check (Says 'Moved to Charms.') "pasted by the menu of Charms: 'Moved to Charms.'"
  VaultGoTo 'Charms'
  Check ([bool](WaitUntil { VaultFolderTile 'Jewels' } "the tile Jewels" 3000)) "and Jewels is a folder of Charms"
}

Case 'clipboard.copy-and-paste-make-a-second-under-its-own-id' -Tags clipboard, vault -Changes {
  $count = VaultCount
  Check ((Tomes).Count -eq 1) "start: one Tome of Town Portal in the vault"

  OpenVault
  ClickEl (WaitUntil { ById 'Vault_Copy' } "the vault's Copy (Free)") "Copy"
  # Waited for: the click reaches the tools a moment after it is made.
  try { WaitUntil { if ((Hint 'Vault_Paste') -like '*copied') { 'held' } } "the copy held" 3000 | Out-Null } catch { }
  Check ((Hint 'Vault_Paste') -eq 'Paste (Ctrl + V) / 1 item copied') "Paste says '1 item copied' ('$(Hint 'Vault_Paste')')"
  ClickEl (ById 'Vault_Paste') "Paste"
  $grew = $false
  try { WaitUntil { (VaultCount) -eq $count + 1 } "one row more" 5000 | Out-Null; $grew = $true } catch { }
  Check $grew "Free: the vault holds one more ($count -> $(VaultCount))"
  $tomes = Tomes
  Check ($tomes.Count -eq 2 -and $tomes[0] -ne $tomes[1]) "two tomes, each its own id ($($tomes -join ', '))"
}
