# The patch guard, as only a started program and its window show it (of the utmost importance):
# that the start checks a new PD2 build and the
# strip says what came of it; that a hold at start still lets a character open, and Settings draws an ACCEPT whose
# click lifts it; that the window's coming back looks at the build again, and a build changed while open is held
# with no ACCEPT drawn.
#
# What the guard decides - the first run's silence, which build becomes the known good one, the words, that an
# edit is not written while held and is written after Accept - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Season/PatchGuardTests.
#
# The build is faked through PD2SPC_FAKE_BUILD (a file, so it can change while the program is open), the
# unreadable save through PD2SPC_FAKE_UNREADABLE; a test's known good build lives beside its save folder
# (<pad>\pd2spc-verified-build.txt), never in the player's settings. Each case starts the program for itself, and
# takes its switches and the known build away again when it ends.

function Verified { if (Test-Path "$pad\pd2spc-verified-build.txt") { (Get-Content "$pad\pd2spc-verified-build.txt" -Raw).Trim() } else { '' } }
function Strip { foreach ($t in 1..5) { try { return (@((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { $b = $_.Current.BoundingRectangle; -not $_.Current.IsOffscreen -and $b.Y -gt ($script:r.Y + 1320) -and $b.X -lt ($script:r.X + 1280) } | ForEach-Object { $_.Current.Name }) -join ' | ') } catch { Start-Sleep -Milliseconds 100 } } }
# The switches and the known build gone: the next start is a first run, which stores the real build without a word.
function Unfake {
  Remove-Item Env:\PD2SPC_FAKE_BUILD, Env:\PD2SPC_FAKE_UNREADABLE -ErrorAction SilentlyContinue
  Remove-Item "$pad\pd2spc-verified-build.txt", "$pad\fake-build.txt" -ErrorAction SilentlyContinue
}

Case 'patch-guard.new-build-is-checked-at-start-and-said' -Tags guards, patch-guard -Start Home -Before {
  Remove-Item Env:\PD2SPC_FAKE_UNREADABLE -ErrorAction SilentlyContinue
  Set-Content "$pad\pd2spc-verified-build.txt" "test-build-0" -NoNewline
  Set-Content "$pad\fake-build.txt" "test-build-a" -NoNewline
  $env:PD2SPC_FAKE_BUILD = "@$pad\fake-build.txt"
} {
  try {
    $said = WaitUntil { $s = Strip; if ($s -match 'read back exactly') { $s } } "the strip saying the saves read back" 15000
    Check ($said -match 'PD2 changed') "a new build, all read back: said so ($said)"
    Check ((Verified) -eq 'test-build-a') "it became the known good build"
  }
  finally { Unfake }
}

Case 'patch-guard.held-at-start-accept-in-settings-lifts-it' -Tags guards, patch-guard, settings -Start Home -Before {
  Set-Content "$pad\pd2spc-verified-build.txt" "test-build-a" -NoNewline
  Set-Content "$pad\fake-build.txt" "test-build-b" -NoNewline
  $env:PD2SPC_FAKE_BUILD = "@$pad\fake-build.txt"
  $env:PD2SPC_FAKE_UNREADABLE = 'PhySpearZon.d2s'
} {
  try {
    WaitUntil { (Strip) -match 'do not read back' } "the strip saying writes are held" 15000 | Out-Null
    Check ((Verified) -eq 'test-build-a') "the known good build unchanged while held"
    ClickEl (WaitUntil { ByName 'Open' } "Load") "Load"
    WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "character loaded while held" | Out-Null
    Check $true "the character still opens"

    # Accept in Settings lifts it.
    ClickEl (ById 'SideBar_Settings') "Settings"
    WaitUntil { ByName 'ACCEPT' } "the Accept button" | Out-Null
    ClickUntil { if ($a = ByName 'ACCEPT') { ClickEl $a "Accept" } } { (Verified) -eq 'test-build-b' } "the accepted build stored" 800 | Out-Null
    Check $true "Accept made it the known good build"
  }
  finally { Unfake }
}

Case 'patch-guard.build-changed-while-open-is-seen-on-coming-back' -Tags guards, patch-guard -Start Home -Before {
  Remove-Item Env:\PD2SPC_FAKE_UNREADABLE -ErrorAction SilentlyContinue
  Set-Content "$pad\pd2spc-verified-build.txt" "test-build-b" -NoNewline
  Set-Content "$pad\fake-build.txt" "test-build-b" -NoNewline
  $env:PD2SPC_FAKE_BUILD = "@$pad\fake-build.txt"
} {
  try {
    Start-Sleep -Milliseconds 1500
    Set-Content "$pad\fake-build.txt" "test-build-c" -NoNewline
    # An activation looks again: away to the desktop and back.
    [Fast]::ShowWindow($script:proc.MainWindowHandle, 6) | Out-Null
    Start-Sleep -Milliseconds 400
    Front
    WaitUntil { (Strip) -match 'changed while open' } "the strip saying restart" 8000 | Out-Null
    ClickEl (ById 'SideBar_Settings') "Settings"
    Start-Sleep -Milliseconds 600
    Check ($null -eq (ByName 'ACCEPT')) "changed while open: held, and no Accept"
  }
  finally { Unfake }
}
