# Trading beside the vault, as only a window shows it (virtual trading works with the
# vault, so a user does not feel bounced back to the inventory): that the bookmarks leave Trade and the vault side by
# side, that the link box, Buy, the payment and Pay reach a purchase whose card is then drawn in the folder shown,
# that the warning mark is drawn beside Buy in Legit, that a card clicked in the vault is the item on sale and the
# sale's two buttons reach it, and that the records' button opens the list.
#
# What the pages decide - where the item goes, what the strip and the mark say, the row out and back by Undo, what
# the records list - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Trade/TradeVaultTests.
#
# A listing is opened by its link from the trade cache (no network): the corpus keeps one as the trade site handed
# it out, and the cases that buy put it in the run's own cache before the program starts.

$listingId = '6a92170a9f44c105fc2892b4'
$link = "https://www.projectdiablo2.com/market/listing/$listingId"
# A vault item: its tile on the folder's pages.
function Card($name) { VaultTile $name }
function Put($box, [string]$text) { $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); Start-Sleep -Milliseconds 250 }
function Edits { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.HelpText -ne 'Paste a PD2 trade listing link' -and $_.Current.BoundingRectangle.X -gt (BookMiddle) } | Sort-Object { $_.Current.BoundingRectangle.Y }) }
function LinkBox { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { $_.Current.HelpText -eq 'Paste a PD2 trade listing link' -and -not $_.Current.IsOffscreen })[0] }
# What the left strip last said of a trade: the item's name out of "Bought X to vault." or "Sold X from vault.".
function Said([string]$pattern) {
  foreach ($t in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
    if ($t.Current.Name -match $pattern) { return $Matches[1] }
  }
}
# The buttons wearing a tooltip, lowest on the page first: the payment panel's Pay or Sell stands under the page's own.
function Tipped([string]$tip) { @((All (Cond $AE::HelpTextProperty $tip)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.IsEnabled } | Sort-Object { -$_.Current.BoundingRectangle.Y }) }
# One purchase of the listing at nothing: the link, Buy, an offer of 0, Pay.
function BuyAtNothing([string]$link) {
  Put (LinkBox) ''
  Put (LinkBox) $link
  $buy = WaitUntil { $b = ByTip 'Buy'; if ($b -and $b.Current.IsEnabled) { $b } } "the listing shown, Buy offered" 15000
  ClickEl $buy "Buy"
  WaitUntil { ByTip 'Pick by hand' } "the payment" | Out-Null
  Put (Edits)[-1] '0'
  WaitUntil { ByName 'Paying nothing.' } "paying nothing" | Out-Null
  ClickEl (Tipped 'Pay')[0] "Pay"
}
# The corpus's listing into the run's trade cache, which begins empty: for -Before.
function CacheListing {
  $corpus = $script:Corpus
  $cache = Join-Path (DataRoot) 'TradeCache'
  New-Item -ItemType Directory $cache -Force | Out-Null
  Copy-Item "$corpus\trade\desec\$listingId.json" $cache
}
# The vault on the left page at its top, Trade on the right.
function TradeBesideVault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { VaultPages } "the vault on the left" | Out-Null
  VaultGoTo 'Vault'
  ClickEl (ById 'Bookmark_Trade') "Trade"
  WaitUntil { ByName 'Virtual trading' } "the Trade page" | Out-Null
}

Case 'trade-vault.trade-and-vault-stay-side-by-side' -Tags trade, vault {
  # The two together, in either order of taking them.
  TradeBesideVault
  Check ($null -ne (VaultPages)) "Trade taken beside the vault: the vault stays on the left"
  Check ((ById 'Bookmark_Trade').Current.HelpText -like 'Trade*Ctrl + Tab: Next bookmark') "and its bookmark warns of nothing ('$((ById 'Bookmark_Trade').Current.HelpText)')"
  ClickEl (ById 'Bookmark_Inventory') "Inventory"
  WaitUntil { ItemsPage } "the inventory back" | Out-Null
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left) again"
  WaitUntil { VaultPages } "the vault on the left again" | Out-Null
  Check ($null -ne (ByName 'Virtual trading')) "the vault taken beside Trade: Trade stays on the right"
}

Case 'trade-vault.bought-beside-the-vault-lands-in-its-folder' -Tags trade, vault -Mode Legit -Before { CacheListing } {
  TradeBesideVault
  $count = VaultCount
  Check ($null -eq (ById 'Trade_BoughtBefore')) "a listing never bought: no warning mark"
  BuyAtNothing $link
  $name = WaitUntil { Said '^Bought (.+) to vault\.$' } "the strip: bought to vault" 6000
  Check ($null -ne $name) "bought beside the vault: 'Bought $name to vault.'"
  $kept = $false
  try { WaitUntil { (VaultCount) -eq $count + 1 } "one row more" 5000 | Out-Null; $kept = $true } catch { }
  Check $kept "the vault file gains a row ($count -> $(VaultCount))"
  Check ($null -ne (WaitUntil { Card $name } "the bought card" 4000)) "and its card is in the folder shown"
}

Case 'trade-vault.bought-before-wears-a-warning-in-legit' -Tags trade, play-mode -Mode Legit -Before { CacheListing } {
  TradeBesideVault
  BuyAtNothing $link
  WaitUntil { Said '^Bought (.+) to vault\.$' } "the strip: bought to vault" 6000 | Out-Null

  # Bought before, in Legit: the warning mark beside Buy, saying who and when.
  Put (LinkBox) ''
  Put (LinkBox) $link
  $mark = WaitUntil { ById 'Trade_BoughtBefore' } "the warning mark" 15000
  Check ($mark.Current.HelpText -match '^(Bought before: .+, \d\d/\d\d/\d{4}\.|Already bought\.)$') "the same listing again, Legit: a warning beside Buy, '$($mark.Current.HelpText)'"
  Check ([bool](WaitUntil { $b = ByTip 'Buy'; if ($b -and $b.Current.IsEnabled) { $b } } "Buy offered still" 5000)) "and Buy is still there to press"
}

Case 'trade-vault.card-selected-is-sold-and-undo-puts-it-back' -Tags trade, vault -Mode Legit -Changes {
  $name = 'Tome of Town Portal'
  TradeBesideVault

  # Sold beside the vault: the card selected there, its row out.
  ClickEl (WaitUntil { ByName 'SELL' } "the Sell tab") "SELL"
  Check ($null -ne (WaitUntil { ByName 'Please select an item.' } "nothing on sale yet" 3000)) "Sell beside the vault, nothing selected: 'Please select an item.'"
  $middle = (BookMiddle)
  ClickUntil { ClickEl (Card $name) $name } { @((All (Cond $AE::NameProperty $name)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt $middle })[0] } "$name on sale" 1200 | Out-Null
  Check $true "a card selected in the vault is the item on sale"
  $rows = VaultCount
  # The page's own $ opens the sale; the panel's Sell, another button of the same tooltip, makes it.
  $open = (Tipped 'Sell')[0]
  $openAt = $open.Current.BoundingRectangle
  ClickEl $open "the sale's button"
  WaitUntil { ByName 'Materials:' -Prefix } "the sale's panel" 4000 | Out-Null
  Put (Edits)[-1] '0'
  WaitUntil { ByName 'Receiving nothing.' } "receiving nothing" | Out-Null
  ClickEl (WaitUntil { @(Tipped 'Sell' | Where-Object { $_.Current.BoundingRectangle.Y -ne $openAt.Y -or $_.Current.BoundingRectangle.X -ne $openAt.X })[0] } "the panel's Sell") "Sell"
  $sold = WaitUntil { Said '^Sold (.+) from vault\.$' } "the strip: sold from vault" 6000
  Check ($sold -eq $name) "sold beside the vault: 'Sold $sold from vault.'"
  $out = $false
  try { WaitUntil { (VaultCount) -eq $rows - 1 } "one row fewer" 5000 | Out-Null; $out = $true } catch { }
  Check $out "the vault file loses the row ($rows -> $(VaultCount))"

  # Undo puts the sold card back.
  ClickEl (WaitUntil { $u = ByTip 'Undo' -Prefix; if ($u -and $u.Current.IsEnabled) { $u } } "Undo") "Undo"
  $back = $false
  try { WaitUntil { (VaultCount) -eq $rows } "the row back" 6000 | Out-Null; $back = $true } catch { }
  Check $back "Undo puts the sold card back ($(VaultCount) rows)"
}

Case 'trade-vault.records-button-opens-the-list' -Tags trade -Mode Legit -Before { CacheListing } {
  TradeBesideVault

  # A trade to list: a season's ledger stays behind at a migration, so the pad's vault begins with none.
  BuyAtNothing $link
  WaitUntil { Said '^Bought (.+) to vault\.$' } "the strip: bought to vault" 6000 | Out-Null

  # The trade records: what was bought and sold, listed.
  ClickEl (WaitUntil { ById 'Trade_Records' } "the records' button") "Trade records"
  $listed = WaitUntil { $c = ById 'Trade_RecordsCount'; if ($c -and $c.Current.Name -match '^\d+ trades?$') { $c.Current.Name } } "the list, counted" 4000
  $deals = @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -match '^(Bought|Sold)( \W .+)?$' } |
    Sort-Object { $_.Current.BoundingRectangle.Y } | ForEach-Object { $_.Current.Name })
  Check ($deals.Count -ge 1) "the records list what was traded: $listed - $($deals -join ' | ')"
  ClickEl (@((All (Cond $AE::HelpTextProperty 'Close')) | Where-Object { -not $_.Current.IsOffscreen } | Sort-Object { $_.Current.BoundingRectangle.Y })[0]) "close the records"
  Check ([bool](WaitUntil { if (-not (ById 'Trade_RecordsCount')) { 'closed' } } "the list closed" 4000)) "and the list closes"
}
