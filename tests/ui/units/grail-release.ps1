# A grail card chosen searches for its copies, and chosen again it is let go of (the card
# stayed selected over a search that had ended). As only a window shows it: that a click on the card in the list
# chooses it, a second click lets it go, and a third chooses it again - nothing stuck. Read only: nothing is saved.
#
# What the grail and the search decide between them is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Grail/GrailReleaseTests.
#
# The item search has a page of its own, and the grail's card is ended by the card.

# The grail's card for the piece: the list item on the right page holding its name.
function Card([string]$name) {
  foreach ($item in (All (Cond $AE::ControlTypeProperty $CT::ListItem))) {
    if ($item.Current.IsOffscreen -or $item.Current.BoundingRectangle.X -lt (BookMiddle)) { continue }
    if ($item.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $item }
  }
}
function IsChosen($card) { $card.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected }

$piece = 'Death Cleaver'

Case 'grail-release.card-clicked-again-is-let-go' -Tags grail, search {
  ClickUntil { ClickEl (ById 'Bookmark_Grail') "grail" } { ByName 'UNIQUES' } "the grail" 1000 | Out-Null
  ClickEl (ByName 'UNIQUES') "UNIQUES"
  $search = WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object {
      -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt (BookMiddle) })[0] } "the grail's search"
  $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($piece)

  # Chosen: the card lit.
  ClickUntil { ClickEl (WaitUntil { Card $piece } "the card") "the card" } { if (IsChosen (Card $piece)) { 'chosen' } } "the card chosen" 1000 | Out-Null
  Check (IsChosen (Card $piece)) "the card clicked is chosen"

  # Clicked again: let go.
  ClickEl (Card $piece) "the card again"
  Check ([bool](WaitUntil { if (-not (IsChosen (Card $piece))) { 'let go' } } "the card let go" 3000)) "clicked again, it is let go"

  # And chosen again: nothing stuck.
  ClickEl (Card $piece) "the card a third time"
  Check ([bool](WaitUntil { if (IsChosen (Card $piece)) { 'chosen' } } "the card chosen again" 3000)) "chosen again, it is chosen again"

  # As the grail was found, for the cases that share this start: the card let go, its box emptied.
  ClickEl (Card $piece) "the card, let go"
  WaitUntil { if (-not (IsChosen (Card $piece))) { 'let go' } } "the card let go" 3000 | Out-Null
  $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
}
