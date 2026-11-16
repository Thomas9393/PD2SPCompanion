# The backups out of the main flow, as only a window shows it: the home page's card draws no Backups button and no save
# button; the list opens over the Settings page from Storage's button; a row clicked there puts its backup on the
# home page's card, which then - and only then - draws Close backup; Restore is the top bar's, and its click
# reaches the question.
#
# What the pages decide - whose backups Settings counts, what the newest says it was taken before, that Restore
# asks and then writes the backup, that its answers go - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Home/BackupsTests.
#
# On the pad's PhySpearZon, whose worn amulet (Beast Necklace) has item level 81.

function Texts([string]$like) { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { $_.Current.Name -like $like -and -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) }
function Level { if ((& $script:Cli dump "$pad\saves\PhySpearZon.d2s" --no-stats 2>&1 | Out-String) -match 'Beast Necklace[^\n]*\n[^\n]*ilvl (\d+)') { [int]$Matches[1] } }

# A change, which takes a backup of the character as it was: the amulet's item level stepped to 82, and written.
function StepTheAmulet {
  ClickUntil { ClickEl (WaitUntil { Tile 'Beast Necklace' } "the amulet") "the amulet" } { OnEdit 'Beast Necklace' } "the amulet on the Edit page" | Out-Null
  $box = WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen -and $_.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '81' })[0] } "the item level box"
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('82')
  Kept "the level written"
}
# Settings, Storage: the list of the open character's backups, opened over the page; its rows, newest first.
function OpenTheList {
  ClickEl (ById 'SideBar_Settings') "Settings"
  $open = WaitUntil { $b = ById 'Settings_Backups'; if ($b -and $b.Current.IsEnabled) { $b } } "the Backups button under Storage"
  # Clicked until the list is up: a click that lands while the Settings page is still fading in is lost (the
  # restore's case after the home card's). The button opens and shuts the list, so the list has
  # long to show before it is pressed again.
  ClickUntil { ClickEl $open "Backups" } { ByName 'Restore replaces current character.' } "the list" 1500 | Out-Null
  WaitUntil { $r = @((All (Cond $AE::HelpTextProperty 'Open backup')) | Where-Object { -not $_.Current.IsOffscreen } | Sort-Object { $_.Current.BoundingRectangle.Y }); if ($r.Count -ge 1) { , $r } } "the backups' rows"
}

Case 'backups-list.home-card-draws-no-backup-or-save-button' -Tags backups, home {
  # The Items button pressed while its page is up leaves the page: the landing page, the character still open.
  ClickEl (ById 'SideBar_Inventory') "Items again: the landing page"
  WaitUntil { $b = ByTip 'Reload current character'; if ($b -and $b.Current.IsEnabled) { $b } } "the home card" | Out-Null
  Check ($null -eq (ByTip 'Backups') -and $null -eq (ById 'Backup_Restore') -and $null -eq (ById 'Backup_Close')) "the home card: no Backups button, no Restore, no Close backup"
  Check ($null -eq (ByTip 'Save current character') -and $null -eq (ByTip 'No changes')) "and no save button"
}

Case 'backups-list.settings-row-puts-a-backup-on-the-home-card' -Tags backups, home, settings -Changes {
  StepTheAmulet
  ClickEl (ById 'SideBar_Settings') "Settings"
  $open = WaitUntil { $b = ById 'Settings_Backups'; if ($b -and $b.Current.IsEnabled) { $b } } "the Backups button under Storage"
  $line = @(Texts 'PhySpearZon: * backup*')
  Check ($line.Count -eq 1) "Storage names the open character's backups: '$($line -join ' | ')'"
  ClickEl $open "Backups"
  Check ($null -ne (WaitUntil { ByName 'Restore replaces current character.' } "the list" 4000)) "the list opens over the Settings page"
  $rows = WaitUntil { $r = @((All (Cond $AE::HelpTextProperty 'Open backup')) | Where-Object { -not $_.Current.IsOffscreen } | Sort-Object { $_.Current.BoundingRectangle.Y }); if ($r.Count -ge 1) { , $r } } "the backups' rows"
  Check ((Texts 'Before: Edited Beast Necklace.*').Count -ge 1) "the newest row says what it was taken before: '$(@(Texts 'Before: Edited Beast Necklace.*')[0])'"

  ClickEl $rows[0] "Open backup (the newest)"
  $restore = WaitUntil { $b = ById 'Backup_Restore'; if ($b -and $b.Current.IsEnabled) { $b } } "Restore, in the top bar" 8000
  Check ($null -ne $restore -and $null -ne (ById 'Backup_Close')) "a row clicked: the backup is shown on the home page, Close backup on its card and Restore in the top bar"
  Check ($restore.Current.BoundingRectangle.Y -lt (ById 'Backup_Close').Current.BoundingRectangle.Y - 40) "Restore once, in the top bar: not on the card as well"
  Check ((Texts 'Before: Edited Beast Necklace.*').Count -ge 1) "the card says which, by what it was taken before: '$(@(Texts 'Before: Edited Beast Necklace.*')[0])'"
  Check (($restore.Current.HelpText -split "`n")[0].Trim() -eq 'Restore this backup') "Restore says what it does: '$(($restore.Current.HelpText -split "`n")[0].Trim())'"

  # Esc closes the backup shown, as Close backup does (every overlay closes the same ways).
  Front; [Fast]::keybd_event(0x1B, 0, 0, 0); [Fast]::keybd_event(0x1B, 0, 2, 0)
  Check ([bool](WaitUntil { if (-not (ById 'Backup_Close') -and -not (ById 'Backup_Restore')) { 'closed' } } "the backup closed" 4000)) "Esc closes the backup: Close backup and Restore are gone"
}

Case 'backups-list.restore-click-asks-and-the-answers-go' -Tags backups, home -Changes {
  StepTheAmulet
  $rows = OpenTheList
  ClickEl $rows[0] "Open backup (the newest)"
  $restore = WaitUntil { $b = ById 'Backup_Restore'; if ($b -and $b.Current.IsEnabled) { $b } } "Restore, in the top bar" 8000
  ClickEl $restore "Restore"
  Check ($null -ne (WaitUntil { ByName 'Current character is backed up first.' -Prefix } "the question" 4000)) "Restore asks first: 'Current character is backed up first.'"
  ClickEl (WaitUntil { ByName 'RESTORE' } "RESTORE") "RESTORE"
  $back = $false
  try { WaitUntil { (Level) -eq 81 } "the level back in the file" 10000 | Out-Null; $back = $true } catch { }
  Check $back "RESTORE writes the backup: the level is $(Level) again"
  Check ([bool](WaitUntil { if (-not (ById 'Backup_Restore')) { 'gone' } } "the answers gone" 5000)) "and with no backup on screen Restore and Close backup are gone"
}
