# The Trade page's Sell tab follows the inventory's selection, as only a window shows it (the
# page only notes a choice made while it is hidden, and takes it up when shown): that the pages'
# coming and going in the window tells the Trade page so - the item chosen on the Edit page is on sale on opening
# Trade, a choice made with Trade open shows at once, one made on Edit again is there on coming back - and that
# the sale's button opens the currency panel with the tab's total on it.
#
# What the page decides once told it is shown or hidden - which choice is on sale, nothing with none or several
# selected, the panel's total - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Trade/SellFollowTests.
#
# Nothing is changed, and nothing is sold.

# The sale's item name, on the right page - not the inventory's tile of the same name. The sale names an item
# as its tooltip does, its sockets after the name ("Stone Barb [4]"): with or without them.
function OnSale([string]$name) {
  $middle = (BookMiddle)
  foreach ($said in @($name) + (1..6 | ForEach-Object { "$name [$_]" })) {
    $found = @((All (Cond $AE::NameProperty $said)) | Where-Object {
        $_.Current.AutomationId -ne 'Tile' -and -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt $middle })[0]
    if ($found) { return $found }
  }
}
# The Buy tab's link box (trade-vault.ps1): on screen only while the page is on Buy.
function LinkBox { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { $_.Current.HelpText -eq 'Paste a PD2 trade listing link' -and -not $_.Current.IsOffscreen })[0] }
function Choose([string]$name) {
  ClickUntil { ClickEl (WaitUntil { if (Tiles $name) { Tile $name } } "the $name tile") $name } { OnEdit "$name" } "$name selected" | Out-Null
}

Case 'sell-follow.sale-follows-the-item-chosen' -Tags trade, selection {
  ClickEl (WaitUntil { ById 'Bookmark_Edit' } "Edit") "Edit"

  Choose 'Stone Barb'
  ClickEl (ById 'Bookmark_Trade') "Trade"
  ClickEl (WaitUntil { ByName 'SELL' } "the Sell tab") "SELL"
  Check ([bool](WaitUntil { OnSale 'Stone Barb' } "Stone Barb on sale" 3000)) "chosen on Edit, on sale on opening Trade"

  # With Trade open the Edit page's heading is hidden: the sale itself says the click took.
  ClickUntil { ClickEl (WaitUntil { if (Tiles 'Annihilus') { Tile 'Annihilus' } } "the Annihilus tile") 'Annihilus' } { OnSale 'Annihilus' } "Annihilus on sale" | Out-Null
  Check ([bool](OnSale 'Annihilus')) "chosen with Trade open, on sale at once"

  ClickEl (ById 'Bookmark_Edit') "Edit"
  Choose "Duriel's Shell"
  ClickEl (ById 'Bookmark_Trade') "Trade"
  Check ([bool](WaitUntil { OnSale "Duriel's Shell" } "Duriel's Shell on sale" 3000)) "chosen on Edit again, on sale on coming back"
  Check (-not (OnSale 'Annihilus')) "the earlier choice gone"

  # The Buy tab again, as the page was found: the cases that share this start look for its link box.
  ClickUntil { ClickEl (ByName 'BUY') "the Buy tab" } { LinkBox } "the Buy tab, its link box back" 800 | Out-Null
}

Case 'sell-follow.sales-button-opens-the-currency-panel-with-the-tabs-total' -Tags trade {
  ClickEl (WaitUntil { ById 'Bookmark_Edit' } "Edit") "Edit"
  Choose "Duriel's Shell"
  ClickEl (ById 'Bookmark_Trade') "Trade"
  ClickEl (WaitUntil { ByName 'SELL' } "the Sell tab") "SELL"
  WaitUntil { OnSale "Duriel's Shell" } "Duriel's Shell on sale" 3000 | Out-Null

  # The currency panel, opened by the sale's button, says what the materials tab holds in all.
  # Nothing is sold.
  ClickEl (WaitUntil { ByTip 'Sell' } "the sale's button") "Sell"
  $holding = WaitUntil { ByName 'Materials:' -Prefix } "the currency panel's total" 4000
  Check ($holding.Current.Name -match '^Materials: [\d.]+ HR$') "the currency panel totals the tab: '$($holding.Current.Name)'"

  # The panel shut (Esc closes the picker on top) and the Buy tab again, as the page was found.
  PressEsc
  ClickUntil { ClickEl (ByName 'BUY') "the Buy tab" } { LinkBox } "the Buy tab, its link box back" 800 | Out-Null
}
