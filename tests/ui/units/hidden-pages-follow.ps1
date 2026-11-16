# Pages kept hidden do their counting when shown (WhenShown), as only a window shows it: that the
# Settings page and the Extra page are told, by the side bar and their views, each time they come to the front.
# A ring in the personal stash is moved twice between visits; on every visit to Settings the line drawn is the
# backups on disk, and the rune wizard draws its count.
#
# What the pages do when told they are shown and put away - counted on a visit, not behind another page - is
# proven without a window: tests/PD2SPCompanion.Shell.Tests/Home/HiddenPagesTests.

# The backups this program keeps, as the Settings line counts them.
function OnDisk { @(Get-ChildItem "$pad\saves\PD2SPCompanion Backups" -Recurse -Filter *.bak -ErrorAction SilentlyContinue).Count }
# The Settings line of the saves' backups: "52 files · 258.1 KB", or "None". By its id: the vault's copies have
# a line of the same words over it, and the first such line on the page would be read as this one.
function Shown {
  $line = ById 'Settings_BackupSummary'
  if (-not $line) { return $null }
  if ($line.Current.Name -eq 'None') { 0 } else { [int]($line.Current.Name -split ' ')[0] }
}
function VisitSettings([string]$when) {
  ClickEl (ById 'SideBar_Settings') "Settings"
  $disk = OnDisk
  $seen = WaitUntil { $s = Shown; if ($null -ne $s) { "$s" } } "the backup line ($when)"
  Check ([int]$seen -eq $disk) "Settings $when shows the backups on disk ($seen shown, $disk on disk)" | Out-Host
  [int]$seen
}
function VisitExtra([string]$when) {
  ClickEl (ById 'SideBar_Extra') "Extra"
  $summary = WaitUntil {
    @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object {
      -not $_.Current.IsOffscreen -and $_.Current.Name -match ' can be made$' })[0] } "the rune wizard ($when)"
  Check ([bool]$summary) "the rune wizard $when counts ($($summary.Current.Name))"
}

Case 'hidden-pages-follow.settings-and-rune-wizard-read-afresh-on-each-visit' -Tags hidden-pages, settings, extra -Changes {
  $first = VisitSettings "before the moves"
  VisitExtra "before the moves"

  ClickEl (ById 'SideBar_Inventory') "Items"
  WaitUntil { ItemsPage } "items page" | Out-Null
  Pane 0 'Personal Stash'
  $tile = WaitUntil { if (Tiles 'Steel Ring of Skill') { Tile 'Steel Ring of Skill' } } "the ring's tile"
  $cells = @((CellOf $tile), (CellOf $tile -1 0))
  $disk = OnDisk
  DragTo $cells[0] $cells[1]; Start-Sleep -Milliseconds 700
  DragTo $cells[1] $cells[0]; Start-Sleep -Milliseconds 700
  # The autosave's backups land a moment after the moves.
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ((OnDisk) -eq $disk -and $sw.ElapsedMilliseconds -lt 6000) { Start-Sleep -Milliseconds 200 }
  Step "backups on disk $disk -> $(OnDisk)"

  $after = VisitSettings "after the moves"
  if ((OnDisk) -ne $disk) { Check ($after -ne $first) "the count moved with the disk" }
  VisitExtra "after the moves"
}
