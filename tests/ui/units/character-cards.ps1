# The Character page's three folding cards, as only a window shows them: Quests, then
# Waypoints, then Map seed under the character card, their content drawn in the cards; a heading's click folds
# what is drawn; Ctrl and Shift held on Grant and Remove reach the waypoints' card; the map seed's boxes take
# what is typed on Enter, its dice and its list's rows and bins take their clicks.
#
# What the cards decide - which waypoints a reach grants and takes back, the camp that stays, what a seed typed,
# rolled, kept, chosen and forgotten does to the character and the list - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Character/CharacterCardsTests.
#
# On the card pad, with a fresh level 30 "Carder" made before the program starts; Free mode.

Add-Type -AssemblyName System.Windows.Forms
function Card($name) { foreach ($c in (All (Cond $AE::ControlTypeProperty $CT::ListItem))) { if (-not $c.Current.IsOffscreen -and $c.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $c } } }
function Retry([scriptblock]$find) { for ($n = 0; $n -lt 10; $n++) { try { return (& $find) } catch { Start-Sleep -Milliseconds 150 } } }
function Texts { Retry { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) } }
function HasText([string]$pattern) { @(Texts | Where-Object { $_ -match $pattern }).Count -gt 0 }
function Expect([string]$pattern, [string]$what) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt 3000) { if (HasText $pattern) { break }; Start-Sleep -Milliseconds 100 }
  Check (HasText $pattern) $what
}
function Text([string]$name) { Retry { OnScreen (Cond $AE::NameProperty $name) } }

# A press as a hand makes it: on the element, a moment's hover, then the modifier and the click.
function Press($el, [string]$mod = '') {
  $b = $el.Current.BoundingRectangle; $x = [int]($b.X + $b.Width / 2); $y = [int]($b.Y + $b.Height / 2)
  Front; [Fast]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 250
  $key = @{ Shift = 0x10; Ctrl = 0x11 }[$mod]
  if ($key) { [Fast]::keybd_event($key, 0, 0, 0); Start-Sleep -Milliseconds 40 }
  [Fast]::Click($x, $y)
  if ($key) { Start-Sleep -Milliseconds 40; [Fast]::keybd_event($key, 0, 2, 0) }
  Start-Sleep -Milliseconds 200
}
# The map seed's boxes: the edits on the "Seed" label's line, left to right - the seed, then the name.
function SeedBoxes {
  $label = (Text 'Seed').Current.BoundingRectangle; $mid = $label.Y + $label.Height / 2
  Retry { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) |
    Where-Object { $r = $_.Current.BoundingRectangle; [Math]::Abs($r.Y + $r.Height / 2 - $mid) -lt 16 } |
    Sort-Object { $_.Current.BoundingRectangle.X }) }
}
Add-Type @"
using System.Runtime.InteropServices;
public static class Wheel32 { [DllImport("user32.dll")] public static extern void mouse_event(int f, int x, int y, int d, int e); }
"@ -ErrorAction SilentlyContinue
# The mouse wheel over an element, notches down when negative.
function Wheel($el, [int]$notches) {
  $b = $el.Current.BoundingRectangle
  Front; [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 100
  [Wheel32]::mouse_event(0x0800, 0, 0, 120 * $notches, 0); Start-Sleep -Milliseconds 400
}
function Val($e) { try { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { "?" } }

$makeCarder = {
  Remove-Item "$pad\saves\Carder.*" -ErrorAction SilentlyContinue
  $corpus = $script:Corpus
  & $script:Cli create --class Amazon --template "$corpus\saves\PhySpearZon.d2s" --out "$pad\saves\Carder.d2s" --level 30 | Out-Null
}
# Carder chosen in the list and loaded, the Character page in front.
function OpenCarder {
  ClickUntil { if ($c = Card 'Carder') { ClickEl $c "Carder's card" } } { ByName 'Open' } "Carder chosen" 800 | Out-Null
  ClickEl (ByName 'Open') "Load"
  WaitUntil { (ById 'SideBar_Char').Current.IsEnabled } "Carder loaded" | Out-Null
  ClickEl (ById 'SideBar_Char') "Character"
  # Read once the page has settled: during its cross-fade the cards are still being laid out, and a first read
  # found them out of order (2 in 6 runs).
  function InOrder { $q = Text 'Quests'; $w = Text 'Waypoints'; $m = Text 'Map seed'
    if ($q -and $w -and $m -and $q.Current.BoundingRectangle.Y -lt $w.Current.BoundingRectangle.Y -and $w.Current.BoundingRectangle.Y -lt $m.Current.BoundingRectangle.Y) { 'yes' } }
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt 3000 -and -not (Invoke-Cond { InOrder })) { Start-Sleep -Milliseconds 100 }
  WaitUntil { Text 'Quests' } "the Quests card" | Out-Null
}

Case 'character-cards.three-cards-in-order-and-their-headings-fold' -Tags character, character-cards -Pad card -Start Home -Before $makeCarder {
  OpenCarder
  $q = Text 'Quests'; $w = Text 'Waypoints'; $m = Text 'Map seed'
  Check ($q.Current.BoundingRectangle.Y -lt $w.Current.BoundingRectangle.Y -and $w.Current.BoundingRectangle.Y -lt $m.Current.BoundingRectangle.Y) "Quests, then Waypoints, then Map seed"
  Check ($null -ne (WaitUntil { Text 'Den of Evil' } "Act I's quests")) "the quests open, Act I shown"
  Check ($null -ne (Text 'Rogue Encampment')) "the waypoints open"
  Check ($null -ne (Text 'Seed')) "the map seed open"
  Press $q; Check ($null -eq (Text 'Den of Evil')) "the quests fold"
  Press (Text 'Map seed'); Check ($null -eq (Text 'Seed')) "the map seed folds"
}

Case 'character-cards.held-keys-reach-grant-and-remove' -Tags character, character-cards, waypoints -Pad card -Start Home -Before $makeCarder {
  OpenCarder
  # The quests and the map seed folded, so the waypoints' steps are on screen.
  Press (Text 'Quests'); Press (Text 'Map seed')

  # A new level 30 holds Act I's camp only.
  Expect '^Act I\W+1 ?/ ?9$' "Act I: the camp only"
  Press (Text 'GRANT'); Expect '^Act I\W+9 ?/ ?9$' "Grant: all of Act I"
  Expect 'Normal 9 / 39' "the summary follows"
  Press (Text 'GRANT') 'Ctrl'; Expect 'Normal 39 / 39' "Ctrl + Grant: every act of Normal"
  Expect 'Nightmare 0 / 39|Nightmare 1 / 39' "Nightmare untouched"
  Press (Text 'REMOVE') 'Shift'; Expect '^Act I\W+1 ?/ ?9$' "Shift + Remove: everything, Act I's camp kept"
  Expect 'Normal 1 / 39' "Normal back to its camp"
}

Case 'character-cards.map-seed-boxes-dice-and-list-take-the-hand' -Tags character, character-cards, map-seed -Pad card -Start Home -Typing -Before $makeCarder {
  OpenCarder
  # The quests and the waypoints folded, so the map seed's list is on screen.
  Press (Text 'Quests'); Press (Text 'Waypoints')

  $boxes = WaitUntil { $b = SeedBoxes; if (@($b).Count -ge 2) { , $b } } "the seed and name boxes"
  $was = Val $boxes[0]; Step "seed at start: $was"
  Press $boxes[0]; [System.Windows.Forms.SendKeys]::SendWait("^a4242{ENTER}")
  Expect '^4242$' "typed 4242 and Enter: the card says 4242"
  Press $boxes[1]; [System.Windows.Forms.SendKeys]::SendWait("^aUI test seed{ENTER}")
  Expect '^4242\W+UI test seed$' "named and kept with Enter: the heading names it"
  Expect '^UI test seed$' "the kept list holds it"
  Press (ByTip 'Random seed'); Start-Sleep -Milliseconds 300
  $rolled = Val (SeedBoxes)[0]
  Check ($rolled -ne '4242' -and $rolled -match '^\d+$') "the dice puts a new seed on ($rolled)"
  # The player's own kept seeds are in the list too, and it scrolls in its card: the page to its end, then the
  # list - the wheel just under its heading.
  Wheel (Text 'Kept seeds') -40
  $under = (Text 'Kept seeds').Current.BoundingRectangle
  Front; [Fast]::SetCursorPos([int]($under.X + 200), [int]($under.Y + $under.Height + 40)) | Out-Null; Start-Sleep -Milliseconds 100
  [Wheel32]::mouse_event(0x0800, 0, 0, -120 * 40, 0); Start-Sleep -Milliseconds 400
  $row = Text 'UI test seed'; Press $row; Expect '^4242\W+UI test seed$' "a kept seed clicked: the character goes onto it"
  # The bin arms on the first click and removes on the second, as every bin does. The bin on the test seed's own
  # row, by position: UI Automation reports the list's bins with no bounds. The row's own text, as the name box
  # holds the same words; the bin is just right of the row's button.
  $rowText = Retry { $script:win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
    (Cond $AE::NameProperty 'UI test seed'), (Cond $AE::ControlTypeProperty $CT::Text)))) }
  $r = $rowText.Current.BoundingRectangle
  $bin = [System.Windows.Rect]::new($r.X + $r.Width + 8, $r.Y - 4, 32, $r.Height + 8)
  $hit = [pscustomobject]@{ Current = [pscustomobject]@{ BoundingRectangle = $bin } }
  Press $hit; Press $hit; Start-Sleep -Milliseconds 300
  Check (-not (HasText '^UI test seed$')) "Remove seed: gone from the list"
}
