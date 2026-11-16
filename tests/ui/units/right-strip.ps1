# The right page's message strip, as only a window shows it: what an item panel action came to
# is drawn under the right page - in the bottom bar's right half - not under the item's name and not on the left
# strip. The Demonic Cube's reroll on the worn amulet: "Beast Necklace rerolled: ...".
#
# That the reroll is said on the right page's log and not the left's is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Window/MessageStripTests.
#
# The reroll is an edit, kept at once: the pad is put back after.

Case 'right-strip.panel-action-is-said-under-the-right-page' -Tags window, strip, items -Changes {
  ClickUntil { ClickEl (WaitUntil { Tile 'Beast Necklace' } "the worn amulet") "the amulet" } { OnEdit 'Beast Necklace' } "the amulet on the Edit page" | Out-Null

  # By its first words: the tooltip goes on with the cube's key, "(Ctrl + R)".
  ClickEl (WaitUntil { ByTip 'Reroll all stats' -Prefix } "the Demonic Cube") "reroll"

  $said = WaitUntil { ByName 'Beast Necklace rerolled: ' -Prefix } "the message"
  $b = $said.Current.BoundingRectangle
  $middle = (BookMiddle)
  Check ($b.X -gt $middle) "said under the right page (x $([int]$b.X), window middle $([int]$middle))"
  Check ($b.Y -gt $script:r.Y + $script:r.Height - 60) "said in the bottom bar, not under the item's name"
}
