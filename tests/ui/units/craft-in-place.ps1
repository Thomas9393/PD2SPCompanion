# A craft made in place, as only a window shows it: that the Craft button among the item's
# tools opens the families a ring can be made into, and that a family clicked crafts the ring where it lies - the
# file read back: crafted, the same id, the same place, and no copy. The picker stays open on the pick
# (another family replaces it), and the craft is written when its cross closes it.
#
# The recipe changed after, the worn amulet, boots stored in the vault and crafted on the vault's Edit page, and
# the legitimacy check's silence are proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Edit/CraftInPlaceTests.
#
# On the pad's PhySpearZon: the magic Steel Ring of Skill in the personal stash, at 1,1.

$script:Families = 'Blood', 'Caster', 'Hit Power', 'Safety', 'Vampiric', 'Bountiful', 'Brilliant', 'Lucky', 'Mercenary'

Case 'craft-in-place.ring-crafted-where-it-lies' -Tags craft, edit -Mode Casual -Changes {
  # a magic ring in the personal stash, at 1,1 - found by its tile, never a window point (the layout moves)
  Pane 0 'Personal Stash'
  ClickUntil { ClickEl (WaitUntil { Tile 'Steel Ring of Skill' } "the ring") "the ring" } { OnEdit 'Steel Ring of Skill' } "the ring on the Edit page" | Out-Null

  # Craft the selected item through the panel: the cube button, the first family offered (the item's at once).
  ClickEl (WaitUntil { $b = ByTip 'Craft'; if ($b -and $b.Current.IsEnabled) { $b } } "craft button") "craft"
  $row = WaitUntil { foreach ($f in $script:Families) { $t = ByName $f; if ($t) { return $t } } } "a craft family in the picker"
  $family = $row.Current.Name
  ClickEl $row "family $family"

  # The picker stays on the pick, nothing written yet; its cross closes it, and the craft is written.
  $was = PadStamps
  Start-Sleep -Milliseconds 1200
  Check ([bool](ByName $family) -and (PadStamps) -eq $was) "a family clicked: the picker stays open, and nothing is written while it is"
  $cross = @((All (Cond $AE::HelpTextProperty 'Close')) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.AutomationId -ne 'CloseButton' })[0]
  ClickEl $cross "the picker's cross"
  Kept "written (the ring, $family)"

  $dump = & $script:Cli dump "$pad\saves\PhySpearZon.d2s" 2>&1 | Out-String
  Check ($dump -match 'Craft, ilvl \d+, id 4010115452, at 1,1') "ring: crafted ($family), same id, same place"
  Check ($dump -match 'items \(92\)') "character: 92 items, as before (no copy)"
}
