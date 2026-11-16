# The affix picker as the corruption's (one that has to confirm is inconsistent in
# use): no tick; the brush empties the aimed slot and the skull rolls one into it, each at once, the picker staying
# open; what stood when it closed is in the file.
#
# What a pick, a roll, an emptied slot and the slot buttons do to the item is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Edit/AffixTests and Forge/JewelSlotsTests.
#
# On the pad's PhySpearZon: the worn rare amulet, Beast Necklace, prefixes 311/414/440.

function Prefixes { if (((& $script:Cli dump "$pad\saves\PhySpearZon.d2s" 2>&1 | Out-String) -split '(?=\n  #)' | Where-Object { $_ -match 'Beast Necklace' } | Select-Object -First 1) -match 'prefix (\d+)/') { [int]$Matches[1] } }

Case 'affixes.brush-and-skull-write-at-once' -Tags affixes, edit -Changes {
  Check ((Prefixes) -eq 311) "start: the amulet's first prefix is 311"
  ClickUntil { ClickEl (WaitUntil { Tile 'Beast Necklace' } "the amulet") "the amulet" } { OnEdit 'Beast Necklace' } "the amulet on the Edit page" | Out-Null

  # The picker opens on the first prefix.
  InvokeEl (WaitUntil { @((All (Cond $AE::NameProperty "Wyrm's")) | Where-Object { $_.Current.AutomationId -ne 'Tile' })[-1] } "the affix line") "affixes"
  WaitUntil { ByName 'Prefixes' } "the affix picker" | Out-Null

  # The brush: the slot emptied, the picker still open on it.
  ClickEl (WaitUntil { ByTip 'Remove affix' } "the brush") "empty slot"
  Check ([bool](WaitUntil { ByName 'Empty. Pick one below, or roll for it.' } "the slot said empty")) "the brush empties the slot, the picker stays open"

  # The skull: one rolled into the empty slot, the picker still open.
  ClickEl (WaitUntil { ByTip 'Random affix' } "the skull") "random affix"
  Check ([bool](WaitUntil { if (-not (ByName 'Empty. Pick one below, or roll for it.') -and (ByName 'Prefixes')) { 'rolled' } } "a prefix rolled" 3000)) "the skull rolls one in, the picker stays open"

  # Closed: what stood is written.
  if ($close = ByTip 'Close') { InvokeEl $close "close the picker" }
  Kept "the save written"
  Check ((Prefixes) -gt 0) "written: a first prefix ($(Prefixes))"
}

# The rare name's skull and the corruption picker's top as it stands with nothing: every
# picker has its roll, and nothing below moves when the first pick lands. The name rolled is written as the picker
# closes, so the pad is put back after: without -Changes the next case found no Beast Necklace.
Case 'affixes.name-skull-and-corruption-none' -Tags affixes, corruption, edit -Changes {
  ClickUntil { ClickEl (WaitUntil { Tile 'Beast Necklace' } "the amulet") "the amulet" } { OnEdit 'Beast Necklace' } "the amulet on the Edit page" | Out-Null

  # The name: the skull names it anew, the picker open still.
  InvokeEl (WaitUntil { @((All (Cond $AE::NameProperty 'Beast Necklace')) | Where-Object { $_.Current.AutomationId -ne 'Tile' -and $_.Current.ControlType -eq $CT::Button })[0] } "the name button") "the name"
  ClickEl (WaitUntil { ByTip 'Random name' } "the name's skull") "random name"
  Check ([bool](WaitUntil { if (-not (ByName 'Beast Necklace') -and (ByTip 'Random name')) { 'renamed' } } "another name" 3000)) "the skull gives another name, the picker open"
  if ($close = ByTip 'Close') { InvokeEl $close "close the picker" }

  # The corruption picker on an amulet with none: its top stands, the brush there and dimmed.
  ClickEl (WaitUntil { $b = ByTip 'Corrupt'; if ($b -and $b.Current.IsEnabled) { $b } } "the corrupt button") "corrupt"
  $brush = WaitUntil { ByTip 'Clean corruption' -Prefix } "the corruption's brush"
  Check (-not $brush.Current.IsEnabled -and $null -ne (ByName 'Corrupted')) "the top stands with nothing on the item, its brush dimmed"
  if ($close = ByTip 'Close') { InvokeEl $close "close the picker" }
}
