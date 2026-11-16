# An item sent to the other pane, as only a window shows it (by its menu's row):
# where Swap stands now that the arrows between the panes are gone, that an item's menu has the
# row while there is a pane across and not without, and that the row sends a vault card with the vault on both
# pages, the card then in sight on the other page.
#
# What the row decides - one item, back again, several picked together, each refusal's words - is proven
# without a window: tests/PD2SPCompanion.Shell.Tests/Inventory/SendAcrossTests.
#
# On the pad's PhySpearZon: a Stalagmite and a Steel Ring of Skill in the personal stash, a Small Charm of
# Fortune in the vault's Charms\Small, one item in Charms\Grand.

Add-Type -AssemblyName System.Windows.Forms
function Esc { Front; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300 }
function Strip([string]$like, [int]$ms = 4000) {
  try { [bool](WaitUntil { foreach ($t in (All (Cond $AE::ControlTypeProperty $CT::Text))) { if (-not $t.Current.IsOffscreen -and $t.Current.Name -like $like) { return $t } } } "the strip: $like" $ms) } catch { $false }
}
# The lines of the panes shown, left to right.
function Pickers { @(@((PaneLine 0), (PaneLine 1)) | Where-Object { $_ }) }
# One pane or two, whichever the case before left: Ctrl + click on a row opens the second, and on its own row shuts it.
function PanesShown([int]$count) {
  if (@(Pickers).Count -ne $count) {
    ToggleSecondPane
    WaitUntil { if (@(Pickers).Count -eq $count) { 'so' } } "$count pane(s) shown" 4000 | Out-Null
    Start-Sleep -Milliseconds 500
  }
}
# The vault page's parts by the page of the book they are on.
function Half($el) { PageOf $el }
function On([string]$side, [string]$id, $name = $null) {
  Steady {
    foreach ($e in (All (Cond $AE::AutomationIdProperty $id))) {
      if (-not $e.Current.IsOffscreen -and (Half $e) -eq $side -and (-not $name -or $e.Current.Name -eq $name)) { return $e }
    }
  }
}
function Count([string]$side) { $t = On $side 'Vault_Count'; if ($t) { $t.Current.Name } }
function TileOn([string]$side, $name) { @(Tiles $name | Where-Object { (Half $_) -eq $side })[0] }
function Sheets { @((All (Cond $AE::AutomationIdProperty 'Vault_Pages')) | Where-Object { -not $_.Current.IsOffscreen }).Count }

Case 'send-across.swap-stands-in-the-second-panes-header' -Tags send-across, items {
  Pane 0 'Personal Stash'
  PanesShown 2
  Pane 1 'Shared Page 5'
  Start-Sleep -Milliseconds 500
  $swap = ById 'Inv_Swap'; $gold = ById 'Inv_Gold'
  Check ($null -ne $swap -and $null -ne $gold -and $swap.Current.BoundingRectangle.X -gt $gold.Current.BoundingRectangle.X -and [Math]::Abs($swap.Current.BoundingRectangle.Y - $gold.Current.BoundingRectangle.Y) -lt 3) "Swap is in the section's header, after the gold's button"
  # As the next case expects the page: the inventory in the one pane shown.
  PanesShown 1
  Check ($null -eq (ById 'Inv_Swap')) "and gone with the second pane"
  Pane 0 'Inventory'
}

Case 'send-across.menu-has-the-row-with-a-pane-across' -Tags send-across, items, menu {
  $ring = 'Steel Ring of Skill'
  PanesShown 1
  Pane 0 'Personal Stash'
  Start-Sleep -Milliseconds 500
  MenuOf (WaitUntil { Tile 'Stalagmite' } "the Stalagmite") 'the Stalagmite'
  Check ((MenuWords) -notlike '*To other pane*') "one pane: the menu has no 'To other pane' ('$(MenuWords)')"
  Esc

  PanesShown 2
  Pane 1 'Shared Page 5'
  Start-Sleep -Milliseconds 500
  MenuOf (Tile $ring) 'the ring'
  Check ((MenuWords) -like '*To other pane*') "two panes: the menu has 'To other pane' ('$(MenuWords)')"
  MenuPick 'To other pane'
  Check ([bool](WaitUntil { if ((PaneShows 1) -eq 'Shared Page 5' -and (Tile $ring)) { 'sent' } } "the ring across" 4000)) "and the row sends the ring across"
  MenuOf (WaitUntil { Tile $ring } "the ring, across") 'the ring, across'
  MenuPick 'To other pane'
  Start-Sleep -Milliseconds 500
  PanesShown 1
  Pane 0 'Inventory'
}

Case 'send-across.vault-card-goes-to-the-other-vault-page' -Tags send-across, vault -Changes {
  $charm = 'Small Charm of Fortune'
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Left' } "vault bookmark (left)") "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  VaultGoTo 'Small'
  WaitUntil { VaultTile $charm } "the charm" | Out-Null

  ClickEl (On 'Left' 'Vault_Compare') "compare"
  WaitUntil { if ((Sheets) -eq 2) { 'both' } } "the vault on both pages" 5000 | Out-Null
  Start-Sleep -Milliseconds 500
  VaultUnfold | Out-Null
  ClickEl (WaitUntil { On 'Right' 'FolderRow' 'Grand' } "the right tree's Grand") "Grand (right)"
  WaitUntil { if ((Count 'Right') -eq '1 item') { 'there' } } "Grand on the right" 3000 | Out-Null

  MenuOf (TileOn 'Left' $charm) 'the charm'
  Check ((MenuWords) -like '*To other page*') "the card's menu has 'To other page' ('$(MenuWords)')"
  MenuPick 'To other page'
  Check ([bool](WaitUntil { if ((Count 'Left') -eq '1 item' -and (Count 'Right') -eq '2 items') { 'moved' } } "the charm in Grand" 4000)) "sent from the left page: it is in the right page's folder ('$(Count 'Left')', '$(Count 'Right')')"
  Check ($null -ne (TileOn 'Right' $charm)) "and in sight on the right page"
  Check (Strip 'Moved to Grand.') "said: 'Moved to Grand.'"
}
