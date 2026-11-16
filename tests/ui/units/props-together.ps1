# Rows that go off together, as only a window shows them: that the brush on a property's row
# reaches the row - both of Enhanced Damage's go - and that the top bar's Undo, once the change is written, brings
# both back onto the page.
#
# Which rows go together and which come on together - a flat damage's three copies on a helm - is proven without
# a window: tests/PD2SPCompanion.Shell.Tests/Edit/PropertyRowsTests.
#
# On the pad's PhySpearZon: the worn Veil of Steel, twelve properties. In Free, where a property can be taken off.

function Header { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { $_.Current.Name -like 'Properties*(*' } | ForEach-Object { $_.Current.Name })[0] }
function Buttons([string]$tip) { @((All (Cond $AE::ControlTypeProperty $CT::Button)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.HelpText -like $tip } | Sort-Object { $_.Current.BoundingRectangle.Y }) }

Case 'props-together.brush-and-undo-move-both-rows' -Tags edit, properties, undo -Mode Free -Changes {
  ClickUntil { ClickEl (WaitUntil { Tile 'Veil of Steel' } "Veil of Steel") "Veil of Steel" } { OnEdit 'Veil of Steel' } "Veil of Steel on the Edit page" | Out-Null
  WaitUntil { (Header) -eq 'Properties  (12)' } "its twelve properties" | Out-Null

  # Off together: Enhanced Damage is a maximum and a minimum, and the brush on one takes both.
  $brushes = WaitUntil { $b = Buttons 'Remove property*Adjacent run entries go too'; if ($b.Count -ge 2) { , $b } } "Enhanced Damage's two brushes"
  # Pressed, not clicked: on this helm the rows lie past the page's foot until it is scrolled, and a click at the
  # brush's place lands on the strip under the page. The header asked in a redraw can be nothing: that is "not yet".
  InvokeEl $brushes[0] "the brush on Enhanced Maximum Damage"
  WaitUntil { $h = Header; if ($h -and $h -ne 'Properties  (12)') { $h } } "a row gone" | Out-Null
  Check ((Header) -eq 'Properties  (10)') "the brush on one of Enhanced Damage's rows takes both off: '$(Header)'"
  UndoLast "the two rows off"
  $back = $false
  try { WaitUntil { (Header) -eq 'Properties  (12)' } "the rows back" 6000 | Out-Null; $back = $true } catch { }
  Check $back "the undo brings both back: '$(Header)'"
}
