# The Items page's list of pages, as only a window shows it: that the rows stand left of the grids and a click on one fills the first pane,
# Ctrl + click the second - the two rows and the two panes' lines then saying I and II, the row's tooltip what
# Ctrl + click does - and a right click opens the row's menu, which does either;
# that on a narrow page the list gives way to the grid and says its names short, the
# whole name in the tooltip; that an item dragged onto a row goes to that page without the page being shown;
# and that the shared stash's gold stands in the section's header, set behind the button beside it.
#
# What a click, Ctrl + click and the menu's rows decide, the short names, which page an item let go on a row lands on and
# where, and how the list is given its width, are proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Inventory/PagesListTests.

function OnePane { if ((PaneCount) -gt 1) { ToggleSecondPane } }
# The I or II a row wears while two panes are up; nothing with one.
function Mark([string]$name) {
  $mark = (PageRow $name).FindFirst($TS::Descendants, (Cond $AE::AutomationIdProperty 'PageRow_Pane'))
  if ($mark) { $mark.Current.Name } else { '' }
}
# A row's short name, drawn only while the list is squeezed.
function Short([string]$name) {
  $short = (PageRow $name).FindFirst($TS::Descendants, (Cond $AE::AutomationIdProperty 'PageRow_Short'))
  if ($short -and -not $short.Current.IsOffscreen) { $short.Current.Name }
}
function Right($el) { $b = $el.Current.BoundingRectangle; $b.X + $b.Width }

Case 'pages-list.rows-fill-the-two-panes' -Tags pages-list, items {
  OnePane
  Pane 0 'Personal Stash'
  Check ((PaneLine 0).Current.Name -eq 'Personal Stash') "a click on a row: the first pane shows that page, its line saying so ('$((PaneLine 0).Current.Name)')"
  Check ((Mark 'Personal Stash') -eq '' -and $null -eq (ById 'Inv_Swap')) "one pane up: no row wears a number, and there is no Swap"
  $row = PageRow 'Personal Stash'; $line = PaneLine 0
  Check ((Right $row) -le $line.Current.BoundingRectangle.X + 2) "the list stands left of the pane ($([int](Right $row)) against $([int]$line.Current.BoundingRectangle.X))"

  Hover (PageRow 'Cube')
  $tip = (TipTexts) -join ' | '
  Check ($tip -like '*Show in pane II*' -and $tip -notlike '*Close*') "a row's tooltip says what Ctrl + click does ('$tip')"

  # A right click: the row's menu, which shows the page in either pane; nothing is shown by the click itself.
  MenuOf (PageRow 'Cube') "the Cube's row"
  Check ((MenuWords) -like 'Show in pane I; Show in pane II*' -and (PaneCount) -eq 1) "a right click opens the row's menu, and shows nothing yet ('$(MenuWords)')"
  MenuPick 'Show in pane II'
  WaitUntil { if ((PaneCount) -eq 2) { 'two' } } "two panes" 4000 | Out-Null
  Check ((PaneShows 1) -eq 'Cube') "its 'Show in pane II': the second pane shows the Cube"
  MenuOf (PageRow 'Cube') "the Cube's row, shown"
  Check ((MenuWords) -like 'Show in pane I; Close pane II*') "shown there, the row says it closes it ('$(MenuWords)')"
  MenuPick 'Close pane II'
  WaitUntil { if ((PaneCount) -eq 1) { 'one' } } "one pane" 4000 | Out-Null

  CtrlClickEl (PageRow 'Cube') "the Cube's row"
  WaitUntil { if ((PaneCount) -eq 2) { 'two' } } "two panes" 4000 | Out-Null
  Step "the second pane opened"
  $dot = [char]0xB7
  Check ((PaneLine 0).Current.Name -eq "I $dot Personal Stash" -and (PaneLine 1).Current.Name -eq "II $dot Cube") "Ctrl + click: the second pane, the lines numbered ('$((PaneLine 0).Current.Name)', '$((PaneLine 1).Current.Name)')"
  Check ((Mark 'Personal Stash') -eq 'I' -and (Mark 'Cube') -eq 'II' -and (Mark 'Inventory') -eq '') "and the two rows ('$(Mark 'Personal Stash')', '$(Mark 'Cube')'; the inventory's '$(Mark 'Inventory')')"
  Check ($null -ne (ById 'Inv_Swap')) "Swap is there with the second pane"

  Hover (PageRow 'Cube')
  $tip = (TipTexts) -join ' | '
  Check ($tip -like '*Close pane II*') "on the second pane's own row the tooltip says Ctrl + click shuts it ('$tip')"

  CtrlClickEl (PageRow 'Cube') "the Cube's row again"
  WaitUntil { if ((PaneCount) -eq 1) { 'one' } } "one pane" 4000 | Out-Null
  Check ((PaneLine 0).Current.Name -eq 'Personal Stash' -and (Mark 'Personal Stash') -eq '') "shut: one pane, its line and its row plain again"
  Pane 0 'Inventory'
}

Case 'pages-list.narrow-page-says-the-names-short' -Tags pages-list, items {
  OnePane
  Pane 0 'Personal Stash'
  Check ($null -eq (Short 'Shared Page 1')) "wide: the rows say their whole names"
  try {
    Size 900 600
    $short = $null
    try { $short = WaitUntil { Short 'Shared Page 1' } "the short names" 4000 } catch { }
    Check ($short -eq 'SP 1' -and (Short 'Personal Stash') -eq 'PStash' -and (Short 'Materials') -eq 'Mat') "at the window's least the list is squeezed and says its names short ('$short', '$(Short 'Personal Stash')', '$(Short 'Materials')')"

    $script:r = $script:win.Current.BoundingRectangle
    $row = (PageRow 'Shared Page 1').Current.BoundingRectangle; $line = (PaneLine 0).Current.BoundingRectangle
    Check ($row.Width -ge 60 -and $row.X -ge $script:r.X) "the list keeps a least width ($([int]$row.Width) px)"
    $tile = @((All (Cond $AE::AutomationIdProperty 'Tile')) | Where-Object { -not $_.Current.IsOffscreen } | Sort-Object { $_.Current.BoundingRectangle.X } -Descending)[0]
    Check ($null -ne $tile -and $line.X -ge $row.X + $row.Width - 2) "and the grid is whole beside it"

    # Brought into view first: at the window's least the Items page scrolls, and since Advanced Stats stands under
    # Equipped the row is below the fold - a hover at it was a hover at nothing.
    try { (PageRow 'Shared Page 1').GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView(); Start-Sleep -Milliseconds 300 } catch { }
    Hover (PageRow 'Shared Page 1')
    $tip = (TipTexts) -join ' | '
    Check ($tip -like '*Shared Page 1*' -and $tip -like '*Show in pane II*') "a short row's tooltip says its whole name over the right button's line ('$tip')"
  }
  finally {
    try { Wide } catch { }
  }
  WaitUntil { if ($null -eq (Short 'Shared Page 1')) { 'whole' } } "the whole names again" 4000 | Out-Null
  Check ($true) "wide again: the whole names are back"
  Pane 0 'Inventory'
}

Case 'pages-list.item-dragged-onto-a-row-goes-to-that-page' -Tags pages-list, items -Changes {
  OnePane
  Pane 0 'Personal Stash'
  $ring = WaitUntil { Tile 'Steel Ring of Skill' } "the ring in the stash"
  $rings = @(Tiles 'Steel Ring of Skill').Count

  DragTo $ring (PageRow 'Shared Page 5') -Hold 700
  WaitUntil { if (@(Tiles 'Steel Ring of Skill').Count -eq $rings - 1) { 'gone' } } "the ring gone from the stash" 5000 | Out-Null
  Check ((PaneShows 0) -eq 'Personal Stash' -and (PaneCount) -eq 1) "let go on a row: the item left its page, and the pane still shows it"

  Pane 0 'Shared Page 5'
  $there = $null
  try { $there = WaitUntil { Tile 'Steel Ring of Skill' } "the ring on Shared Page 5" 4000 } catch { }
  Check ($null -ne $there) "and lies on the page whose row it was let go on"
  Pane 0 'Inventory'
}

Case 'pages-list.gold-stands-in-the-header' -Tags pages-list, items, gold {
  OnePane
  Pane 0 'Inventory'
  $said = ById 'Inv_GoldText'; $button = ById 'Inv_Gold'
  Check ($null -ne $said -and $said.Current.Name -match '^[\d,.\s]+ gold$') "the shared stash's gold is said in the section's header ('$($said.Current.Name)')"
  Check ($null -ne $button -and $button.Current.HelpText -eq 'Shared gold' -and $button.Current.BoundingRectangle.X -ge (Right $said) - 2) "with its button beside it ('$($button.Current.HelpText)')"
  $names = @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name })
  Check (-not ($names -contains 'Inventory gold') -and -not ($names -contains 'Stash gold')) "and no purse of the character's own under a pane"

  $box = ClickUntil { ClickEl (ById 'Inv_Gold') "the gold's button" } { ById 'Inv_GoldBox' } "the gold's panel" 800
  Check ($null -ne $box) "the button opens the gold's panel: its amount to type"
  Check ($null -ne (ById 'Inv_GoldToVault') -and $null -ne (ById 'Inv_GoldFromVault')) "and the vault's two arrows"
  PressEsc
  WaitUntil { -not (ById 'Inv_GoldBox') } "the panel shut" 3000 | Out-Null
}
