# The payment's two ways, as only a window shows them: that the payment's
# panel has the button that switches between an offer counted out and each currency picked by hand, and that the
# currencies' boxes reach the offer.
#
# What the payment decides - what the offer comes to, held at what the tab holds, when it can be paid - is proven
# without a window: tests/PD2SPCompanion.Shell.Tests/Trade/PayByHandTests.
#
# A listing is opened by its link from the trade cache (no network), put there before the program starts; the
# payment is looked at and left: nothing is paid, nothing kept.

$listingId = '6a92170a9f44c105fc2892b4'
function Held($box) { $p = $null; if ($box.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$p)) { $p.Current.Value } }
function Put($box, [string]$text) { $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); Start-Sleep -Milliseconds 250 }
function Edits { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.HelpText -ne 'Paste a PD2 trade listing link' -and $_.Current.BoundingRectangle.X -gt (BookMiddle) } | Sort-Object { $_.Current.BoundingRectangle.Y }) }
# The currencies' rows: each count box with the worth its line says, "0.5 HR . 3 in tab".
function Rows {
  $lines = @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -match '^([0-9.]+) HR .* (\d+) in tab$' })
  $boxes = Edits
  foreach ($line in $lines) {
    $null = $line.Current.Name -match '^([0-9.]+) HR .* (\d+) in tab$'
    $y = $line.Current.BoundingRectangle.Y
    $box = @($boxes | Sort-Object { [Math]::Abs($_.Current.BoundingRectangle.Y - $y) })[0]
    [pscustomobject]@{ Worth = [decimal]$Matches[1]; Stock = [int]$Matches[2]; Box = $box }
  }
}
function Offer { (Edits)[-1] }
# The corpus's listing into the run's trade cache, which begins empty: for -Before.
function CacheListing {
  $corpus = $script:Corpus
  $cache = Join-Path (DataRoot) 'TradeCache'
  New-Item -ItemType Directory $cache -Force | Out-Null
  Copy-Item "$corpus\trade\desec\$listingId.json" $cache
}

Case 'pay-by-hand.button-switches-between-counting-and-picking' -Tags trade -Before { CacheListing } {
  ClickEl (WaitUntil { ById 'Bookmark_Trade' } "Trade") "Trade"
  $link = WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { $_.Current.HelpText -eq 'Paste a PD2 trade listing link' })[0] } "the link box"
  Put $link "https://www.projectdiablo2.com/market/listing/$listingId"
  $buy = WaitUntil { $b = ByTip 'Buy'; if ($b -and $b.Current.IsEnabled) { $b } } "the listing shown, Buy offered" 15000
  ClickEl $buy "Buy"
  $pick = WaitUntil { ByTip 'Pick by hand' } "the payment, counting for the player"
  Check ($null -ne (ByTip 'Pay')) "the payment opens counting an offer out, as it did"

  ClickEl $pick "Pick by hand"
  WaitUntil { ByTip 'Count for me' } "picking by hand" | Out-Null
  $rows = @(Rows)
  Check ($rows.Count -gt 0) "picking: $($rows.Count) currencies to set"

  # The boxes reach the offer: everything put back to nought, then one of a currency the tab holds.
  foreach ($row in $rows) { Put $row.Box '0' }
  Check ([decimal](Held (Offer)) -eq 0 -and $null -ne (ByName 'Paying nothing.')) "nothing picked: the offer is 0 ('$(Held (Offer))'), and it says so"
  $one = @($rows | Where-Object { $_.Stock -gt 0 })[0]
  Put $one.Box '1'
  Check ([decimal](Held (Offer)) -eq $one.Worth) "one picked: the offer is its worth, $($one.Worth) HR ('$(Held (Offer))')"

  ClickEl (ByTip 'Count for me') "Count for me"
  Check ($null -ne (WaitUntil { ByTip 'Pick by hand' } "counting again")) "the button switches back: the offer is counted out again"
}
