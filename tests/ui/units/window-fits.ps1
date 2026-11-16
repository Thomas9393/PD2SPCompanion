# The window never larger than the screen: at 1920 x 1080 and 150% the
# screen's room is 1280 x 672 units, the window's least size was 1280 x 720 and its foot lay under the taskbar.
# The program is told the room (PD2SPC_SCREEN) on a screen that has more; the window opened must be within it,
# its foot - the message strip - in the window, and no smaller than asked: on a screen with room it keeps
# its least size. Under the UI scale the window on the small screen is also drawn smaller
# (Automatic), so its least size there is the least layout at that scale - within the room, where 720 was not.
#
# All of it is the window's own doing (MainWindow.HoldToScreen), so all of it is here: nothing of this script
# became a unit test. Automatic's arithmetic is the Core's (ScreenFitTests), and which scale a screen is given
# is in tests/PD2SPCompanion.Shell.Tests/Window/UiScaleTests.
#
# Nothing is loaded or changed. Each case tells the program its room through the environment and shows the
# window as it stands, not maximized; both are taken back at the case's end.

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class FastDpi {
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int t, bool repaint);
}
"@ -ErrorAction SilentlyContinue

# The window as it opens, not maximized: its size in the window's own units.
function Units {
  $h = $script:proc.MainWindowHandle
  $scale = [FastDpi]::GetDpiForWindow($h) / 96.0
  $b = $script:win.Current.BoundingRectangle
  @{ Wide = $b.Width / $scale; Tall = $b.Height / $scale; Scale = $scale }
}
# The window settled as the program opened it in the room it was told.
function Opened {
  Start-Sleep -Milliseconds 600
  $script:r = $script:win.Current.BoundingRectangle
  Units
}
# The setting as a new install has it, whatever the settings handed in say: Automatic.
function AutomaticScale {
  $text = [IO.File]::ReadAllText((SettingsFile)) -replace '"UiScale":\s*\d+', '"UiScale": 0'
  [IO.File]::WriteAllText((SettingsFile), $text, (New-Object System.Text.UTF8Encoding($false)))
}
# The program's log says the scale it drew at only as that changes from 1, and fast.ps1's Unit reads the last such
# line: left in the pad's log, this start's would be told to every later case that asks. The log goes with it.
function ForgetScaleSaid { Remove-Item "$pad\editor.log" -ErrorAction SilentlyContinue }

Case 'window-fits.small-screen-holds-the-whole-window' -Tags window, scale -Start Home -Before {
  AutomaticScale
  $env:PD2SPC_SCREEN = '1280x672'
  $script:show = 5
} {
  try {
    # A screen with less room than the least size: the window is as large as the room and no larger.
    $small = Opened
    "  told 1280 x 672: the window is $([int]$small.Wide) x $([int]$small.Tall) units (scale $($small.Scale))"
    Check ($small.Wide -le 1281 -and $small.Tall -le 673) "the window is within the room"
    Check ($small.Tall -ge 640) "and as tall as the room lets it be"

    # Asked to be larger than the room it still goes there (a drag of its edge): only its least size is held.
    # Asked to be smaller, it stops at the least layout as that screen draws it - 720 at three quarters and a
    # little, with the caption - which is inside the room, where 720 itself was not.
    $h = $script:proc.MainWindowHandle
    [FastDpi]::MoveWindow($h, 40, 40, 800, 500, $true) | Out-Null; Start-Sleep -Milliseconds 500
    $least = Units
    "  asked for less: $([int]$least.Wide) x $([int]$least.Tall) units"
    Check ($least.Tall -le 673 -and $least.Tall -ge 540 -and $least.Wide -le 1281 -and $least.Wide -ge 960) "the least size is the least layout drawn at that screen's scale, inside the room ($([int]$least.Wide) x $([int]$least.Tall))"

    # The foot is in the window: the sidebar's last button, which stands at the bottom of it.
    [FastDpi]::MoveWindow($h, 40, 40, 100, 100, $true) | Out-Null; Start-Sleep -Milliseconds 500
    $script:r = $script:win.Current.BoundingRectangle
    $foot = (ById 'SideBar_Settings').Current.BoundingRectangle
    Check ($foot.Y + $foot.Height -le $script:r.Y + $script:r.Height + 1) "the sidebar's foot is inside the window"
  }
  finally {
    $env:PD2SPC_SCREEN = $null
    $script:show = 3
    ForgetScaleSaid
  }
}

Case 'window-fits.roomy-screen-keeps-the-least-size' -Tags window, scale -Start Home -Before {
  AutomaticScale
  $env:PD2SPC_SCREEN = '2048x1104'
  $script:show = 5
} {
  try {
    # A screen with room: the least size is the layout's own.
    $roomy = Opened
    "  told 2048 x 1104: the window is $([int]$roomy.Wide) x $([int]$roomy.Tall) units"
    Check ($roomy.Wide -ge 1279 -and $roomy.Tall -ge 719) "with room, the window opens at its least size or more"
    $h = $script:proc.MainWindowHandle
    [FastDpi]::MoveWindow($h, 40, 40, 800, 500, $true) | Out-Null; Start-Sleep -Milliseconds 500
    $least = Units
    Check ($least.Wide -ge 1279 -and $least.Tall -ge 719) "and goes no smaller than 1280 x 720 ($([int]$least.Wide) x $([int]$least.Tall))"
  }
  finally {
    $env:PD2SPC_SCREEN = $null
    $script:show = 3
  }
}
