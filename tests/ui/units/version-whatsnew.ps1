# The version and "What's new", as only a window shows them: that a real start at a newer
# version puts the release notes over the window, that OK closes them, that the About card draws the build and
# its two buttons reach what they should - the second of them the window's own clipboard.
#
# What is decided - when the notes open and with which releases, that they open once, what a first run and the
# same version say, the bug-report line's words - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Window/WhatsNewTests.
#
# Nothing of the character's is changed. The Copy case empties and then writes the clipboard, as the button does.

Add-Type -AssemblyName System.Windows.Forms

# The middle dot the About lines are joined with, not written here as itself: Windows PowerShell reads a file
# without a byte order mark as ANSI.
$script:Dot = [string][char]0x00B7

# Asked through Invoke-Cond and a few times over: a search that meets the popup as it goes can throw
# UI Automation's passing "Could not open the process token" - seen twice in a row, once the
# other pages were laid out at start-up and the timing moved.
function Says([string]$part) {
  for ($n = 0; $n -lt 20; $n++) {
    $found = Invoke-Cond {
      foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
        if ($e.Current.Name.Contains($part) -and -not $e.Current.IsOffscreen) { return $e }
      }
    }
    if ($found) { return $found }
    Start-Sleep -Milliseconds 100
  }
}
# The same, for a text that fits a pattern: a version is whichever this build is.
function SaysLike([string]$pattern) {
  for ($n = 0; $n -lt 20; $n++) {
    $found = Invoke-Cond {
      foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
        if ($e.Current.Name -match $pattern -and -not $e.Current.IsOffscreen) { return $e }
      }
    }
    if ($found) { return $found }
    Start-Sleep -Milliseconds 100
  }
}
function Stored([string]$key) { (Get-Content (SettingsFile) -Raw | ConvertFrom-Json).$key }

Case 'version-whatsnew.notes-open-by-themselves-after-an-update' -Tags window, whatsnew -Start Home -Before {
  # As if the last run had been an older version. Written into the text as it stands, not through
  # ConvertTo-Json as the old script did: Windows PowerShell writes "PlayMode":  "Free" with two spaces, which
  # the runner's UsePlayMode then no longer finds - and the case would run in whatever mode the settings held.
  $text = [IO.File]::ReadAllText((SettingsFile))
  if ($text -match '"LastRunVersion":\s*"[^"]*"') { $text = $text -replace '"LastRunVersion":\s*"[^"]*"', '"LastRunVersion": "0.0.9"' }
  else { $text = ([regex]'\{').Replace($text, '{ "LastRunVersion": "0.0.9",', 1) }
  [IO.File]::WriteAllText((SettingsFile), $text, (New-Object System.Text.UTF8Encoding($false)))
} {
  # The popup by itself, over the landing page: what changed since 0.0.9.
  Check ([bool](WaitUntil { ByName "What's new in $(Stored 'LastRunVersion')" } "the What's new popup" 10000)) "the release notes open by themselves"
  Check ([bool](WaitUntil { ByName '1.0.0' } "the 1.0.0 release" 3000)) "with the 1.0.0 release"
  Check ((Stored 'LastRunVersion') -match '^\d+\.\d+\.\d+$') "shown once: last run is now $(Stored 'LastRunVersion')"
  ClickEl (ByName 'OK') "OK"
  Check ([bool](WaitUntil { if (-not (ByName '1.0.0')) { 'closed' } } "the popup closed" 3000)) "OK closes them"
}

Case 'version-whatsnew.about-button-opens-the-notes' -Tags window, whatsnew, settings {
  ClickEl (ById 'SideBar_Settings') "Settings"
  ClickUntil { if ($b = ByTip "What's new") { ClickEl $b "What's new" } } { ByName '1.0.0' } "the popup from About" 1000 | Out-Null
  Check ([bool](ByName "What's new")) "About's button opens the release notes"
  ClickEl (ByName 'OK') "OK"
  Check ([bool](WaitUntil { if (-not (ByName '1.0.0')) { 'closed' } } "the popup closed again" 3000)) "and OK closes them"
}

# Every overlay closes the same ways: by Esc, and by a click beside it - on its veil.
Case 'version-whatsnew.esc-and-a-click-beside-close-the-notes' -Tags window, whatsnew, settings, keys {
  ClickEl (ById 'SideBar_Settings') "Settings"
  ClickUntil { if ($b = ByTip "What's new") { ClickEl $b "What's new" } } { ByName '1.0.0' } "the popup from About" 1000 | Out-Null
  Front; [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  Check ([bool](WaitUntil { if (-not (ByName '1.0.0')) { 'closed' } } "the popup closed by Esc" 3000)) "Esc closes the release notes"
  Check ([bool](ByTip "What's new")) "and nothing else: the Settings page is still the page"

  ClickUntil { if ($b = ByTip "What's new") { ClickEl $b "What's new" } } { ByName '1.0.0' } "the popup again" 1000 | Out-Null
  # Beside it: the popup stands in the window's middle, and this is low in its left tenth.
  ClickAt ([int]($script:r.Width * 0.08)) ([int]($script:r.Height * 0.85))
  Check ([bool](WaitUntil { if (-not (ByName '1.0.0')) { 'closed' } } "the popup closed by a click beside it" 3000)) "a click beside them closes them too"
}

Case 'version-whatsnew.about-card-says-the-build' -Tags window, whatsnew, settings {
  ClickEl (ById 'SideBar_Settings') "Settings"
  WaitUntil { ByTip "What's new" } "the About card" | Out-Null
  $version = SaysLike '^Project Diablo II Single Player Companion \d+\.\d+\.\d+'
  Check ([bool]$version) "About shows the version: $(if ($version) { $version.Current.Name })"
  Check ([bool](Says 'Built ')) "and the build"
  $season = SaysLike "^Project Diablo 2 .+ $($script:Dot) S\d+$"
  Check ([bool]$season) "the Game folder line says the season: $(if ($season) { $season.Current.Name })"
}

Case 'version-whatsnew.copy-puts-the-bug-report-line-on-the-clipboard' -Tags window, whatsnew, settings {
  ClickEl (ById 'SideBar_Settings') "Settings"
  try { [System.Windows.Forms.Clipboard]::Clear() } catch { }
  ClickEl (WaitUntil { ByTip 'Copy for a bug report' } "Copy") "Copy for a bug report"
  # Read again while the program still holds the clipboard open from writing it.
  $copied = WaitUntil { $t = try { [System.Windows.Forms.Clipboard]::GetText() } catch { $null }; if ($t) { $t } } "the clipboard" 3000
  $dot = $script:Dot
  Check ($copied -match "^Project Diablo II Single Player Companion \d+\.\d+\.\d+.* $dot Project Diablo 2 .* $dot S\d+ $dot ") "the bug-report line: $copied"
  Check ([bool](Says 'Version copied.')) "the strip says it was copied"
}
