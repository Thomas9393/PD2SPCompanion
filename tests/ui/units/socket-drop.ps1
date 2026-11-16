# Socketing by drag, as only a window shows it: that a
# drag let go of over an item ends in a socketing - a count carried out of the materials tab, a rune carried from
# a grid onto an item in another grid, and a rune let go of on a worn item. The release finds what is under the
# pointer in the view (a tile hides its cell, a worn slot has none) and hands it to the page.
#
# What the page then decides - one of a stack and the rest back, the last rune making the runeword, an item with no
# socket left, every word on the strip - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Edit/SocketDropTests.
#
# In Legit: the game's own action, which costs the rune. On the pad's PhySpearZon: a Hel Rune in the inventory, a
# Veil of Steel with one empty socket on its head, Tir and El Runes in the materials tab.

function Dump { @((& $script:Cli dump "$pad\saves\PhySpearZon.d2s" 2>&1 | Out-String) -split "`n") }
# An item of the save by name: the section it stands under and its "n/m sockets" - or nothing.
function Item([string]$name) {
  $section = ''; $lines = Dump
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*== (.+?) \(\d+\)') { $section = $Matches[1]; continue }
    if ($lines[$i] -match '^\s*(?:#\d+)?\s+\[\w+\s*\]\s+(.*?)\s{2,}\(' -and $Matches[1].Trim() -eq $name) {
      $facts = "$($lines[$i + 1])"
      return [pscustomobject]@{ Where = $section; Sockets = $(if ($facts -match '(\d+/\d+) sockets') { $Matches[1] }); Runeword = ($facts -match 'runeword \d+') }
    }
  }
}
function FirstOf([string]$wanted) {
  $section = ''
  foreach ($line in Dump) {
    if ($line -match '^\s*== (.+?) \(\d+\)') { $section = $Matches[1]; continue }
    if ($section -eq $wanted -and $line -match '^\s*(?:#\d+)?\s+\[\w+\s*\]\s+(.*?)\s{2,}\(') { return $Matches[1].Trim() }
  }
}
function Until([scriptblock]$cond, [int]$ms = 6000) { try { WaitUntil $cond "the files" $ms | Out-Null; $true } catch { $false } }
# What a strip says, waited for.
function Says([string]$text, [int]$ms = 5000) { try { [bool](WaitUntil { ByName $text } "the strip: $text" $ms) } catch { $false } }
# The tiles of a name on screen, each with the count drawn in it and which pane it is in.
function Stacks([string]$name) {
  # A tile redrawn while it is read is gone from under the reader: it is left out, and the caller asks again.
  @((All (Cond $AE::NameProperty $name)) | ForEach-Object {
      try {
        if ($_.Current.AutomationId -ne 'Tile' -or $_.Current.IsOffscreen) { return }
        $count = @($_.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match '^\d+$' })[0]
        [pscustomobject]@{ Tile = $_; Count = $(if ($null -ne $count) { [int]$count } else { 1 }); Second = ($_.Current.BoundingRectangle.X -gt $script:secondFrom) }
      } catch { } })
}
function InSecond([string]$name) { Stacks $name | Where-Object Second | Select-Object -First 1 }
function SecondPane([string]$choice) {
  Pane 1 $choice
  Start-Sleep -Milliseconds 500
  $script:secondFrom = (PaneLine 1).Current.BoundingRectangle.X - 2
}
# The personal stash in the first pane, and a second pane beside it.
function TwoPanes {
  Pane 0 'Personal Stash'
  $panes = (PaneCount)
  if ($panes -lt 2) { ToggleSecondPane }
}
# A plain base with so many sockets into the pad's character, before the program starts: it lands in the stash.
function Give([string]$base) {
  $kept = "$pad\socket-drop-before"
  Remove-Item $kept -Recurse -Force -ErrorAction SilentlyContinue
  & $script:Cli give "$pad\saves\PhySpearZon.d2s" --base $base --backup $kept | Out-Null
}

Case 'socket-drop.tab-count-dragged-onto-an-item' -Tags sockets, items -Mode Legit -Before { Give 'ssd:2' } {
  Check ((Item 'Short Sword').Sockets -eq '0/2') "start: a Short Sword 0/2 given"
  TwoPanes
  SecondPane 'Materials'
  $tir = (WaitUntil { InSecond 'Tir Rune' } "the tab's Tir Rune").Count
  DragTo (InSecond 'Tir Rune').Tile (WaitUntil { Tile 'Short Sword' } "the Short Sword") -Hold 400
  Check (Says 'Tir Rune socketed into Short Sword.') "the tab's Tir Rune let go of on the Short Sword: 'Tir Rune socketed into Short Sword.'"
  Check (Until { (InSecond 'Tir Rune').Count -eq $tir - 1 }) "one out of the tab ($tir -> $((InSecond 'Tir Rune').Count))"
  Check (Until { (Item 'Short Sword').Sockets -eq '1/2' }) "the save has the sword at $((Item 'Short Sword').Sockets)"
}

Case 'socket-drop.rune-dragged-onto-an-item-in-a-grid' -Tags sockets, items -Mode Legit -Before { Give 'buc:1' } {
  Check ((Item 'Buckler').Sockets -eq '0/1') "start: a Buckler 0/1 given"
  TwoPanes
  SecondPane 'Inventory'
  DragTo (WaitUntil { InSecond 'Hel Rune' } "the inventory's Hel Rune").Tile (WaitUntil { Tile 'Buckler' } "the Buckler") -Hold 500
  Check (Says 'Hel Rune socketed into Buckler.') "the inventory's Hel Rune let go of on the Buckler: 'Hel Rune socketed into Buckler.'"
  Check (Until { (Item 'Buckler').Sockets -eq '1/1' }) "the save has the Buckler at $((Item 'Buckler').Sockets)"
}

Case 'socket-drop.rune-dragged-onto-a-worn-item' -Tags sockets, items -Mode Legit -Changes {
  $helm = FirstOf 'equipped: Head'
  Check ((Item $helm).Sockets -eq '0/1') "start: the worn $helm 0/1"
  TwoPanes
  SecondPane 'Inventory'
  # The worn one: the first of its name from the top, over the grids.
  DragTo (WaitUntil { InSecond 'Hel Rune' } "the inventory's Hel Rune").Tile (WaitUntil { Tile $helm } "the worn $helm") -Hold 500
  Check (Says "Hel Rune socketed into $helm.") "the inventory's Hel Rune let go of on the worn $helm`: 'Hel Rune socketed into $helm.'"
  Check (Until { (Item $helm).Sockets -eq '1/1' -and (Item $helm).Where -eq 'equipped: Head' }) "the save has it worn, $((Item $helm).Sockets)"
}
