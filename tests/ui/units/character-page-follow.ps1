# The Character page follows the session only while it is shown, or when it is next shown (WhenShown),
# as only a window shows it: that the page's view tells its model when it comes to the front. The character is
# changed under it as the game saving would - with the Items page in front, another character's save is copied
# over the open one - and on going back to the Character page the page draws that character, not the one it
# last drew.
#
# What the page does when told it is shown, and that the program reads the file again by itself, is proven
# without a window: tests/PD2SPCompanion.Shell.Tests/Character/PageFollowTests.

Case 'character-page-follow.coming-back-draws-what-was-read-meanwhile' -Tags character, hidden-pages -Changes {
  $corpus = $script:Corpus
  ClickEl (ById 'SideBar_Char') "Character"
  Check ([bool](WaitUntil { OnScreen (Cond $AE::NameProperty 'Level 86 Amazon') } "the Character page" 5000)) "the Character page shows Level 86 Amazon"

  ClickEl (ById 'SideBar_Inventory') "Items"
  WaitUntil { ItemsPage } "items page" | Out-Null

  # The game saving another character's state into this one's file.
  Copy-Item "$corpus\saves\FigureTest.d2s" "$pad\saves\PhySpearZon.d2s" -Force
  Check ([bool](WaitUntil { OnScreen (Cond $AE::NameProperty 'PhySpearZon reloaded. Saved in game.') } "the reload" 8000)) "the program read the file again"

  ClickEl (ById 'SideBar_Char') "Character"
  Check ([bool](WaitUntil { OnScreen (Cond $AE::NameProperty 'Level 40 Barbarian') } "the Character page again" 5000)) "the Character page shows what was read: Level 40 Barbarian"
  Check (-not (OnScreen (Cond $AE::NameProperty 'Level 86 Amazon'))) "and not the character it last drew"
}
