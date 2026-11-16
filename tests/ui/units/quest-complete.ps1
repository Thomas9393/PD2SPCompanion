# The Quests card's two squares, as only a window shows them (no marks, no confirm): a
# quest's complete and revert buttons are drawn on the line of its name, the complete button's tooltip says what
# completing does not give, and a click on each arrives - the strip saying what was done.
#
# What the card decides - the words on the button and the strip, the file byte for byte as it was after a
# revert, a quest whose points are spent refused with its reason - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Character/QuestCompleteTests.
#
# On the card pad, with a questless level 10 "Quester" made before the program starts; Free mode.

function Card($name) { foreach ($c in (All (Cond $AE::ControlTypeProperty $CT::ListItem))) { if (-not $c.Current.IsOffscreen -and $c.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $c } } }
function Retry([scriptblock]$find) { for ($n = 0; $n -lt 10; $n++) { try { return (& $find) } catch { Start-Sleep -Milliseconds 150 } } }
function Texts { Retry { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name }) } }
function Said([string]$pattern) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt 3000) { if (@(Texts | Where-Object { $_ -match $pattern }).Count -gt 0) { return $true }; Start-Sleep -Milliseconds 100 }
  $false
}
# A row's buttons: the complete and revert squares on the line of the quest's name.
function RowButton([string]$quest, [string]$tip) {
  $name = Retry { $script:win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
    (Cond $AE::NameProperty $quest), (Cond $AE::ControlTypeProperty $CT::Text)))) }
  if (-not $name) { return $null }
  $r = $name.Current.BoundingRectangle; $mid = $r.Y + $r.Height / 2
  Retry { @((All (Cond $AE::ControlTypeProperty $CT::Button)) |
    Where-Object { $_.Current.HelpText -match $tip -and -not $_.Current.BoundingRectangle.IsEmpty -and
                   [Math]::Abs($_.Current.BoundingRectangle.Y + $_.Current.BoundingRectangle.Height / 2 - $mid) -lt 14 })[0] }
}
function Saved { WaitUntil { (WrittenAt "$pad\saves\Quester.d2s") -ne $script:stamp } "the autosave" 6000 | Out-Null; $script:stamp = WrittenAt "$pad\saves\Quester.d2s" }

Case 'quest-complete.row-squares-complete-and-revert' -Tags character, quests -Pad card -Start Home -Before {
  Remove-Item "$pad\saves\Quester.*" -ErrorAction SilentlyContinue
  $corpus = $script:Corpus
  & $script:Cli create --class Sorceress --template "$corpus\saves\PhySpearZon.d2s" --out "$pad\saves\Quester.d2s" --level 10 | Out-Null
} {
  $script:stamp = WrittenAt "$pad\saves\Quester.d2s"
  ClickUntil { if ($c = Card 'Quester') { ClickEl $c "Quester's card" } } { ByName 'Open' } "Quester chosen" 800 | Out-Null
  ClickEl (ByName 'Open') "Load"
  WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "Quester loaded" | Out-Null
  ClickEl (ById 'SideBar_Char') "Character"

  # Cain: what completing does not give is on the button before, and on the strip after.
  $complete = WaitUntil { RowButton 'The Search for Cain' '^Complete' } "Cain's complete button"
  $tip = $complete.Current.HelpText -replace '\r?\n', ' | '
  Step "Cain's button: $tip"
  Check ($tip -match "Not given: Akara's ring\.") "the button says Akara's ring is not given"
  ClickEl $complete "complete Cain"
  Check (Said "^The Search for Cain completed\. Not given: Akara's ring\.$") "completed at once; the strip says what is not given"
  Check (Said '^Done$') "Cain reads Done"
  Saved

  $revert = WaitUntil { RowButton 'The Search for Cain' '^Revert$' } "Cain's revert button"
  ClickEl $revert "revert Cain"
  Check (Said '^The Search for Cain reverted\.$') "reverted"
  Check ($null -eq (RowButton 'The Search for Cain' '^Revert$')) "the revert gone with it"
  Check ($null -ne (RowButton 'The Search for Cain' '^Complete')) "Cain can be completed again"
}
