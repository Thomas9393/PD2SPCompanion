# The titles' font, as only a start of the program shows it: Cinzel as shipped; a font put in the
# Fonts folder takes its place at the next start, and Settings > Folders says which is in use. The folder is the
# pad's (PD2SPC_FONTS, set by Launch), so the developer's own Fonts folder is never touched. The font put in is
# one Windows ships (Georgia).
#
# What needs no window - Cinzel with no folder, a folder with nothing to read, a file that is no font passed
# over - is in tests/PD2SPCompanion.Shell.Tests/Window/TitleFontTests. A font that does read is taken for as
# long as the program runs, so that one is here, with a start to itself; the pad's folder is taken out again at
# its end, or every case after it would run in Georgia.

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

Case 'title-font.settings-names-the-shipped-face' -Tags window, fonts, settings {
  ClickEl (ById 'SideBar_Settings') "Settings"
  Check ([bool](WaitUntil { ByName 'Cinzel' } "the title font line" 5000)) "no Fonts folder: Cinzel"
  Check ($null -eq (Says 'from the Fonts folder')) "and nothing said of the folder"
}

Case 'title-font.font-in-the-folder-is-taken-at-the-next-start' -Tags window, fonts, settings -Start Home -Before {
  $fonts = "$pad\fonts"
  $system = Join-Path $env:WINDIR 'Fonts\georgia.ttf'
  if (-not (Test-Path $system)) { throw "no font to test with: $system" }
  Remove-Item $fonts -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory $fonts -Force | Out-Null
  Copy-Item $system $fonts
} {
  try {
    ClickEl (ById 'SideBar_Settings') "Settings"
    $line = WaitUntil { Says 'from the Fonts folder' } "the folder's font named" 5000
    Check ($line.Current.Name.StartsWith('Georgia')) "a font in the folder: $($line.Current.Name)"
  }
  finally { Remove-Item "$pad\fonts" -Recurse -Force -ErrorAction SilentlyContinue }
}
