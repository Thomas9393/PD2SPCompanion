# The grail's copies held, as only a window shows them: that a card says its counts in a
# line - "Character N   Vault M".
#
# What the grail counts - the shared stash's copy as the character's, a store followed and undone -
# is proven without a window: tests/PD2SPCompanion.Shell.Tests/Grail/GrailCountsTests.
#
# Read only: nothing is stored here.

# The card's line: "Character N      Vault M".
function Counts {
  foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
    if (-not $e.Current.IsOffscreen -and $e.Current.Name -match '^Character (\d+)\s+Vault (\d+)$') { return @([int]$Matches[1], [int]$Matches[2]) }
  }
}

$piece = 'Death Cleaver'

Case 'grail-counts.card-says-its-copies' -Tags grail {
  # The grail, its uniques, the piece searched for.
  ClickUntil { ClickEl (ById 'Bookmark_Grail') "grail" } { ByName 'UNIQUES' } "the grail" 1000 | Out-Null
  ClickEl (ByName 'UNIQUES') "UNIQUES"
  $search = WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object {
      -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt (BookMiddle) })[0] } "the grail's search"
  $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($piece)
  $held = WaitUntil { Counts } "the card's counts"
  Check ($held[0] -ge 1) "the shared stash's copy counts as the character's (character $($held[0]), vault $($held[1]))"

  # The grail as it was found: nothing typed.
  $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
  Start-Sleep -Milliseconds 300
}
