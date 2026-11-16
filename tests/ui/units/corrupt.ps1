# The corruption picker, as only a window shows it (CorruptionEditor): that the Corrupt button among the item's
# tools opens it, that its roll and its close are there to press, and that what stood when it closed is in the
# file, the item's summary saying Corrupted. The one case kept for the pickers of the Edit page: the others -
# desecration, craft, runeword, superior, affixes, the rare name - open and close the same way.
#
# What a roll, a second roll, a clean and a desecration do to the item, the set lists they leave alone and the
# legitimacy check's silence are proven without a window: tests/PD2SPCompanion.Shell.Tests/Edit/CorruptionTests.
#
# On the pad's PhySpearZon: the worn rare amulet, Beast Necklace.

function Amulet { (& $script:Cli dump "$pad\saves\PhySpearZon.d2s" 2>&1 | Out-String) -split '(?=\n  #)' | Where-Object { $_ -match 'Beast Necklace' } | Select-Object -First 1 }

Case 'corrupt.picker-rolls-a-corruption-into-the-file' -Tags corruption, edit -Changes {
  Check ((Amulet) -notmatch 'corruptor') "start: the amulet is not corrupted"
  ClickUntil { ClickEl (WaitUntil { Tile 'Beast Necklace' } "the amulet") "the amulet" } { OnEdit 'Beast Necklace' } "the amulet on the Edit page" | Out-Null

  # corrupt: the cube's own roll, the item's at once; the picker stays open on what was rolled
  ClickEl (WaitUntil { $b = ByTip 'Corrupt'; if ($b -and $b.Current.IsEnabled) { $b } } "the corrupt button") "corrupt"
  ClickEl (WaitUntil { ByTip 'Random corruption' } "the roll button") "roll"
  Check ([bool](WaitUntil { ByName 'Corrupted' } "what was rolled, at the picker's top")) "the picker stays open on what was rolled"
  if ($close = ByTip 'Close') { InvokeEl $close "close the picker" }
  Kept "the save written (corrupted)"

  # The item's own summary says it, as the game does.
  Check ([bool](WaitUntil { ByName 'Corrupted' } "the summary says Corrupted")) "the summary says Corrupted"
  $amulet = Amulet
  Check ($amulet -match '\+ corruptor\s+1' -and $amulet -match '\+ corrupted\s+\d+') "written: the amulet is corrupted"
  Check (([regex]::Matches($amulet, '\+ corrupted\s')).Count -eq 1) "once"
}
