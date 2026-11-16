# The item panel's property picker, as only a window shows it: that Add property opens the picker over the Edit
# page, its lines drawn each with its id, that a click on a line adds its row and closes the picker, and that the
# Custom panel's "Raw property list" switch is reached and is the picker's the next time it opens, with no restart.
#
# What the picker lists and what a pick adds - a line per tree, class and skill, each arriving with its own
# chosen, the id finding every line, the raw list's one line a stat by the engine's name - is proven without a
# window: tests/PD2SPCompanion.Shell.Tests/Items/PropertyLinesTests.
#
# On the pad's ring, in the personal stash. What is picked is kept as it is made, so the case that picks changes
# the save, and the raw list's changes the settings; the one that only looks turns its pane back to the inventory.

# The picker's search box: the one wide text box while it is open - the window's own search box aside
# (Quick_Box).
function SearchBox {
  foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Edit))) {
    if ($e.Current.AutomationId -eq 'Quick_Box') { continue }
    if (-not $e.Current.IsOffscreen -and $e.Current.BoundingRectangle.Width -gt 200) { $last = $e }
  }
  $last
}
function Texts { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen }) }
function Search([string]$typed) {
  ClickEl (WaitUntil { ByTip 'Add property' } "Add property") "add property"
  $box = WaitUntil { SearchBox } "the picker's search"
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($typed)
}
function Pick([string]$typed, [string]$line) {
  Search $typed
  $option = WaitUntil { Texts | Where-Object { $_.Current.Name -eq $line } | Select-Object -First 1 } "the line '$line'"
  ClickEl $option "pick '$line'"
}

# The Edit page of the pad's ring.
function OpenRing {
  Pane 0 'Personal Stash'
  ClickEl (WaitUntil { if (Tiles 'Steel Ring of Skill') { Tile 'Steel Ring of Skill' } } "a ring") "the ring"
  ClickEl (WaitUntil { ById 'Bookmark_Edit' } "the Edit bookmark") "Edit"
}

Case 'property-lines.picker-opens-and-lists-a-line-a-tree' -Tags properties, items {
  OpenRing

  # The id still finds the stat: every line of it, each saying its id.
  Search '188'
  $lines = WaitUntil { $found = @(Texts | Where-Object { $_.Current.Name -eq 'stat 188' }); if ($found.Count -ge 3) { $found } } "the skill-tree lines"
  Check ($lines.Count -ge 3) "searching 188 lists the tree lines ($($lines.Count) on screen)"
  Check (-not (Texts | Where-Object { $_.Current.Name -eq '+X to Javelin and Spear Skills' })) "no line names one tree for the whole stat"
  ClickEl (ByTip 'Close') "close the picker"
  # The picker fades out before the left page is clicked again.
  Start-Sleep -Milliseconds 500
  Pane 0 'Inventory'
}

Case 'property-lines.line-clicked-adds-its-row' -Tags properties, items -Changes {
  OpenRing
  Pick 'Bow and Crossbow' '+X to Bow and Crossbow Skills (Amazon Only)'
  Check ([bool](WaitUntil { ByName '+1 to Bow and Crossbow Skills (Amazon Only)' } "the tree row")) "the tree line adds its tree"
}

# The raw list: Settings, Custom, the switch, SAVE - the mode is Custom then, as no preset has it.
Case 'property-lines.raw-switch-reaches-the-picker' -Tags properties, play-mode, settings -Changes {
  OpenRing
  $settingsFile = SettingsFile
  ClickEl (ById 'SideBar_Settings') "Settings"
  ClickEl (WaitUntil { ByName 'CUSTOM' } "the Custom button") "Custom"
  # The switch by its own id and name (Policy_Switch), not by where it sits, level with its label.
  $switch = New-Object System.Windows.Automation.AndCondition((Cond $AE::AutomationIdProperty 'Policy_Switch'), (Cond $AE::NameProperty 'Raw property list'))
  $tick = WaitUntil { OnScreen $switch } "the Raw property list switch"
  $toggle = [System.Windows.Automation.TogglePattern]::Pattern
  Check ($tick -and $tick.GetCurrentPattern($toggle).Current.ToggleState -eq 'Off') "raw: the switch is off in Free"
  ClickEl $tick "Raw property list on"
  Check ($tick.GetCurrentPattern($toggle).Current.ToggleState -eq 'On') "raw: the switch turns on"
  ClickEl (WaitUntil { ByName 'SAVE' } "SAVE") "save the Custom mode"
  Check ([bool](WaitUntil { [IO.File]::ReadAllText($settingsFile) -match '"RawProperties": true' } "the mode saved")) "raw: saved as Custom with the switch on"
  # Live, no restart: back to the same ring's Edit page, and the picker asks the mode as it opens.
  ClickEl (ById 'SideBar_Inventory') "Items"
  WaitUntil { ByTip 'Add property' } "the ring's Edit page again" | Out-Null
  Search '188'
  $raw = WaitUntil { $found = @(Texts | Where-Object { $_.Current.Name -eq 'item_addskill_tab' }); if ($found.Count -ge 1) { $found } } "the raw line"
  Check ($raw.Count -eq 1) "raw: searching 188 lists the stat once, by the engine's name ($($raw.Count))"
  ClickEl (ByTip 'Close') "close the picker"
}
