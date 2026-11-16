# The vault's bin deletes on its second click once the vault has been open on both pages (nothing in the vault
# could be deleted). The fault lay in the window: both vault views are kept once shown, and a
# press reached the one kept behind and took the bin's arming back. So: the vault opened on the right, then on the
# left, a card selected, the bin clicked and its red tick clicked - the row is gone from the file; then the same
# from the right page.
#
# What each page's bin decides - its own arming, its own selection, what it says - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Vault/VaultDeleteTests (and VaultInventoryParityTests for its words).

# A vault item: its tile on the folder's pages.
function Card($name) { VaultTile $name }

# The bin on that page: the inventory's and the vault's say the same, and the inventory's
# stands on the left while the vault is on the right.
function BinOn([string]$where) {
  foreach ($b in (All (Cond $AE::HelpTextProperty 'Delete selected item (Del)'))) {
    if ($b.Current.IsOffscreen) { continue }
    $right = $b.Current.BoundingRectangle.X -gt (BookMiddle)
    if ($right -eq ($where -eq 'right page')) { return $b }
  }
}

# Select a card, press the bin, press its red tick; the vault file loses one row.
function DeleteCard([string]$name, [string]$where) {
  $count = VaultCount
  ClickUntil { if ($c = Card $name) { ClickEl $c $name } } { $b = BinOn $where; if ($b -and $b.Current.IsEnabled) { $b } } "$name selected, the bin enabled ($where)" 800 | Out-Null
  ClickEl (BinOn $where) "the bin ($where)"
  $tick = WaitUntil { ByTip 'Confirm deletion' -Prefix } "the bin armed ($where)" 3000
  ClickEl $tick "confirm ($where)"
  $gone = $false
  try { WaitUntil { (VaultCount) -eq $count - 1 } "one row gone ($where)" 4000 | Out-Null; $gone = $true } catch { }
  Check $gone "$where`: $name deleted on the second click ($count -> $(VaultCount))"
}

Case 'vault-delete.bin-deletes-on-its-second-click-with-both-views-built' -Tags vault -Changes {
  # The vault on the right first, so that view exists; then on the left, which takes the list over.
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Right' } "vault bookmark (right)") "vault (right)"
  WaitUntil { VaultPages } "the vault on the right" | Out-Null
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { $p = VaultPages; if ($p -and $p.Current.BoundingRectangle.X -lt (BookMiddle)) { $p } } "the vault on the left" | Out-Null
  # The vault is on both pages now: the right page is turned to the grail, so the left is
  # the one vault page.
  ClickEl (ById 'Bookmark_Grail') "grail (right)"
  WaitUntil { @((All (Cond $AE::AutomationIdProperty 'Vault_Pages')) | Where-Object { -not $_.Current.IsOffscreen }).Count -eq 1 } "the vault on the left page only" | Out-Null
  VaultGoTo 'Vault'
  DeleteCard 'Tome of Town Portal' 'left page'

  # And from the right page, the left one now the view kept behind.
  ClickEl (ById 'Bookmark_Inventory') "Inventory"
  $right = WaitUntil { $b = ById 'Bookmark_Vault_Right'; if ($b -and $b.Current.IsEnabled) { $b } } "the right vault offered"
  ClickEl $right "vault (right)"
  WaitUntil { $p = VaultPages; if ($p -and $p.Current.BoundingRectangle.X -gt (BookMiddle)) { $p } } "the vault on the right again" | Out-Null
  VaultGoTo 'Small'; WaitUntil { Card 'Small Charm of Fortune' } "Small opened" | Out-Null
  DeleteCard 'Small Charm of Fortune' 'right page'
}
