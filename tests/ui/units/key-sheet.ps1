# The sheet of keys and gestures, as only a window shows it (KeySheetService): that
# F1 reaches it from a page and puts it away again, that Esc closes it, that it is drawn with its rows, and that
# the button of Settings' About card and the sheet's own OK are there to click.
#
# What the sheet holds and what opens and shuts it - not shown to begin with, the right click's menu, the
# steppers', the drags, its own key, the button's tooltip - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Keys/KeySheetTests.
#
# Nothing is changed, and no letter is typed: F1 and Esc are keys under any input language.

Add-Type -AssemblyName System.Windows.Forms
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }
function Open { [bool](ById 'KeySheet_Title') }
function Says([string]$text) { @((All (Cond $AE::NameProperty $text)) | Where-Object { -not $_.Current.IsOffscreen }).Count -gt 0 }

Case 'key-sheet.f1-opens-it-with-its-rows-and-puts-it-away' -Tags keys, settings {
  Check (-not (Open)) "the sheet is not shown to begin with"

  Keys '{F1}'
  Check ([bool](WaitUntil { if (Open) { 'open' } } "the sheet" 3000)) "F1 opens the sheet of keys and gestures"
  Check ((Says 'RMB') -and (Says 'Menu key / Shift + F10') -and (Says 'Menu of what is selected')) "it names the menu's keys: 'RMB', 'Menu key / Shift + F10'"
  Check (-not (Says 'Ctrl + Shift + RMB') -and -not (Says 'Alt + RMB')) "and no chord of the right click, which are the menu's rows now"
  Check ((Says 'Ctrl + Shift + LMB') -and (Says 'Every point: a stat, a skill, the level, points to spend')) "and the steppers': 'Ctrl + Shift + LMB', 'Every point: a stat, a skill, the level, points to spend'"
  Check ((Says 'Drag onto an item') -and (Says 'F1')) "and the drags, and its own key"

  Keys '{F1}'
  Check ([bool](WaitUntil { if (-not (Open)) { 'shut' } } "the sheet shut by its key" 3000)) "F1 again puts it away"
}

Case 'key-sheet.esc-closes-it' -Tags keys, settings {
  Keys '{F1}'
  WaitUntil { if (Open) { 'open' } } "the sheet" 3000 | Out-Null
  Keys '{ESC}'
  Check ([bool](WaitUntil { if (-not (Open)) { 'shut' } } "the sheet shut" 3000)) "Esc closes it"
}

Case 'key-sheet.settings-about-button-opens-it-and-ok-closes-it' -Tags keys, settings {
  ClickEl (ById 'SideBar_Settings') "Settings"
  $button = WaitUntil { ById 'Settings_Keys' } "the About card's button"
  Check (($button.Current.HelpText -split "`n")[0] -match 'F1') "Settings, About: 'Keys and gestures', its tooltip naming the key ('$(($button.Current.HelpText -split "`n")[0])')"
  # Brought into view first: the About card is the page's last, below the fold with the Sorting rules row
  # above it, and a click at a button the scroller hides lands on what is over it.
  try { $button.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView(); Start-Sleep -Milliseconds 300 } catch { }
  $button = ById 'Settings_Keys'
  ClickEl $button "Keys and gestures"
  Check ([bool](WaitUntil { if (Open) { 'open' } } "the sheet from Settings" 3000)) "the button opens the same sheet"
  ClickEl (ById 'KeySheet_Close') "OK"
  Check ([bool](WaitUntil { if (-not (Open)) { 'shut' } } "the sheet shut by OK" 3000)) "and OK closes it"
}

# A click beside the sheet closes it, as beside every popup.
Case 'key-sheet.click-beside-closes-it' -Tags keys, settings {
  Keys '{F1}'
  WaitUntil { if (Open) { 'open' } } "the sheet" 3000 | Out-Null
  ClickAt ([int]($script:r.Width * 0.04)) ([int]($script:r.Height * 0.9))
  Check ([bool](WaitUntil { if (-not (Open)) { 'shut' } } "the sheet shut" 3000)) "a click beside it closes it"
}
