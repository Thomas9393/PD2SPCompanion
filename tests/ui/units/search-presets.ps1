# The search's presets, as only a window shows them (in the panel): where
# the presets' section is drawn - above the form, its labels on one left edge with the form's - and that a click
# reaches Keep, a kept preset's row and its bin, the name box and the kept row being the view's to draw.
#
# What the presets decide - "No preset" on the header, when Keep is lit and what it says it will do, a kept preset
# being the search at once, an update in place, a rename, the bin asking first, what is written to the settings -
# is proven without a window: tests/PD2SPCompanion.Shell.Tests/Search/SearchPresetsTests.
#
# The boxes are set through UI Automation: nothing is typed through the keyboard.

function Retry([scriptblock]$find) { for ($n = 0; $n -lt 10; $n++) { try { return (& $find) } catch { Start-Sleep -Milliseconds 150 } } }
function Texts { Retry { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) } }
function HasText([string]$pattern) { @(Texts | Where-Object { $_ -match $pattern }).Count -gt 0 }
function Expect([string]$pattern, [string]$what) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt 3000) { if (HasText $pattern) { break }; Start-Sleep -Milliseconds 100 }
  Check (HasText $pattern) $what
}
# A button on the same line as an element, right of it: the bin beside a kept preset.
function BesideOf($el, [string]$tip) {
  $r = $el.Current.BoundingRectangle; $mid = $r.Y + $r.Height / 2
  Retry { @((All (Cond $AE::HelpTextProperty $tip)) |
    Where-Object { $b = $_.Current.BoundingRectangle; [Math]::Abs($b.Y + $b.Height / 2 - $mid) -lt 24 -and $b.X -gt $r.X })[0] }
}
# The edit left of a button on its line: the preset name box, left of Keep.
function EditLeftOf($button) {
  $r = $button.Current.BoundingRectangle; $mid = $r.Y + $r.Height / 2
  Retry { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) |
    Where-Object { $b = $_.Current.BoundingRectangle; [Math]::Abs($b.Y + $b.Height / 2 - $mid) -lt 12 -and $b.X -lt $r.X })[0] }
}
function Val($e) { try { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { "?" } }
function SetText($e, [string]$text) {
  $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); Start-Sleep -Milliseconds 200
}

Case 'search-presets.section-above-the-form-on-one-edge' -Tags search, presets {
  ItemSearch

  # the presets' section first, then the name
  $presets = WaitUntil { ByName 'Presets' } "the presets' section"
  $name = WaitUntil { ItemSearchBox } "the keyword box"
  Check ($presets.Current.BoundingRectangle.Y -lt $name.Current.BoundingRectangle.Y) "the presets above the keyword"
  $keyword = ByName 'Keyword'; $sockets = ByName 'Sockets'
  Check ($null -ne $keyword -and $keyword.Current.BoundingRectangle.Bottom -le $name.Current.BoundingRectangle.Top + 2) "the keyword box under its heading"
  Check ([Math]::Abs($sockets.Current.BoundingRectangle.X - $name.Current.BoundingRectangle.X) -lt 4) "labels and boxes left, one edge"
  Expect '^No preset$' "No preset on the header"
}

# Kept, chosen and forgotten again by the buttons: the settings are written, so the program is not shared after.
Case 'search-presets.keep-row-and-bin-are-reached' -Tags search, presets -Changes {
  ItemSearch
  WaitUntil { ByName 'Presets' } "the presets' section" | Out-Null
  # a name typed, then kept as a preset - in the panel, nothing opened
  $box = WaitUntil { ItemSearchBox } "the keyword box"
  SetText $box 'Charm'
  $keep = WaitUntil { ByTip 'Keep preset' } "the Keep button"
  $nameBox = EditLeftOf $keep
  Check (-not $keep.Current.IsEnabled) "Keep off with no name"
  SetText $nameBox 'UI test charms'
  Check ((ByTip 'Keep preset').Current.IsEnabled) "Keep on with a name"
  ClickEl (ByTip 'Keep preset') "Keep preset"
  Expect '^UI test charms$' "the preset kept, in the list and on the header"

  # the search cleared; a click on the kept preset is the search again, the section still there
  SetText $box ''
  Expect '^No preset$' "cleared: no preset"
  ClickEl (WaitUntil { ByName 'UI test charms' } "the kept row") "the kept preset"
  Start-Sleep -Milliseconds 300
  Check ((Val (ItemSearchBox)) -eq 'Charm') "the preset is the search at once"
  Check ($null -ne (ByName 'Presets')) "nothing closes"

  # forgotten again
  $row = @((All (Cond $AE::NameProperty 'UI test charms')) | Where-Object { -not $_.Current.IsOffscreen } | Sort-Object { $_.Current.BoundingRectangle.Y } | Select-Object -Last 1)[0]
  # Asks first: one press leaves it, the second deletes.
  ClickEl (BesideOf $row 'Delete preset') "Delete preset (asks)"
  Start-Sleep -Milliseconds 300
  Check (HasText '^UI test charms$') "Delete preset: one press only asks"
  ClickEl (BesideOf $row 'Delete preset') "Delete preset (confirmed)"
  Start-Sleep -Milliseconds 300
  Check (-not (HasText '^UI test charms$')) "Delete preset: gone from the list"
  ItemSearchEnd
}
