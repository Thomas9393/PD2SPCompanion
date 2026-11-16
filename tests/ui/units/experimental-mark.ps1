# The Experimental page's online notice and the side bar's red dot, as only a window shows them:
# while the page's files are in the game folder and the notice was never dismissed, the button
# wears a red dot on its corner and the page shows the notice under its buttons; its cross takes a click and
# both go.
#
# What decides it - installed and never dismissed, the tooltip's two lines, the dismissal for good and written
# to the settings - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Window/ExperimentalMarkTests.
#
# Never writes the game folder: each case follows whether the install marker is there, whichever it is - with
# nothing installed the first shows that nothing is marked, and the second has nothing to dismiss. Each starts
# the program on settings that say "not yet dismissed", the run's copy, put back by the runner after it.

$script:Advice = "Mod files installed. Remove them before playing online."
# Whether the page's files are in the game folder - the one the run's settings name, which is where the program
# looks; the development default where they name none.
function Installed {
  $root = (Get-Content (SettingsFile) -Raw | ConvertFrom-Json).GameFolder
  if (-not $root) { $root = $script:GameRoot }
  Test-Path "$root\ProjectD2\data\pd2spc-density.json"
}

# Whether the red dot is drawn: the pixel at its centre, 3 in from the button's top right corner. The
# selected button's own fill is a darker red, so the bounds are the dot's bright orange-red.
# Read off the screen - the button's tooltip text, as UI Automation reports it, is not told again.
function DotShown {
  $b = (ById 'SideBar_Experimental').Current.BoundingRectangle
  $bmp = New-Object System.Drawing.Bitmap 1, 1
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen([int]($b.Right - 3), [int]($b.Top + 3), 0, 0, $bmp.Size)
  $c = $bmp.GetPixel(0, 0); $g.Dispose(); $bmp.Dispose()
  $c.R -gt 200 -and $c.G -gt 40 -and $c.G -lt 110 -and $c.B -lt 70
}
# Not yet dismissed, for this start.
function NotYetDismissed {
  $text = [IO.File]::ReadAllText((SettingsFile)) -replace '"OnlineWarningDismissed":\s*true', '"OnlineWarningDismissed": false'
  [IO.File]::WriteAllText((SettingsFile), $text, (New-Object System.Text.UTF8Encoding($false)))
}

Case 'experimental-mark.dot-and-notice-follow-what-is-installed' -Tags window, experimental -Start Home -Before { NotYetDismissed } {
  $button = WaitUntil { ById 'SideBar_Experimental' } "the Experimental button"
  if (-not (Installed)) {
    # Its name and its key (the side bar's tooltips name their keys), and no advice under them.
    Check ($button.Current.HelpText -eq 'Experimental (Ctrl + 5)') "not installed: the button says only Experimental and its key ('$($button.Current.HelpText)')"
    Check (-not (DotShown)) "not installed: no red dot"
    ClickEl $button "Experimental"
    Start-Sleep -Milliseconds 800
    Check (-not (ByName $script:Advice)) "not installed: no notice on the page"
    return
  }

  Check ($button.Current.HelpText -match "Remove them before playing online") "installed: the button's tooltip gives the advice"
  ClickEl $button "Experimental"
  Check ([bool](WaitUntil { ByName $script:Advice } "the notice under the buttons")) "installed: the notice under the page's buttons"
  Start-Sleep -Milliseconds 600
  Check (DotShown) "installed: the red dot on the button's corner"
}

Case 'experimental-mark.cross-takes-the-notice-and-the-dot' -Tags window, experimental -Start Home -Before { NotYetDismissed } {
  if (-not (Installed)) {
    "  nothing of the page's is installed in the game folder: no notice to dismiss"
    return
  }

  ClickEl (WaitUntil { ById 'SideBar_Experimental' } "the Experimental button") "Experimental"
  WaitUntil { ByName $script:Advice } "the notice under the buttons" | Out-Null
  Start-Sleep -Milliseconds 600

  ClickEl (WaitUntil { ById 'OnlineNotice_Dismiss' } "the notice's cross") "dismiss"
  Check ([bool](WaitUntil { if (-not (ByName $script:Advice)) { 'gone' } } "the notice gone")) "dismissed: the notice is gone"
  Check ([bool](WaitUntil { if (-not (DotShown)) { 'gone' } } "the red dot gone" 3000)) "dismissed: the red dot is gone"
  Check ((Get-Content (SettingsFile) -Raw) -match '"OnlineWarningDismissed":\s*true') "and the click reached the settings"
}
