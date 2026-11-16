# The UI scale, as only a window shows it: the whole
# window drawn at one scale over Windows' own, so a screen with less room is laid out as the reference monitor is.
#   - told a scale (PD2SPC_UI_SCALE, 80): an item's tile is that much of its size, and a drop-down - a window
#     of its own - opens at the same scale, as wide as the box it drops from;
#   - told the room of a 1080p screen at 150% (PD2SPC_SCREEN): Automatic scales the window down, and its foot
#     is inside it;
#   - Settings, Display, "UI scale": a step chosen redraws the window at once and reaches the settings.
#
# What the settings decide - the steps offered, a stored number put on the nearest, what is written, which
# scale a screen is given - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Window/UiScaleTests
# (and the Core's ScreenFitTests for Automatic's arithmetic).
#
# Nothing of the character's is changed. The two cases told something through the environment have a start of
# the program to themselves and take it back at their end.

# Windows' own scaling of the window (fast.ps1's UnitDpi).
function Windows { [UnitDpi]::GetDpiForWindow([IntPtr]$script:win.Current.NativeWindowHandle) / 96.0 }
function Near([double]$a, [double]$b, [double]$by = 2) { [Math]::Abs($a - $b) -le $by }
function Scaled { $line = Get-Content "$pad\editor.log" | Select-String 'UI scale ([\d.]+)' | Select-Object -Last 1; if ($line) { [double]$line.Matches[0].Groups[1].Value } else { 1.0 } }
# Whether the box's list is down: asked of the box itself, since an entry's name is the box's own text too.
function Dropped {
  try { "$((ById 'Settings_UiScale').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Current.ExpandCollapseState)" -eq 'Expanded' } catch { $false }
}
function Choose([string]$entry) {
  # Clicked until its list is down: a click as the Settings page was still coming up opened nothing, and the case
  # then waited for an entry that was never listed (in a full run: it passed only alone).
  ClickUntil { ClickEl (WaitUntil { ById 'Settings_UiScale' } "the UI scale box") "UI scale" } { if (Dropped) { 'down' } } "the UI scale list" 1200 | Out-Null
  # The entry in the list that dropped: the lowest of that name, the box's own text being above it.
  ClickEl (WaitUntil { @((All (Cond $AE::NameProperty $entry)) | Where-Object { -not $_.Current.IsOffscreen } | Sort-Object { $_.Current.BoundingRectangle.Y })[-1] } "the entry '$entry'") $entry
  Start-Sleep -Milliseconds 700
}
# The setting as a new install has it, whatever the settings handed in say: Automatic.
function AutomaticScale {
  $text = [IO.File]::ReadAllText((SettingsFile)) -replace '"UiScale":\s*\d+', '"UiScale": 0'
  [IO.File]::WriteAllText((SettingsFile), $text, (New-Object System.Text.UTF8Encoding($false)))
}
# The program's log says the scale it drew at only as that changes from 1, and fast.ps1's Unit reads the last such
# line: left in the pad's log, this start's would be told to every later case that asks. The log goes with it.
function ForgetScaleSaid { Remove-Item "$pad\editor.log" -ErrorAction SilentlyContinue }

Case 'ui-scale.page-and-menu-drawn-at-a-told-scale' -Tags window, scale -Before {
  $env:PD2SPC_UI_SCALE = '80'
} {
  try {
    $windows = Windows
    $tile = (WaitUntil { Tile 'Veil of Steel' } "the helm, two cells by two").Current.BoundingRectangle
    # A tile stands a unit inside its cells on each side.
    $want = (2 * 28 - 2) * $windows * 0.8
    Check ((Near $tile.Width $want) -and (Near $tile.Height $want)) "at 80% a two-cell tile is $([Math]::Round($tile.Width, 1)) px wide (54 units x Windows' $windows x 0.8 = $want)"

    # A popup opens at the page's scale: the sort menu's rows, 26 units tall at 80%.
    Pane 0 'Personal Stash'
    ClickEl (WaitUntil { ByTip 'Sort' -Prefix } "the pane's Sort menu") "the Sort menu"
    $row = WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::Button)) | Where-Object { -not $_.Current.IsOffscreen -and $_.FindFirst($TS::Descendants, (Cond $AE::NameProperty 'Rarity')) })[0] } "the menu's Rarity row"
    $rowAt = $row.Current.BoundingRectangle
    $tall = 26 * $windows * 0.8
    Check ($rowAt.Height -ge $tall - 6 -and $rowAt.Height -le $tall + 8) "a menu opens at the page's scale: its rows $([Math]::Round($rowAt.Height, 1)) px tall (about 26 units x Windows' $windows x 0.8 = $tall)"
    PressEsc
    Pane 0 'Inventory'
  }
  finally {
    $env:PD2SPC_UI_SCALE = $null
    ForgetScaleSaid
  }
}

Case 'ui-scale.automatic-draws-a-small-screen-smaller' -Tags window, scale -Start Home -Before {
  AutomaticScale
  # The scale is read from the program's log, and a start that draws at 1 writes no line: no older start's.
  ForgetScaleSaid
  $env:PD2SPC_SCREEN = '1280x672'
  # Shown as it stands, not maximized: its size is what is looked at.
  $script:show = 5
} {
  try {
    Start-Sleep -Milliseconds 700
    $auto = Scaled
    Check ($auto -lt 0.8 -and $auto -ge 0.75) "told a room of 1280 x 672, Automatic draws the window at $auto - smaller, and no less than three quarters"
    $script:r = $script:win.Current.BoundingRectangle
    $foot = (ById 'SideBar_Settings').Current.BoundingRectangle
    Check ($foot.Y + $foot.Height -le $script:r.Y + $script:r.Height + 1) "and the window's foot is inside it"
  }
  finally {
    $env:PD2SPC_SCREEN = $null
    $script:show = 3
    ForgetScaleSaid
  }
}

Case 'ui-scale.step-chosen-in-settings-redraws-at-once' -Tags window, scale, settings -Changes {
  try {
    ClickEl (WaitUntil { ById 'SideBar_Settings' } "Settings") "Settings"
    Choose '100%'
    $full = (ById 'Settings_UiScale').Current.BoundingRectangle.Width
    Choose '80%'
    $small = (ById 'Settings_UiScale').Current.BoundingRectangle.Width
    Check (Near $small ($full * 0.8) 2) "80% chosen in Settings redraws the window at once: the box is $([Math]::Round($small, 1)) px wide, $([Math]::Round($full, 1)) at 100%"
    Check ([IO.File]::ReadAllText((SettingsFile)) -match '"UiScale":\s*80\b') "and the choice reaches the settings"
    Choose 'Automatic'
    Check ([IO.File]::ReadAllText((SettingsFile)) -match '"UiScale":\s*0\b') "Automatic chosen again: kept as that"
  }
  finally { ForgetScaleSaid }
}
