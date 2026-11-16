# The Character page's steppers in each play mode, as only a window shows them: that Ctrl
# and Shift held on a stepper reach it as 5 and 20 - and both together as every point, on a stat and a skill
# (PD2's own key); that the points-to-spend box takes what is typed on Enter or on leaving it; which
# steppers are drawn in Free, Casual and Legit; that the level's progress is a slider in Free, stepped by 1 with
# the arrow keys and stopping at 99, and a gauge elsewhere; and what the steppers' tooltips say.
#
# What the pages then decide - the points moved, the level kept, what a mode refuses - is proven without a
# window: tests/PD2SPCompanion.Shell.Tests/Character/PointsTests.
#
# On the card pad, with a fresh level 30 "Stepper" made before the program starts, once for each case.

Add-Type -AssemblyName System.Windows.Forms
function Card($name) { foreach ($c in (All (Cond $AE::ControlTypeProperty $CT::ListItem))) { if (-not $c.Current.IsOffscreen -and $c.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $c } } }

# UI Automation now and then fails a search while the page redraws: asked again, it answers.
function Retry([scriptblock]$find) { for ($n = 0; $n -lt 10; $n++) { try { return (& $find) } catch { Start-Sleep -Milliseconds 150 } } }
function RightOf([object[]]$all) { , @($all | Where-Object { $_.Current.BoundingRectangle.X -gt (BookMiddle) -and -not $_.Current.IsOffscreen } |
  Sort-Object { $_.Current.BoundingRectangle.Y }, { $_.Current.BoundingRectangle.X }) }
# The right page's boxes: the attributes' points to spend above the skills'.
function Boxes { Retry { RightOf @((All (Cond $AE::ControlTypeProperty $CT::Edit))) } }
function Buttons([string]$tip) { Retry { RightOf @((All (Cond $AE::ControlTypeProperty $CT::Button)) |
  Where-Object { $_.Current.HelpText -like "$tip*" }) } }
# An attribute's stepper, by its id: its tooltip is worded as a skill's (Add point, Remove point).
function Attr([string]$id) { Retry { RightOf @((All (Cond $AE::AutomationIdProperty $id))) } }
function Val($e) { try { [int]$e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { -1 } }
function Pool([int]$i) { $b = Boxes; if ($b.Count -gt $i) { Val $b[$i] } else { -1 } }

# A click as a hand makes it: on the button, a moment's hover, then the modifier and the press. The rect, not
# the element: a skill's tile is drawn again after each change.
function Press($rect, [string]$mod = '') {
  $x = [int]($rect.X + $rect.Width / 2); $y = [int]($rect.Y + $rect.Height / 2)
  Front; [Fast]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 250
  # 'CtrlShift' holds both: every point, on a stat and a skill (PD2's own key).
  $keys = @(@{ Shift = @(0x10); Ctrl = @(0x11); CtrlShift = @(0x11, 0x10) }[$mod])
  try {
    foreach ($key in $keys) { if ($key) { [Fast]::keybd_event($key, 0, 0, 0); Start-Sleep -Milliseconds 40 } }
    [Fast]::Click($x, $y)
  }
  finally { foreach ($key in $keys) { if ($key) { Start-Sleep -Milliseconds 40; [Fast]::keybd_event($key, 0, 2, 0) } } }
}
function Expect([int]$i, [int]$want, [string]$what) {
  $got = -1; $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt 3000) { $got = Pool $i; if ($got -eq $want) { break }; Start-Sleep -Milliseconds 100 }
  Check ($got -eq $want) "$what ($got, want $want)"
}
function TypeIn([int]$i, [string]$keys) { Press (Boxes)[$i].Current.BoundingRectangle; [System.Windows.Forms.SendKeys]::SendWait($keys) }

# The level's progress slider, and the experience box (Free's, left of the page's middle).
function Progress { Retry { $script:win.FindFirst($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Slider)) } }
function Percent { try { [int](Progress).GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value } catch { -1 } }
function Experience { $e = Retry { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) |
  Where-Object { $_.Current.BoundingRectangle.X -lt (BookMiddle) } | Sort-Object { $_.Current.BoundingRectangle.X })[-1] }
  try { [long]($e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -replace ',', '') } catch { -1 } }

# The first skill a point can go into, top of the tree: its own add and remove buttons.
function SkillIn { (Buttons 'Add point' | Where-Object { $_.Current.HelpText -notlike 'Add points*' -and $_.Current.AutomationId -ne 'Attr_Add' })[0].Current.BoundingRectangle }
function SkillOut { (Buttons 'Remove point' | Where-Object { $_.Current.HelpText -notlike 'Remove points*' -and $_.Current.AutomationId -ne 'Attr_Remove' })[0].Current.BoundingRectangle }

$makeStepper = {
  Remove-Item "$pad\saves\Stepper.*" -ErrorAction SilentlyContinue
  $corpus = $script:Corpus
  & $script:Cli create --class Amazon --template "$corpus\saves\PhySpearZon.d2s" --out "$pad\saves\Stepper.d2s" --level 30 | Out-Null
}
# Stepper chosen in the list and loaded, the Character page in front: its points to spend, the attributes' then
# the skills', read once both boxes answer - and kept from that same reading: read again after the wait, one box
# came back unread (-1) as the page drew once more.
function OpenStepper {
  ClickUntil { if ($c = Card 'Stepper') { ClickEl $c "Stepper's card" } } { ByName 'Open' } "Stepper chosen" 800 | Out-Null
  ClickEl (ByName 'Open') "Load"
  WaitUntil { (ById 'SideBar_Char').Current.IsEnabled } "Stepper loaded" | Out-Null
  ClickEl (ById 'SideBar_Char') "Character"
  WaitUntil { (Boxes).Count -ge 2 } "the points boxes" | Out-Null
  $both = WaitUntil { $a = Pool 0; $b = Pool 1; if ($a -ge 0 -and $b -ge 0) { , @($a, $b) } } "both boxes read"
  Step "start: attributes $($both[0]), skills $($both[1])" | Out-Host
  , $both
}

Case 'points-steppers.free-held-keys-reach-every-stepper' -Tags character, points -Pad card -Start Home -Mode Free -Before $makeStepper {
  $both = OpenStepper; $stats = $both[0]; $skills = $both[1]
  Check ($stats -gt 20 -and $skills -gt 20) "a level 30 character has points to spend"

  $add = Buttons 'Add points'; $remove = Buttons 'Remove points'
  Check ($add.Count -eq 2 -and $remove.Count -eq 2) "both points to spend have steppers"
  $addSkills = $add[1].Current.BoundingRectangle; $removeSkills = $remove[1].Current.BoundingRectangle
  Press $addSkills 'Ctrl';  Expect 1 ($skills + 5) "skills' points to spend: Ctrl + adds 5"
  Press $addSkills 'Shift'; Expect 1 ($skills + 25) "Shift + adds 20"
  Press $removeSkills 'Ctrl'; Expect 1 ($skills + 20) "Ctrl - takes 5"
  Press $removeSkills 'Shift'; Expect 1 $skills "Shift - takes 20"
  Press $add[0].Current.BoundingRectangle 'Ctrl'; Expect 0 ($stats + 5) "attributes' points to spend: Ctrl + adds 5"
  Press $remove[0].Current.BoundingRectangle 'Ctrl'; Expect 0 $stats "Ctrl - takes 5"

  # A skill's own steppers take from the points to spend and give back to them.
  $in = SkillIn; $out = SkillOut
  Press $in 'Ctrl';   Expect 1 ($skills - 5) "a skill's Ctrl + puts in 5"
  Press $in 'Shift';  Expect 1 ($skills - 20) "its Shift + fills it to 20"
  Press $out 'Ctrl';  Expect 1 ($skills - 15) "its Ctrl - takes 5"
  Press $out 'Shift'; Expect 1 $skills "its Shift - takes the rest"
  $invest = (Attr 'Attr_Add')[0].Current.BoundingRectangle
  Press $invest 'Ctrl'; Expect 0 ($stats - 5) "an attribute's Ctrl + invests 5"
  Press (Attr 'Attr_Remove')[0].Current.BoundingRectangle 'Ctrl'; Expect 0 $stats "its Ctrl - refunds 5"
  # Both keys: every point there is, in and out again (PD2's Ctrl + Shift).
  Press (Attr 'Attr_Add')[0].Current.BoundingRectangle 'CtrlShift'; Expect 0 0 "an attribute's Ctrl + Shift invests every point left"
  Press (Attr 'Attr_Remove')[0].Current.BoundingRectangle 'CtrlShift'; Expect 0 $stats "its Ctrl + Shift refunds every one"
  Check ((Attr 'Attr_Add')[0].Current.HelpText -match 'Ctrl \+ Shift \+ LMB: all') "and the tooltip says so: 'Ctrl + Shift + LMB: all'"
}

Case 'points-steppers.free-box-takes-what-is-typed' -Tags character, points -Pad card -Start Home -Mode Free -Typing -Before $makeStepper {
  $both = OpenStepper; $skills = $both[1]
  TypeIn 1 "^a7{ENTER}"; Expect 1 7 "typed 7 and Enter: skills' points to spend 7"
  TypeIn 1 "^a{BACKSPACE}{ENTER}"; Expect 1 7 "emptied and Enter: back to 7"
  TypeIn 1 "^a$skills"; Press (Boxes)[0].Current.BoundingRectangle; Expect 1 $skills "typed and left: applied"
}

Case 'points-steppers.free-level-is-a-slider-stepped-by-1' -Tags character, points, level -Pad card -Start Home -Mode Free -Before $makeStepper {
  OpenStepper | Out-Null
  $slider = Progress
  Check ($null -ne $slider) "the level has a progress bar"
  # The level's own steppers say what Ctrl and Shift do on them.
  $raise = ByTip 'Raise level' -Prefix
  Check ($null -ne $raise -and $null -ne (ByTip 'Lower level' -Prefix) -and $raise.Current.HelpText -match 'Ctrl \+ LMB: 5 levels') "the level's steppers have their tooltips: '$(($raise.Current.HelpText -split "`n") -join ' / ')'"
  # A dimmed stepper says why under its name: nothing is spent on a new character, so no point comes back.
  $minus = @(Attr 'Attr_Remove')[0]
  Check ($null -ne $minus -and -not $minus.Current.IsEnabled -and $minus.Current.HelpText -like 'Remove point*at class minimum*') "an attribute's minus, dimmed, says why: '$(($minus.Current.HelpText -split "`n") -join ' / ')'"
  Check $slider.Current.IsEnabled "Free: it is a slider"
  $start = Experience
  Check ((Percent) -eq 0) "a new level 30 is 0% into it ($(Percent))"
  $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(50)
  WaitUntil { (Experience) -gt $start } "experience moved" 3000 | Out-Null
  $half = Experience
  Check ((Percent) -eq 50) "set to 50: reads 50 ($(Percent))"
  Check ($null -ne (ByName 'Level 30 Amazon')) "the level kept"
  Press $slider.Current.BoundingRectangle; [System.Windows.Forms.SendKeys]::SendWait("{RIGHT}")
  $sw = [Diagnostics.Stopwatch]::StartNew(); while ((Percent) -ne 51 -and $sw.ElapsedMilliseconds -lt 3000) { Start-Sleep -Milliseconds 100 }
  Step "clicked at the middle, then Right: $(Percent)%"
  [System.Windows.Forms.SendKeys]::SendWait("{END}")
  $sw = [Diagnostics.Stopwatch]::StartNew(); while ((Percent) -ne 99 -and $sw.ElapsedMilliseconds -lt 3000) { Start-Sleep -Milliseconds 100 }
  Check ((Percent) -eq 99) "End: 99, as far as a level goes ($(Percent))"
  Check ($null -ne (ByName 'Level 30 Amazon')) "the level still kept at 99%"
  [System.Windows.Forms.SendKeys]::SendWait("{LEFT}")
  $sw = [Diagnostics.Stopwatch]::StartNew(); while ((Percent) -ne 98 -and $sw.ElapsedMilliseconds -lt 3000) { Start-Sleep -Milliseconds 100 }
  Check ((Percent) -eq 98) "Left: a step of 1 ($(Percent))"
  Step "experience: start $start, at 50% $half, now $(Experience)"
}

Case 'points-steppers.casual-moves-points-but-adds-none' -Tags character, points -Pad card -Start Home -Mode Casual -Typing -Before $makeStepper {
  $both = OpenStepper; $stats = $both[0]; $skills = $both[1]
  $add = Buttons 'Add points'; $remove = Buttons 'Remove points'
  Check ($add.Count -eq 0 -and $remove.Count -eq 0) "no steppers beside the points to spend"
  TypeIn 1 "^a99{ENTER}"; Expect 1 $skills "typed 99 and Enter: unchanged, the box read only"

  $slider = Progress
  Check ($null -ne $slider) "the level has a progress bar"
  Check (-not $slider.Current.IsEnabled) "a gauge, not a slider"

  # A skill's own steppers and an attribute's are there, as in Free.
  $in = SkillIn; $out = SkillOut
  Press $in 'Ctrl';   Expect 1 ($skills - 5) "a skill's Ctrl + puts in 5"
  Press $out 'Ctrl';  Expect 1 $skills "its Ctrl - takes 5"
  $invest = (Attr 'Attr_Add')[0].Current.BoundingRectangle
  Press $invest 'Ctrl'; Expect 0 ($stats - 5) "an attribute's Ctrl + invests 5"
  Press (Attr 'Attr_Remove')[0].Current.BoundingRectangle 'Ctrl'; Expect 0 $stats "its Ctrl - refunds 5"
}

Case 'points-steppers.legit-draws-no-steppers' -Tags character, points -Pad card -Start Home -Mode Legit -Typing -Before $makeStepper {
  $both = OpenStepper; $skills = $both[1]
  $add = Buttons 'Add points'; $remove = Buttons 'Remove points'
  Check ($add.Count -eq 0 -and $remove.Count -eq 0) "no steppers beside the points to spend"
  TypeIn 1 "^a99{ENTER}"; Expect 1 $skills "typed 99 and Enter: unchanged, the box read only"

  $slider = Progress
  Check ($null -ne $slider) "the level has a progress bar"
  Check (-not $slider.Current.IsEnabled) "a gauge, not a slider"

  Check ((Buttons 'Add point').Count -eq 0 -and (Buttons 'Remove point').Count -eq 0) "no skill steppers"
  Check ((Attr 'Attr_Add').Count -eq 0) "no attribute steppers"

  # The waypoints and the map seed are read in Legit (their cards were not there until
  # then): shown, with no Grant, no Remove and no dice.
  Check ($null -ne (ByName 'Waypoints') -and $null -ne (ByName 'Map seed')) "the Waypoints and Map seed cards are shown"
  Check ($null -eq (ByName 'GRANT') -and $null -eq (ByName 'REMOVE')) "Grant and Remove are not there"
  Check ($null -eq (ByTip 'Random seed')) "nor the seed's dice"
}
