# The Iron Golem beside the mercenary, as only a window shows it: its
# button in the mercenary's header, its item and Dismiss drawn in the section, and a click reaching each.
#
# What the page decides - the button only while a golem stands, the item chosen and never deleted, put away and
# brought back, Dismiss handing the item to the character and the undo standing the golem up again, the golem
# going with the item that gives its skill - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Mercenary/IronGolemTests.
#
# On the card pad, with a "Golemancer" made fresh for each case (pd2spc create --golem 7s7).

Add-Type -AssemblyName System.Windows.Forms
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }
function Card($name) { foreach ($c in (All (Cond $AE::ControlTypeProperty $CT::ListItem))) { if (-not $c.Current.IsOffscreen -and $c.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $c } } }
# The character chosen on the home page and loaded, the Items page shown.
function Open($name) {
  ClickUntil { if ($c = Card $name) { ClickEl $c "$name's card" } } { ByName 'Open' } "$name chosen" 800 | Out-Null
  ClickEl (ByName 'Open') "Load"
  WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "$name loaded" | Out-Null
  ClickEl (ById 'SideBar_Inventory') "Items"
  WaitUntil { ItemsPage } "items page" | Out-Null
}
function Wheel([int]$notches) {
  [Fast]::SetCursorPos([int]($script:r.X + $script:r.Width * 0.25), [int]($script:r.Y + $script:r.Height * 0.5)) | Out-Null
  1..[Math]::Abs($notches) | ForEach-Object { [Fast]::mouse_event(0x0800, 0, 0, $(if ($notches -lt 0) { -120 } else { 120 }), 0); Start-Sleep -Milliseconds 100 }
}
# A Necromancer standing an Iron Golem made of a Balrog Spear, on the pad the case runs on.
function MakeGolemancer {
  $corpus = $script:Corpus
  Remove-Item "$pad\saves\Golemancer.*" -ErrorAction SilentlyContinue
  $made = & $script:Cli create --class Necromancer --template "$corpus\saves\PhySpearZon.d2s" --out "$pad\saves\Golemancer.d2s" --level 60 --quests --golem 7s7 2>&1 | Out-String
  if ($made -notmatch 'iron golem\s+made of 7s7') { throw "Golemancer was not made: $made" }
}

Case 'golem-panel.button-shows-and-puts-away-its-item' -Tags mercenary, golem, items -Pad card -Start Home -Before { MakeGolemancer } {
  Open 'Golemancer'
  # The mercenary's section is the page's last: wheeled into view.
  Wheel -8

  $button = WaitUntil { ByTip 'Iron Golem' } "the golem's button"
  Check ($null -ne (Tile 'Balrog Spear')) "shown to begin with: the golem's item is beside the mercenary"
  Check ($null -ne (ByTip 'Dismiss Iron Golem')) "and its Dismiss"

  ClickEl (Tile 'Balrog Spear') "the golem's item"
  Check ($null -ne (WaitUntil { ByName 'Code: 7s7' -Prefix } "the Edit page")) "chosen: the Edit page shows it"

  # Its menu, by the right button, which its tooltip names: the golem's dismissal, and nothing that would be
  # refused (a right click on it opened nothing).
  MenuOf (Tile 'Balrog Spear') "the golem's item"
  Check ((MenuWords) -eq 'Dismiss Iron Golem') "right click on the golem's item: its menu ('$(MenuWords)')"
  Keys '{ESC}'
  WaitUntil { if (-not (MenuUp)) { 'shut' } } "the menu shut" 3000 | Out-Null

  ClickEl $button "the golem's button"
  WaitUntil { -not (ByTip 'Dismiss Iron Golem') } "the golem put away" | Out-Null
  Check ($null -eq (Tile 'Balrog Spear')) "put away: its item is not shown"
  Check ($null -eq (ByName 'Code: 7s7' -Prefix)) "and lets go of the selection"
  ClickEl (ByTip 'Iron Golem') "the golem's button again"
  Check ($null -ne (WaitUntil { ByTip 'Dismiss Iron Golem' } "the golem shown again")) "the button again: shown again"
}

Case 'golem-panel.dismiss-is-clicked-and-the-strip-says-where' -Tags mercenary, golem, items -Pad card -Start Home -Before { MakeGolemancer } {
  Open 'Golemancer'
  Wheel -8

  ClickEl (WaitUntil { ByTip 'Dismiss Iron Golem' } "Dismiss") "Dismiss"
  WaitUntil { -not (ByTip 'Iron Golem') } "the golem's button gone" | Out-Null
  Check ($null -eq (ByTip 'Dismiss Iron Golem')) "dismissed: no golem beside the mercenary"
  Check ($null -ne (WaitUntil { ByName 'Iron Golem dismissed: Balrog Spear in inventory.' } "the strip")) "the strip says where its item went"
  Wheel 8
  Check ($null -ne (WaitUntil { Tile 'Balrog Spear' } "the spear in the inventory")) "and the Balrog Spear is in the inventory"

  ClickEl (WaitUntil { $b = ByTip 'Undo ' -Prefix; if ($b -and $b.Current.IsEnabled) { $b } } "Undo") "Undo"
  Wheel -8
  Check ($null -ne (WaitUntil { ByTip 'Dismiss Iron Golem' } "the golem back")) "undo: the golem stands again"
}
