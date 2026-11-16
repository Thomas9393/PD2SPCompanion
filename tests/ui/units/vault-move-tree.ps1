# The vault's Move to folder picker is a tree that folds, as only a window shows it: that a
# folder holding folders has its chevron there to click, and the click puts its folders out of the list and
# brings them back.
#
# What the picker lists and keeps - which folders are listed, a fold kept while the program runs, the folder tree
# following it - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultMoveTreeTests.
#
# Nothing is moved; the pad's vault is not changed.

# A vault item: its tile on the folder's pages.
function Card($name) { VaultTile $name }
function Fold($name) { OnScreen (New-Object System.Windows.Automation.AndCondition((Cond $AE::AutomationIdProperty 'FoldTarget'), (Cond $AE::NameProperty $name))) }
# A row of the picker, by the folder's name: its button (MoveTarget) - the folder tree beside the picker has a
# row of the same name.
function Listed($name) { foreach ($b in (All (Cond $AE::AutomationIdProperty 'MoveTarget'))) { if (-not $b.Current.IsOffscreen -and $b.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $b } } }
function OpenPicker {
  ClickUntil { if ($c = Card 'Tome of Town Portal') { ClickEl $c "the tome" } } { $b = ByTip 'Move to folder'; if ($b -and $b.Current.IsEnabled) { $b } } "the tome selected" 800 | Out-Null
  ClickEl (ByTip 'Move to folder') "Move to folder"
  WaitUntil { Fold 'Charms' } "the picker, Charms with its chevron" | Out-Null
}

Case 'vault-move-tree.chevron-folds-the-pickers-tree' -Tags vault, folders {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { VaultPages } "the vault" | Out-Null
  VaultGoTo 'Vault'

  OpenPicker
  # The tree opens folded, and the picker with it - unless a case before this one unfolded
  # Charms in the same program: the case asks, and leaves the fold as it found it. (It took Charms for open,
  # and passed only after a case that had opened it.)
  $wasOpen = $null -ne (Listed 'Small')
  if (-not $wasOpen) {
    ClickEl (Fold 'Charms') "Charms' chevron, to unfold"
    Check ($null -ne (WaitUntil { Listed 'Small' } "Small listed")) "unfolded: Charms' folder Small is listed under it"
  }

  ClickEl (Fold 'Charms') "Charms' chevron"
  WaitUntil { -not (Listed 'Small') } "Small folded away" | Out-Null
  Check ($null -eq (Listed 'Small')) "folded: Small is gone from the list"
  Check ($null -ne (Listed 'Jewels')) "the folders beside Charms stay"

  ClickEl (Fold 'Charms') "Charms' chevron again"
  Check ($null -ne (WaitUntil { Listed 'Small' } "Small back")) "unfolded: Small is listed again"
  if (-not $wasOpen) { ClickEl (Fold 'Charms') "Charms' chevron, as it was found"; WaitUntil { -not (Listed 'Small') } "Small folded away again" | Out-Null }
  ClickEl (WaitUntil { ByTip 'Close' } "the picker's Close") "Close"
  WaitUntil { -not (Fold 'Charms') } "the picker closed" | Out-Null
}
