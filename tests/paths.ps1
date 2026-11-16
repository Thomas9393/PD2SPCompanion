# Where this machine keeps what the tests stand on, for the UI cases and the measuring scripts: the same rule as
# tests/paths.py. Each from its environment variable, else from the nearest pd2spc.local.env from the repo's folder
# up, else not known ($null). Dot-sourced by run.ps1, which needs the corpus before it reads fast.ps1, and by fast.ps1.
# What is known is put in the environment for what a case starts: the command line tool takes --game or PD2SPC_GAME
# and reads no file.
$script:GameRoot = $env:PD2SPC_GAME
$script:Corpus = $env:PD2SPC_CORPUS
$script:S13Tables = $env:PD2SPC_S13_TABLES
for ($at = [IO.Path]::GetFullPath("$PSScriptRoot\.."); $at; $at = Split-Path $at -Parent) {
  $localFile = Join-Path $at 'pd2spc.local.env'
  if (-not (Test-Path -LiteralPath $localFile)) { continue }
  foreach ($line in [IO.File]::ReadAllLines($localFile)) {
    if ($line.TrimStart().StartsWith('#') -or $line.IndexOf('=') -lt 1) { continue }
    $parts = $line.Split('=', 2)
    $known = $parts[1].Trim()
    switch ($parts[0].Trim()) {
      'PD2SPC_GAME' { if (-not $script:GameRoot) { $script:GameRoot = $known } }
      'PD2SPC_CORPUS' { if (-not $script:Corpus) { $script:Corpus = $known } }
      'PD2SPC_S13_TABLES' { if (-not $script:S13Tables) { $script:S13Tables = $known } }
    }
  }
  break
}
if ($script:GameRoot) { $env:PD2SPC_GAME = $script:GameRoot }
if ($script:Corpus) { $env:PD2SPC_CORPUS = $script:Corpus }
if ($script:S13Tables) { $env:PD2SPC_S13_TABLES = $script:S13Tables }
