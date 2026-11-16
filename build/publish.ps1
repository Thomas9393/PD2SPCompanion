# A release of Project Diablo II Single Player Companion:
#   1. raise VersionPrefix in src/Directory.Build.props and write its section in CHANGELOG.md;
#   2. run this: it checks both, publishes with the win-x64 profile and zips the folder as
#      dist\Project Diablo II Single Player Companion <version>.zip;
#   3. tag the commit it was built from with the line it prints (it does not tag by itself).
#
#   powershell -File build\publish.ps1
param([switch]$KeepFolder)
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

[xml]$props = Get-Content (Join-Path $repo 'src\Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.VersionPrefix } | Select-Object -First 1).VersionPrefix
if (-not $version) { throw "No VersionPrefix in src\Directory.Build.props." }

$notes = Get-Content (Join-Path $repo 'CHANGELOG.md') -Raw
if ($notes -notmatch "(?m)^## \[$([regex]::Escape($version))\]") { throw "CHANGELOG.md has no section for $version." }

$dirty = git status --porcelain
if ($dirty) { Write-Warning "Uncommitted changes: the release would not match any commit." }

$folder = Join-Path $repo 'src\PD2SPCompanion.Shell.Desktop\bin\Publish\win-x64'

# From nothing: no Release build and no publish folder left over. A publish now and then fails in Avalonia's XAML
# step while a build before still holds a file, and the next one over that reported success with the native
# libraries left out - a zip whose program could not draw a window.
dotnet build-server shutdown | Out-Null
foreach ($left in @(Get-ChildItem (Join-Path $repo 'src') -Directory | ForEach-Object { "$($_.FullName)\obj\Release", "$($_.FullName)\bin\Release" }) + $folder) {
  if (Test-Path -LiteralPath $left) { Remove-Item -LiteralPath $left -Recurse -Force }
}

Write-Host "Publishing $version..."
# No build nodes kept for later: the ones a build leaves running hold its files, and build-server shutdown does not
# end them.
dotnet publish src\PD2SPCompanion.Shell.Desktop -p:PublishProfile=win-x64 -nologo -v q -nodeReuse:false
if ($LASTEXITCODE) { throw "dotnet publish failed." }

# What the program cannot start without, beside it: the native libraries stay out of the single file (the profile),
# and the licenses go with every copy.
$needed = 'PD2SPCompanion.exe', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'av_libglesv2.dll', 'e_sqlite3.dll', 'LICENSE', 'THIRD-PARTY-NOTICES.md'
$missing = @($needed | Where-Object { -not (Test-Path -LiteralPath (Join-Path $folder $_)) })
if ($missing) { throw "The published folder lacks $($missing -join ', '): not zipped." }
if (Test-Path -LiteralPath (Join-Path $folder 'Data')) { throw "The published folder holds a Data folder: a copy must start with its own." }
$dist = Join-Path $repo 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "Project Diablo II Single Player Companion $version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip

$exe = Get-Item (Join-Path $folder 'PD2SPCompanion.exe')
Write-Host ("Built:  {0} ({1})" -f $exe.VersionInfo.ProductVersion, $exe.VersionInfo.ProductName)
Write-Host ("Zip:    {0} ({1:N0} MB)" -f $zip, ((Get-Item -LiteralPath $zip).Length / 1MB))
Write-Host "Tag:    git tag -a v$version -m `"Project Diablo II Single Player Companion $version`""
if (-not $KeepFolder) { Remove-Item -LiteralPath $folder -Recurse -Force }
