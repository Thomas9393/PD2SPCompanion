# The Experimental page's drop rates panel, as only a window shows it: that the page's field opens the panel, with its presets open and its lists
# folded, and that a row's box, the name box, the Keep button and a preset's bin - which asks first - reach it.
#
# What the panel decides - the odds before and after, a preset kept, chosen again, updated in place, deleted,
# gone for good - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Experimental/DropRatesPanelTests.
#
# Never presses APPLY, so nothing reaches the game folder.

function SettingsText { [IO.File]::ReadAllText((SettingsFile)) }

# The multiple box level with a row's or a header's words: the nearest number box on screen by height.
function BoxBeside([string]$name) {
  $label = ByName $name
  $y = $label.Current.BoundingRectangle.Y + $label.Current.BoundingRectangle.Height / 2
  @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen } |
    Sort-Object { [Math]::Abs(($_.Current.BoundingRectangle.Y + $_.Current.BoundingRectangle.Height / 2) - $y) })[0]
}
# The bin of the row a name is on: the name is the first of the row's two lines, the bin stands at the row's middle.
# Of every text of that name the one with a bin beside it: the name is said elsewhere too - the section's line of the
# preset in use, the page's field - and the first of them led to another row's bin.
function BinOnRow([string]$label) {
  $bins = @((All (Cond $AE::HelpTextProperty 'Delete preset')) | Where-Object { -not $_.Current.IsOffscreen })
  # Leftmost first: a row's name begins at the list's left edge; another text of the name stood beside a bin too.
  foreach ($row in @(All (Cond $AE::NameProperty $label) | Sort-Object { $_.Current.BoundingRectangle.X })) {
    if ($row.Current.IsOffscreen) { continue }
    $r = $row.Current.BoundingRectangle; $mid = $r.Y + $r.Height / 2
    $bin = @($bins | Where-Object { $b = $_.Current.BoundingRectangle; [Math]::Abs($b.Y + $b.Height / 2 - $mid) -lt 26 -and $b.X -gt $r.X })[0]
    if ($bin) { return $bin }
  }
}
function SetMultiple($box, [string]$value) {
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
  Start-Sleep -Milliseconds 300
}
# The Experimental page, and its drop rates field.
function DropField {
  ClickUntil { ClickEl (WaitUntil { ById 'SideBar_Experimental' } "Experimental") "Experimental" } { ByName 'Modded single player' } "the Experimental page" 1500 | Out-Null
  WaitUntil { ByTip 'Special items, runes and gems' } "the drop rates field"
}

Case 'drop-panel.field-opens-the-panel-on-its-presets' -Tags drop-panel, experimental {
  $field = DropField
  # What the game's files hold now: Vanilla on a clean install - the developer's may have a pack in.
  $said = @($field.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
  Check ($said.Count -gt 0) "the page's field names the rates in use ('$($said -join ' | ')')"

  ClickEl $field "drop rates"
  WaitUntil { ByName 'Every entry 1x' } "the presets, open" | Out-Null
  # No box takes the focus as it opens - a number's, where a key pressed next changed a rate, nor the preset's name.
  Start-Sleep -Milliseconds 500
  Check ((Focused).Current.ControlType -ne $CT::Edit) "the panel opens with no box focused ('$((Focused).Current.ControlType.ProgrammaticName)')"
  Check ($null -eq (ByName "Larzuk's Puzzlebox")) "special items folded"
  Check ($null -eq (ByName 'Lem Rune')) "runes and gems folded"
  ClickEl (ByTip 'Close') "close the panel"
  Start-Sleep -Milliseconds 400
}

Case 'drop-panel.box-keep-and-bin-reach-the-presets' -Tags drop-panel, experimental -Changes {
  $name = 'UI drop preset'
  Check (-not ((SettingsText) -match [regex]::Escape($name))) "start: no preset named '$name'"

  ClickEl (DropField) "drop rates"
  WaitUntil { ByName 'Every entry 1x' } "the presets, open" | Out-Null
  ClickEl (WaitUntil { ByName 'Special items' } "the special items header") "unfold special items"
  WaitUntil { ByName "Larzuk's Puzzlebox" } "the rows" | Out-Null

  # A change of one row - typed into its box - makes it no preset; kept under a name, it is in the settings.
  SetMultiple (BoxBeside "Larzuk's Puzzlebox") '20'
  # the preset name box: the box on the Keep button's line, left of it - the right page has boxes of its own
  $keep = WaitUntil { ByTip 'Keep preset' } "the Keep button"
  $k = $keep.Current.BoundingRectangle; $mid = $k.Y + $k.Height / 2
  $box = WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) |
    Where-Object { $r = $_.Current.BoundingRectangle; -not $_.Current.IsOffscreen -and [Math]::Abs($r.Y + $r.Height / 2 - $mid) -lt 12 -and $r.X -lt $k.X })[0] } "the name box"
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($name)
  WaitUntil { $k = ByTip 'Keep preset'; if ($k -and $k.Current.IsEnabled) { $k } } "Keep enabled" | Out-Null
  # Enter in the name box keeps, as the Keep button does (it stepped into the rows).
  ClickEl $box "the name box"
  [Fast]::keybd_event(0x0D, 0, 0, 0); Start-Sleep -Milliseconds 40; [Fast]::keybd_event(0x0D, 0, 2, 0)
  WaitUntil { ByName $name } "the kept preset in the list" | Out-Null
  Check ((SettingsText) -match [regex]::Escape($name)) "Enter in the name box keeps: written to the settings"

  # Deleted by its own bin - which asks first: one press leaves it, the second deletes.
  ClickEl (WaitUntil { BinOnRow $name } "the kept preset's bin") "delete (asks)"
  Start-Sleep -Milliseconds 300
  Check ($null -ne (ByName $name) -and (SettingsText) -match [regex]::Escape($name)) "one press only asks: the preset stays"
  ClickEl (WaitUntil { BinOnRow $name } "the kept preset's bin, asking") "delete (confirmed)"
  # By its row: the name box, where Enter left the caret, still says the name.
  WaitUntil { -not (BinOnRow $name) } "the preset gone" | Out-Null
  Check (-not ((SettingsText) -match [regex]::Escape($name))) "deleted: gone from the settings"
  Check ($null -eq (BinOnRow 'Vanilla')) "no bin on Vanilla"

  ClickEl (ByTip 'Close') "close the panel"
  Start-Sleep -Milliseconds 400
}
