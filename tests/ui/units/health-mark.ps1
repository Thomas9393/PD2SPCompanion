# The character health mark, as only a window shows it (subtle: made when
# something is wrong, otherwise not shown): no
# mark is drawn on a clean character's card; and the home page's coming back into sight judges what was written
# while it was hidden (WhenShown) - the mark drawn on the card, its tooltip naming the items.
#
# What the mark says and when the stash is judged is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Home/HealthMarkTests.
#
# The stash with two items the game could not hold is the UI pad's fixtures\pd2_shared_impossible.stash (Season 14's): Pus Spitter and
# Toothrow given two sockets more than their bases take.

Case 'health-mark.clean-card-draws-no-mark' -Tags home, health {
  # The Items button pressed while its page is up leaves the page: the landing page, the character still open.
  ClickEl (ById 'SideBar_Inventory') "Items again: the landing page"
  WaitUntil { $b = ByTip 'Reload current character'; if ($b -and $b.Current.IsEnabled) { $b } } "the home card" | Out-Null
  Start-Sleep -Milliseconds 800
  Check ($null -eq (ById 'HealthMark')) "clean character and stash: no mark"
}

Case 'health-mark.written-while-away-is-marked-on-coming-back' -Tags home, health -Changes {
  $corpus = $script:Corpus
  $badStash = "$corpus\ui-pad\fixtures\pd2_shared_impossible.stash"
  # A clean stash open, the Items page in front, and the game writing the stash with the two in it.
  Copy-Item $badStash "$pad\saves\pd2_shared.stash" -Force
  # The stash reads itself again when the folder watcher settles: a moment for it.
  Start-Sleep -Seconds 3
  ClickEl (ById 'SideBar_Inventory') "Items again: the landing page"
  $mark = WaitUntil { ById 'HealthMark' } "the mark after coming back" 5000
  $said = $mark.Current.HelpText
  Check ($said -match '2 illegal items in shared stash' -and $said -match 'Pus Spitter' -and $said -match 'Toothrow') "written while on the Items page: the mark on coming back, naming them ($said)"
}
