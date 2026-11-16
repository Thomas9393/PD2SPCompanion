# A vault card dragged onto a shared stash page and back, as only a window shows it (an item
# stored from the shared stash by drag could not be dragged back): that a drag of the mouse from the vault's sheet
# reaches a cell of the second pane, and a drag from that cell reaches the sheet.
#
# What the two pages do once told where the drag was let go of - withdrawn there with its id kept, stored again,
# what the strip says - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultDragSharedTests.
#
# The vault on the right page; on the left, the second pane on Shared Page 8, as the user has it ("turn on the
# second pane, and my bug is mostly there"). Casual, as the script this came from ran.

$saves = "$pad\saves"
$tomeId = 2218325626
function Stamp($file) { (WrittenAt "$saves\$file") }
function InStash { (& $script:Cli dump "$saves\pd2_shared.stash" 2>&1 | Out-String) -match "id $tomeId\b" }
# A vault item: its tile on the folder's pages.
function Card($name) { VaultTile $name }

# One case for the two drags: the second begins where the first let go.
Case 'vault-drag-shared.card-dragged-to-a-shared-page-and-back' -Tags vault, drag -Mode Casual -Changes {
  $count = VaultCount
  Check (-not (InStash)) "start: the tome is not in the shared stash, the vault holds $count"

  WaitUntil { ItemsPage } "items page" | Out-Null
  Pane 1 'Shared Page 8'
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Right' } "vault bookmark (right)") "vault (right)"
  WaitUntil { VaultPages } "the vault on the right" | Out-Null
  VaultGoTo 'Vault'
  WaitUntil { Card 'Tome of Town Portal' } "the tome's card" | Out-Null
  # An empty cell low on Shared Page 8, in the second pane: its fourth across, twelfth down.
  $cell = PaneCell 1 3 11 (CellPx (Card 'Tome of Town Portal'))

  # vault -> shared stash
  $was = Stamp 'pd2_shared.stash'
  ClickUntil { if ($c = Card 'Tome of Town Portal') { DragTo $c $cell } } { (Stamp 'pd2_shared.stash') -ne $was } "withdrawn onto the shared stash, and written" 1500 | Out-Null
  WaitUntil { (VaultCount) -eq $count - 1 } "the row gone from the vault" 10000 | Out-Null
  Check (InStash) "the tome is in the shared stash, its id kept"

  # shared stash -> vault
  $was = Stamp 'pd2_shared.stash'
  DragTo $cell (VaultPages)
  WaitUntil { (Stamp 'pd2_shared.stash') -ne $was } "shared stash written" 10000 | Out-Null
  WaitUntil { (VaultCount) -eq $count } "the tome back in the vault" 10000 | Out-Null
  Check (-not (InStash)) "the tome has left the shared stash"
}
