# The Sockets section on the vault's Edit page, as only a window shows it (SocketsEditor): that a vault card
# clicked on the left page is the item the Edit page shows, that a socket added there is in the vault when the
# program is started again, and that Shift held on a click of + and - reaches the stepper - the page watches for
# the key from above the buttons, which mark their own press handled.
#
# What the stepper, the box and the die decide, and what is written to the vault's row, is proven without a
# window: tests/PD2SPCompanion.Shell.Tests/Edit/SocketCountTests.
#
# A Crystal Sword with two sockets is put in the pad's vault with the command line tool before the program
# starts, carried by a copy of the character with its stored items cleared.

function SwordIntoVault {
  $carrier = "$pad\sockets-carrier"
  Remove-Item $carrier -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory $carrier | Out-Null
  Copy-Item "$pad\saves\PhySpearZon.d2s" "$carrier\Carrier.d2s"
  & $script:Cli clear "$carrier\Carrier.d2s" | Out-Null
  & $script:Cli give "$carrier\Carrier.d2s" --base "crs:2" --backup "$carrier\bak" | Out-Null
  & $script:Cli vault --file (OpenVaultFile) --add "$carrier\Carrier.d2s" | Out-Null
}
# The sword's card on the vault's Edit page.
function OpenSword {
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Left' } "vault bookmark") "vault bookmark"
  # On the vault's pages.
  WaitUntil { VaultTile 'Crystal Sword' } "the sword's card" | Out-Null
  ClickUntil { ClickEl (VaultTile 'Crystal Sword') "Crystal Sword card" } { ByName 'Code: crs' -Prefix } "the card on the Edit page" | Out-Null
}

Case 'sockets.added-to-a-vault-card-and-read-back' -Tags sockets, vault, edit -Before { SwordIntoVault } {
  OpenSword
  WaitUntil { ByName 'Sockets  (0 / 2)' } "the section: 0 / 2" | Out-Null
  ClickEl (WaitUntil { $b = ByTip 'Add socket' -Prefix; if ($b -and $b.Current.IsEnabled) { $b } } "Add socket") "add socket"
  Check ([bool](WaitUntil { ByName 'Sockets  (0 / 3)' } "the section: 0 / 3")) "a socket added: 'Sockets  (0 / 3)'"
  Kept "the vault written"

  # The program started again on the pad as it now is, and the card opened again.
  $mode = $script:session.Mode
  End-Session
  Start-Session $pad $mode
  Reach 'Items'
  OpenSword
  Check ($null -ne (WaitUntil { ByName 'Sockets  (0 / 3)' } "the section read back")) "read back: the sword has three sockets"
}

Case 'sockets.shift-click-on-the-stepper-goes-to-the-ends' -Tags sockets, vault, edit -Before { SwordIntoVault } {
  OpenSword
  WaitUntil { ByName 'Sockets  (0 / 2)' } "the section: 0 / 2" | Out-Null

  # Shift on the stepper: + to the most the sword takes, - to none. Kept as it is made.
  $add = ByTip 'Add socket' -Prefix
  DragTo $add $add -Modifier Shift
  Check ([bool](WaitUntil { -not (ByTip 'Add socket' -Prefix).Current.IsEnabled } "+ at the most" 4000)) "Shift + click on +: the most sockets, + greyed"
  Check (-not (ByName 'Sockets  (0 / 2)')) "and no longer two"
  $remove = ByTip 'Remove socket' -Prefix
  DragTo $remove $remove -Modifier Shift
  Check ([bool](WaitUntil { -not (ByTip 'Remove socket' -Prefix).Current.IsEnabled } "- at none" 4000)) "Shift + click on -: no sockets, - greyed"
  Check ($null -ne (ByName 'Sockets  (0 / 0)')) "and the section says none: 'Sockets  (0 / 0)'"
  Check ((ByTip 'Add socket' -Prefix).Current.IsEnabled) "+ open again"
}
