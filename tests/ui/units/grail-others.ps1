# The grail's copies held on the other characters, as only a window shows them: that a card's Details have the line "Other characters N".
#
# What the grail counts there - the other characters' saves read without opening them, none written, the shared
# stash nobody else's - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Grail/GrailOthersTests.
#
# A second character is made with the command line tool before the program starts - Finder, a renamed copy of the
# pad's own with everything stored cleared and a Harlequin Crest given - and the pad's own is opened:
#   a. Harlequin Crest: "Other characters 1" - Finder's.
#   b. Death Cleaver, which lies in the shared stash and on no character: "Other characters 0".
# Casual, as the script this came from ran.

# The card's second line of counts, once it reads as asked: "Other characters N".
function Others { $e = ById 'Grail_HeldOnOthers'; if ($e -and $e.Current.Name -match '^Other characters (\d+)$') { [int]$Matches[1] } }

Case 'grail-others.card-says-the-other-characters-copies' -Tags grail -Start Home -Mode Casual -Before {
  $saves = "$pad\saves"
  $made = "$pad\grail-others-made"
  Remove-Item $made -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory $made | Out-Null
  Copy-Item "$saves\PhySpearZon.d2s" $made
  & $script:Cli rename "$made\PhySpearZon.d2s" Finder | Out-Null
  & $script:Cli clear "$made\Finder.d2s" | Out-Null
  & $script:Cli give "$made\Finder.d2s" --unique 'Harlequin Crest' --backup "$pad\grail-others-give" | Out-Null
  Copy-Item "$made\Finder.d2s" $saves
} {
  # The pad's own character, by its card: there are two to choose from.
  DoubleClickEl (WaitUntil { ByName 'PhySpearZon' } "PhySpearZon's card") "PhySpearZon's card"
  WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "character loaded" | Out-Null
  ClickEl (ById 'SideBar_Inventory') "Items"
  WaitUntil { ItemsPage } "items page" | Out-Null

  ClickUntil { ClickEl (ById 'Bookmark_Grail') "grail" } { ByName 'UNIQUES' } "the grail" 1000 | Out-Null
  ClickEl (ByName 'UNIQUES') "UNIQUES"
  $search = WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object {
      -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt (BookMiddle) })[0] } "the grail's search"

  # ---- a
  $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Harlequin Crest')
  $crest = WaitUntil { $n = Others; if ($n -eq 1) { $n } } "Other characters 1 on the Crest's card"
  Check ($crest -eq 1) "Harlequin Crest: Other characters $crest - Finder's, read from its save"

  # ---- b
  $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Death Cleaver')
  # Waited for as a truth, not as the number: a 0 handed back would read as "not yet".
  WaitUntil { (ByName 'Death Cleaver') -and $null -ne (Others) -and (Others) -ne 1 } "the Cleaver's card" | Out-Null
  $cleaver = Others
  Check ($cleaver -eq 0) "Death Cleaver, in the shared stash only: Other characters $cleaver"
}
