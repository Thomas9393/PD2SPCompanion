# New is not select, as only a window shows it: with the Forge up, that a click of the mouse
# on an item of the left page - a Ctrl + click, a vault card too - reaches the rule after the selection it made,
# so the right page goes to Edit for it; that the Forge's bookmark then says what it holds; and that a drag, which
# selects what it carries, is told from a click when the button is let go, so the Forge stays.
#
# What the rule decides - taking the Forge clears the selection, the Forge keeps what it holds and its bookmark's
# words, several picked, a Forge holding nothing, Trade staying beside a selection, a vault folder being no item,
# what FORGE makes landing unselected - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Forge/ForgeSelectTests.
#
# The Ctrl + click case picks the Annihilus and the Horadric Cube, which the inventory lock keeps out of a pick:
# it stands on the developer's settings having that lock off, as the script it came from did.

function RightEdit { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt (BookMiddle) })[0] }
function Find([string]$text) { (RightEdit).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); Start-Sleep -Milliseconds 400 }
# A find of the Forge's search, by its name: the button its text sits in.
function Found([string]$name) {
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  foreach ($text in (All (Cond $AE::NameProperty $name))) {
    if ($text.Current.IsOffscreen) { continue }
    $parent = $walker.GetParent($text)
    if ($parent -and $parent.Current.ControlType -eq $CT::Button -and $walker.GetFirstChild($parent).Current.Name -eq $name) { return $parent }
  }
}
# A vault item: its tile on the folder's pages.
function Card($name) { VaultTile $name }
# The right page: the Forge (its FORGE button), or Edit showing an item.
function OnForge { $null -ne (ById 'Forge_Make') }
# What the Forge's bookmark says of itself - its name, and what it holds - without the line of keys every right-page
# bookmark carries ("Ctrl + Tab: Next bookmark").
function ForgeTip { (((ById 'Bookmark_Forge').Current.HelpText -replace '\s+', ' ') -replace '\s*Ctrl \+ Tab: Next bookmark$', '') }
function CtrlClick($el) { [Fast]::keybd_event(0x11, 0, 0, 0); try { ClickEl $el "Ctrl+click" } finally { [Fast]::keybd_event(0x11, 0, 2, 0) }; Start-Sleep -Milliseconds 250 }

# The Forge taken, holding War Traveler: a find chosen is made afresh, whatever the holder had.
function ForgeHoldingBoots {
  ClickEl (WaitUntil { ById 'Bookmark_Forge' } "the Forge bookmark") "Forge"
  WaitUntil { OnForge } "the Forge" | Out-Null
  Find 'war trav'
  InvokeEl (WaitUntil { Found 'War Traveler' } "War Traveler among the finds") "War Traveler"
  WaitUntil { OnEdit 'War Traveler' } "War Traveler in the holder" | Out-Null
}

Case 'forge-select.item-clicked-leaves-the-forge-for-edit' -Tags forge, select {
  ForgeHoldingBoots
  Check ((ForgeTip) -eq 'Forge') "on the Forge its bookmark says only 'Forge' ('$(ForgeTip)')"
  ClickEl (Tile 'Annihilus') 'Annihilus'
  Check ($null -ne (WaitUntil { if (-not (OnForge)) { OnEdit 'Annihilus' } } "Edit for Annihilus" 4000)) "an item clicked with the Forge up: the right page is Edit, showing it"
  Check ((ForgeTip) -eq 'Forge Holding War Traveler') "the Forge's bookmark is marked: '$(ForgeTip)'"
  ClickEl (ById 'Bookmark_Forge') "Forge"
  Check ($null -ne (WaitUntil { if (OnForge) { OnEdit 'War Traveler' } } "the Forge, still holding" 4000)) "back on the Forge: it still holds War Traveler"
  Check ((ForgeTip) -eq 'Forge') "and the mark is gone ('$(ForgeTip)')"
}

Case 'forge-select.ctrl-click-leaves-the-forge-too' -Tags forge, select, keys {
  ForgeHoldingBoots
  CtrlClick (Tile 'Annihilus'); CtrlClick (Tile 'Horadric Cube')
  Check ($null -ne (WaitUntil { -not (OnForge) } "the Forge left" 4000)) "two items picked with the Forge up: the Forge is left"
}

Case 'forge-select.vault-card-clicked-leaves-the-forge' -Tags forge, select, vault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { VaultPages } "the vault on the left" | Out-Null
  # The vault's top, where the tome lies, whatever folder a case before left it in.
  VaultGoTo 'Vault'
  ClickEl (ById 'Bookmark_Forge') "Forge"
  WaitUntil { OnForge } "the Forge beside the vault" | Out-Null
  ClickEl (WaitUntil { Card 'Tome of Town Portal' } "the tome's card") "a vault card"
  Check ($null -ne (WaitUntil { if (-not (OnForge)) { OnEdit 'Tome of Town Portal' } } "Edit for the card" 4000)) "a vault card clicked with the Forge up: Edit, showing it"
}

# The boots are forged first, so there is an item to drag whose place is known: the save is changed.
Case 'forge-select.drag-moves-and-the-forge-stays' -Tags forge, select, drag -Changes {
  ForgeHoldingBoots
  ClickEl (ById 'Forge_Make') "FORGE"
  WaitUntil { ByName 'War Traveler forged to inventory.' } "forged" 5000 | Out-Null
  Start-Sleep -Milliseconds 600
  Check ((OnForge) -and $null -ne (OnEdit 'War Traveler')) "FORGE lands the item unselected: the Forge stays, the holder as it was"

  # A drag moves and does not select: the Forge stays. The boots (2x2), three cells left and two down, where the
  # pad's inventory is free - by their own centre, so they land whole cells away.
  $boots = WaitUntil { Tile 'War Traveler' } "the forged boots on the left"
  $at = $boots.Current.BoundingRectangle
  # A quarter of a cell inside the cell aimed at: the boots' own centre is a corner of four cells, and three
  # whole cells from it is one again - a pixel either way chose the cell, and the charm beside was in the way
  # (the tiles' rectangles come from the layer that draws them).
  $cell = $at.Width / 2
  $to = @([int]($at.X + $at.Width / 2 - 3 * $cell - $cell / 4 - $script:r.X), [int]($at.Y + $at.Height / 2 + 2 * $cell - $cell / 4 - $script:r.Y))
  DragTo $boots $to -Hold 700
  Start-Sleep -Milliseconds 800
  $moved = WaitUntil { Tile 'War Traveler' } "the boots after the drag"
  Check ($moved.Current.BoundingRectangle.X -ne $at.X -or $moved.Current.BoundingRectangle.Y -ne $at.Y) "the boots dragged to another cell"
  Check (OnForge) "a drag is not a selection: the Forge stays"
  ClickEl (ById 'Bookmark_Edit') "Edit"
  Start-Sleep -Milliseconds 500
  $shown = ById 'Item_Name'
  Check ($null -eq $shown -or $shown.Current.Name -ne 'War Traveler') "and the dragged item was let go of: Edit shows no War Traveler"
}
