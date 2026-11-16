# One roll, two boxes, as only a window shows it: a number typed on the keyboard into one of Enhanced Damage's two
# boxes stays in that box alone (individual editing on all occasions), and the
# row's link button - "Same number on its group" - puts it in the other box too.
#
# That each row is its own however many sources feed it, and that the link reaches the rows written with it, is
# proven without a window: tests/PD2SPCompanion.Shell.Tests/Edit/PropertyRowsTests.
#
# On the pad's PhySpearZon: Demon Limb in the personal stash, Enhanced Damage 199 and 199, of 180-230.

Add-Type -AssemblyName System.Windows.Forms

function Boxes { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen }) }
# What a box holds; nothing for an edit control that gives no value (a picker's field).
function Held($box) { $p = $null; if ($box.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$p)) { $p.Current.Value } }
function Holding([string]$number) { @(Boxes | Where-Object { (Held $_) -eq $number }) }
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 250 }

# What is typed is the item's as it is typed: the pad is put back after.
Case 'rolls-together.typed-into-one-box-stays-and-the-link-puts-it-in-both' -Tags edit, properties -Typing -Changes {
  Pane 0 'Personal Stash'
  ClickUntil { ClickEl (WaitUntil { Tile 'Demon Limb' } "Demon Limb") "Demon Limb" } { OnEdit 'Demon Limb' } "Demon Limb on the Edit page" | Out-Null
  $pair = WaitUntil { $b = Holding '199'; if ($b.Count -eq 2) { , $b } } "the two Enhanced Damage boxes (199)"
  ClickEl $pair[0] "the first box"
  Keys '^a{DEL}'
  Keys '205'
  Check (((Held $pair[0]) -eq '205') -and ((Held $pair[1]) -eq '199')) "205 typed into one stays in it: '$(Held $pair[0])' and '$(Held $pair[1])'"
  # The first row's link: the topmost of the two.
  $links = @((All (Cond $AE::ControlTypeProperty $CT::Button)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.HelpText -like 'Same number on its group*' } | Sort-Object { $_.Current.BoundingRectangle.Y })
  Check ($links.Count -ge 2) "both rows show their link: $($links.Count)"
  ClickEl $links[0] "the first row's link"
  Start-Sleep -Milliseconds 300
  Check (((Held $pair[0]) -eq '205') -and ((Held $pair[1]) -eq '205')) "the link puts 205 in both: '$(Held $pair[0])' and '$(Held $pair[1])'"
}
