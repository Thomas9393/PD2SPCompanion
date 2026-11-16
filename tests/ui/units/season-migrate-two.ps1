# Two older seasons never migrated, as only a window shows it: with S7 and S13 vaults beside the pad's own, the notice drawn at
# the start names both, and the panel draws a card for each season - S7's, two seasons back with no snapshot of
# its tables, with the reason it is refused in place of a dry run; after the panel's MIGRATE the strip and the
# vault card's row draw that S7 still waits.
#
# What the program decides on the way - the notice's and the panel's words, which season migrates and which is
# refused and why, what the strip and the row then say - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Season/SeasonMigrationTests.
#
# Both vaults are made before the program starts (pd2spc pastvault), and the notice made unanswered in the run's
# own settings.

function Says([string]$part) {
  foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
    if ($e.Current.Name.Contains($part) -and -not $e.Current.IsOffscreen) { return $e }
  }
}

Case 'season-migrate-two.notice-names-both-and-each-season-has-its-card' -Tags season, migration, settings -Start Home -Before {
  $s7 = "$script:Corpus\packs\betweenwalls-s7\PD2-Singleplayer-main\Diablo II"
  $env:PD2SPC_VAULTS = "$pad\vtsmart"
  & $script:Cli pastvault "$s7\Save\Library\_LOD_SharedStashSave.sss" --season S7 --tables "$s7\ProjectD2\data\global\excel\modpacks\official" --take 20
  if ($LASTEXITCODE -ne 0) { throw "the S7 vault was not made" }
  # S13: one season back, which the installed tables' previous-season columns read. Under a name of its own, as the
  # pad's own S13 vault was migrated when the pad moved to S14.
  & $script:Cli pastvault "$script:Corpus\ui-pad\s13\pad\saves\pd2_shared.stash" --season S13 --tables $script:S13Tables --name 'S13 Second'
  if ($LASTEXITCODE -ne 0) { throw "the S13 vault was not made" }
  $settingsFile = SettingsFile
  $text = [IO.File]::ReadAllText($settingsFile) -replace '"MigrationNoticeSeason": "\w*"', '"MigrationNoticeSeason": ""'
  [IO.File]::WriteAllText($settingsFile, $text, (New-Object System.Text.UTF8Encoding($false)))
} {
  Check ([bool](WaitUntil { Says 'Migrate S7 and S13 vaults?' } "the new-season notice" 10000)) "the notice names both seasons"
  ClickEl (WaitUntil { ByName 'MIGRATE' } "the notice's MIGRATE") "MIGRATE"

  Check ([bool](WaitUntil { ByName "Migrate S7 and S13 to $(PadSeason)" } "the migration panel" 5000)) "the panel names both"
  Check ([bool](WaitUntil { ByName 'S13 Softcore' } "S13's card" 20000)) "a card for S13"
  Check ([bool](WaitUntil { ByName 'S7 Softcore' } "S7's card" 3000)) "a card for S7"
  Check ([bool](WaitUntil { Says 'No snapshot of S7' } "S7's refusal" 3000)) "S7 refused: no snapshot of its tables"
  Check ([bool](WaitUntil { Says '40 items:' } "S13's dry run" 20000)) "S13's dry run counts 40 items"

  ClickEl (ByName 'MIGRATE') "the panel's MIGRATE"
  Check ([bool](WaitUntil { Says 'Migrated 40 items from S13.' } "the migration" 30000)) "the strip says what was migrated"
  Check ([bool](Says 'S7 not migrated.')) "and that S7 was not"
  Check ([bool](WaitUntil { ByName 'Migrate from S7' } "the row" 3000)) "the row names S7, still waiting"
  Check ([bool](ByName 'Not migrated yet')) "not migrated yet"
}
