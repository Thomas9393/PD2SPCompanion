# The season migration, as only a started program and its window show it: with an S13 vault beside the pad's own the program starts as a
# new season would - the notice is asked once the window is up; its MIGRATE click shows Settings with the
# migration panel drawn over it, a card named for the vault with its dry run; the panel's MIGRATE click writes
# it, and the strip, the vault card's row and the open vault's box draw what came of it.
#
# What the program decides on the way - the notice's words and its two answers, the card's lines, the strip's
# words, which vault is open after, the season the notice is answered for - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Season/SeasonMigrationTests.
#
# The vault is made before the program starts (pd2spc pastvault), and the notice made unanswered in the run's
# own settings. One season back, which the installed tables' previous-season columns read: S12's would be refused
# for want of a snapshot. Under a name of its own, as the pad's own S13 vault was migrated when the pad moved to S14.

function Says([string]$part) {
  foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
    if ($e.Current.Name.Contains($part) -and -not $e.Current.IsOffscreen) { return $e }
  }
}

Case 'season-migrate.notice-at-start-opens-the-panel-and-its-migrate-writes' -Tags season, migration, settings -Start Home -Before {
  # The last season's vault: what a player has at the first launch of a new season.
  $env:PD2SPC_VAULTS = "$pad\vtsmart"
  & $script:Cli pastvault "$script:Corpus\ui-pad\s13\pad\saves\pd2_shared.stash" --season S13 --tables $script:S13Tables --name 'S13 Second'
  if ($LASTEXITCODE -ne 0) { throw "the S13 vault was not made" }
  # Not yet answered this season. Written as the program writes it (UsePlayMode): UTF-8 without a BOM.
  $settingsFile = SettingsFile
  $text = [IO.File]::ReadAllText($settingsFile) -replace '"MigrationNoticeSeason": "\w*"', '"MigrationNoticeSeason": ""'
  [IO.File]::WriteAllText($settingsFile, $text, (New-Object System.Text.UTF8Encoding($false)))
} {
  Check ([bool](WaitUntil { Says 'Migrate S13 vault?' } "the new-season notice" 10000)) "the notice asks"
  ClickEl (WaitUntil { ByName 'MIGRATE' } "the notice's MIGRATE") "MIGRATE"

  Check ([bool](WaitUntil { ByName "Migrate S13 to $(PadSeason)" } "the migration panel" 5000)) "Settings shows the migration panel"
  Check ([bool](WaitUntil { Says '40 items:' } "the dry run" 20000)) "the dry run counts 40 items"
  Check ([bool](ByName 'S13 Second')) "on a card named for the vault"
  # Which line depends on a snapshot of S13 in the run's data (copied from the developer's): the words are the unit test's.
  Check ([bool](WaitUntil { if ($k = Says 'Values keep their place') { $k } else { Says 'Values kept as they are' } } "the values line" 3000)) "the card says how values come across"

  ClickEl (ByName 'MIGRATE') "the panel's MIGRATE"
  Check ([bool](WaitUntil { Says 'Migrated 40 items from S13.' } "the migration" 30000)) "the strip says what was migrated"
  Check ([bool](WaitUntil { ByName 'Migrated' } "the row" 3000)) "the vault card's row says Migrated"
  # Into a vault of its own name, beside the season's own, and that one is open after.
  Check (Test-Path (Join-Path "$pad\vtsmart" "$(PadSeason)\Softcore\S13 Second.pd2lib")) "S13 Second is a vault of $(PadSeason)'s"
  Check ([bool](WaitUntil { $b = ById 'Vault_Open'; if ($b -and $b.FindFirst($TS::Descendants, (Cond $AE::NameProperty 'S13 Second'))) { 'open' } } "the migrated vault open" 5000)) "and is the vault open"
  $stored = (Get-Content (SettingsFile) -Raw | ConvertFrom-Json).MigrationNoticeSeason
  Check ($stored -eq (PadSeason)) "the notice is answered for $(PadSeason) ($stored)"
}
