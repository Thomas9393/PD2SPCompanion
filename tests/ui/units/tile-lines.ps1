# What a tile's tooltip says of a Based on Character Level line and of a loose gem or rune, as a window draws it: that
# the page hands the holder's level to a real tile, and that a materials tab's slot and a rune in a grid word their
# effects. The wording itself, the rounding, and a tile with no holder are proven without a window:
# tests/PD2SPCompanion.Tests/Items/ItemLinesTests and tests/PD2SPCompanion.Shell.Tests/Items/TileLevelLinesTests.
#
# On the pad's PhySpearZon: a Hel Rune in the inventory, El Runes in the shared stash's materials tab.

function LevelOf { if ((& $script:Cli dump "$pad\saves\PhySpearZon.d2s" --no-stats 2>&1 | Out-String) -match '(?im)\blevel\s*:?\s*(\d+)') { [int]$Matches[1] } }
# The lines of the tooltip up: nothing while none is, so a wait can ask again.
function TipLines {
  $tips = @($script:win.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::ToolTip)))
  if ($tips.Count -ne 1) { return @() }
  @($tips[0].FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name })
}
function HoverFor($el, [scriptblock]$said, [string]$what) {
  $b = $el.Current.BoundingRectangle
  Front
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2) - 4, [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 80
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null
  WaitUntil { $lines = TipLines; if ($lines.Count -gt 0 -and (& $said $lines)) { $lines } } $what 5000
}
function SaysEachHost($lines) { ($lines -contains 'Weapon') -and ($lines -contains 'Armor, Helm') -and ($lines -contains 'Shield') }

Case 'tile-lines.a-held-items-per-level-line-says-its-total-at-the-characters-level' -Tags tooltip, items -Before {
  $kept = "$pad\tile-lines-before"
  Remove-Item $kept -Recurse -Force -ErrorAction SilentlyContinue
  & $script:Cli give "$pad\saves\PhySpearZon.d2s" --runeword 'Enigma:xtp' --backup $kept | Out-Null
} {
  $level = LevelOf
  Check ($level -gt 1) "start: the character is level $level"
  $want = "+$([Math]::Floor($level * 0.75)) to Strength (Based on Character Level)"
  $tile = WaitUntil { Tile 'Enigma' } "the given Enigma, in the inventory"
  $lines = HoverFor $tile { param($l) @($l | Where-Object { $_ -like "*to Strength (Based on Character Level)*" }).Count -gt 0 } "the Strength line"
  $line = @($lines | Where-Object { $_ -like "*to Strength (Based on Character Level)*" })[0]
  Check ($line -like "$want*") "the tooltip says '$line' (wanted '$want')"
}

Case 'tile-lines.a-rune-in-a-grid-says-what-it-gives-in-each-host' -Tags tooltip, items, sockets {
  $tile = WaitUntil { Tile 'Hel Rune' } "the inventory's Hel Rune"
  $lines = HoverFor $tile { param($l) SaysEachHost $l } "Weapon, Armor, Helm and Shield in the tooltip"
  Check (SaysEachHost $lines) "the Hel Rune's tooltip words each host: $($lines -join ' | ')"
}

Case 'tile-lines.a-materials-tab-rune-says-what-it-gives-and-the-edit-page-too' -Tags tooltip, items, materials, sockets {
  if ((PaneCount) -lt 2) { ToggleSecondPane }
  Pane 1 'Materials'
  $from = (PaneLine 1).Current.BoundingRectangle.X - 20
  $rune = { @((All (Cond $AE::NameProperty 'El Rune')) | Where-Object { $_.Current.AutomationId -eq 'Tile' -and -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt $from })[0] }
  $lines = HoverFor (WaitUntil $rune "the tab's El Rune") { param($l) SaysEachHost $l } "Weapon, Armor, Helm and Shield in the tooltip"
  Check (SaysEachHost $lines) "the tab's El Rune words each host: $($lines -join ' | ')"

  ClickUntil { ClickEl (& $rune) "the tab's El Rune" } { ByName 'In a socket' } "the Edit page's In a socket section" 800 | Out-Null
  Check ([bool](ByName 'In a socket')) "selected from the tab, the Edit page says what it gives in a socket"
}
