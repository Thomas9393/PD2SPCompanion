# A shared stash still in last season's encoding, as only a window shows it: its pages are drawn - it opened - and a vault card dragged with the
# mouse onto one of them is refused on the strip, the card still in the vault; the same drag onto the character -
# this season's - lands.
#
# What the pages decide once a carried card is let go of - the refusal's words, nothing written, the character
# taking the same card - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Season/LastSeasonGuardTests.
#
# The pad's own stash, written again in last season's encoding before the program starts (pd2spc lastseason);
# Casual mode. The program is started as on a new season's first launch, its stamp not yet in the table
# (PD2SPC_STAMPS, Debug builds only): with the stamps known a file reads item by item, and nothing is refused.

function Says([string]$part) {
  foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
    if ($e.Current.Name.Contains($part) -and -not $e.Current.IsOffscreen) { return $e }
  }
}
# A vault item: its tile on the folder's pages.
function Card($name) { VaultTile $name }

Case 'last-season-guard.card-dragged-onto-a-last-season-stash-is-refused' -Tags guards, last-season, vault -Mode Casual -Before {
  & $script:Cli lastseason "$pad\saves\pd2_shared.stash" --out "$pad\saves\pd2_shared.lastseason" | Out-Null
  Move-Item "$pad\saves\pd2_shared.lastseason" "$pad\saves\pd2_shared.stash" -Force
  $env:PD2SPC_STAMPS = 'off'
} {
  $stash = "$pad\saves\pd2_shared.stash"
  $stashBytes = [IO.File]::ReadAllBytes($stash)

  WaitUntil { ItemsPage } "items page" | Out-Null
  Pane 1 'Shared Page 8'
  # The shared stash's gold stands in the section's header only with the stash read: it opened.
  Check ([bool](WaitUntil { ById 'Inv_Gold' } "the shared stash's gold" 3000)) "the last-season stash opened"
  # An empty inventory cell for the control: right of the Horadric Cube's top row, in the first pane.
  Pane 0 'Inventory'
  $inventoryCell = CellOf (WaitUntil { Tile 'Horadric Cube' } "the cube") 2 0 2 2
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Right' } "vault bookmark (right)") "vault (right)"
  WaitUntil { Card 'Tome of Town Portal' } "the tome's card" | Out-Null
  # An empty cell low on Shared Page 8, in the second pane: its fourth across, twelfth down (vault-drag-shared.ps1's).
  $cell = PaneCell 1 3 11 (CellPx (Card 'Tome of Town Portal'))
  Step "vault on the right, last-season Shared Page 8 in the second pane"

  # Vault -> the last-season stash: refused.
  ClickUntil { if ($c = Card 'Tome of Town Portal') { DragTo $c $cell } } { Says "read as last season's" } "the refusal" 1500 | Out-Null
  Check ([bool](Says "Shared stash read as last season's.")) "refused, with the reason"
  Check ([bool](Card 'Tome of Town Portal')) "the card is still in the vault"

  # The control: the same card onto the character, this season's - taken. The character carries a tome of its own, so
  # what is asked is that the card has left the vault.
  ClickUntil { if ($c = Card 'Tome of Town Portal') { DragTo $c $inventoryCell } } { if (-not (Card 'Tome of Town Portal')) { 'taken' } } "the tome on the character" 1500 | Out-Null
  Check (@(Tiles 'Tome of Town Portal').Count -ge 2) "the same card onto this season's character is taken (control)"

  Check ([IO.File]::ReadAllBytes($stash).Length -eq $stashBytes.Length -and -not (Compare-Object ([IO.File]::ReadAllBytes($stash)) $stashBytes)) "the stash file untouched"
}
