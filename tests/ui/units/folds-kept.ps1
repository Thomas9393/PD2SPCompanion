# A section folded stays folded the next time the program opens (every fold kept in the user's
# settings; FoldMemory): the inventory's Equipped section folded, the program closed and opened
# again - still folded - and unfolded again.
#
# The one case that runs with the folds kept: fast.ps1 turns them off for the rest (PD2SPC_FOLDS), so they meet
# every section as a new install has it - and the unit tests run the same way, which is why this is here and
# not there: FoldMemory reads that switch once, as the program starts. What can be proven without a window -
# the header's press folding the section, a fold in the settings written and read back - is in
# tests/PD2SPCompanion.Shell.Tests/Window/FoldsTests.
#
# The case closes the program and starts it again itself. The settings it folds in are the run's copy, put
# back by the runner after it.

function CraftCells { @((All (Cond $AE::AutomationIdProperty 'Craft_Cell')) | Where-Object { -not $_.Current.IsOffscreen }) }
function Extra { ClickEl (ById 'SideBar_Extra') "Extra"; WaitUntil { ById 'Craft_Fold' } "the crafting wizard's heading" | Out-Null; Start-Sleep -Milliseconds 400 }
function Header([string]$starts) { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { $_.Current.Name -like "$starts*" -and -not $_.Current.IsOffscreen })[0] }
function Worn { Tile 'Stone Barb' }

Case 'folds-kept.folded-section-stays-folded-at-the-next-start' -Tags window, folds, settings -Before {
  Remove-Item Env:PD2SPC_FOLDS -ErrorAction SilentlyContinue
  # Whatever the player folded is not this case's: it starts from none kept.
  $text = [IO.File]::ReadAllText((SettingsFile)) -replace '(?s)"Folds":\s*\{.*?\},?\s*', ''
  [IO.File]::WriteAllText((SettingsFile), $text, (New-Object System.Text.UTF8Encoding($false)))
} {
  try {
    Start-Sleep -Milliseconds 500
    Check ($null -ne (Worn)) "to begin with the Equipped section is open: what is worn is in sight"
    ClickEl (Header 'Equipped') "the Equipped header"
    Check ([bool](WaitUntil { if (-not (Worn)) { 'folded' } } "Equipped folded" 3000)) "its header folds it"
    Check ([IO.File]::ReadAllText((SettingsFile)) -match '"EquipmentIsOpen":\s*false') "and the settings say so at once"

    # Closed and opened again, on the same pad and settings: the kit's own start, and its way to the Items page.
    $mode = $script:session.Mode
    End-Session
    Start-Sleep -Milliseconds 500
    Start-Session $pad $mode
    Reach 'Items'
    Start-Sleep -Milliseconds 500

    Check ($null -eq (Worn) -and $null -ne (Header 'Equipped')) "the program opened again: Equipped is still folded"
    ClickEl (Header 'Equipped') "the Equipped header, again"
    Check ([bool](WaitUntil { Worn } "Equipped unfolded" 3000)) "and unfolds"
  }
  finally { $env:PD2SPC_FOLDS = 'off' }
}

# The Extra page's two wizards, whose folds the user's settings keep too:
# both folded, the program closed and opened again - still folded - and unfolded again.
Case 'folds-kept.extra-pages-wizards-stay-folded-at-the-next-start' -Tags window, folds, settings, extra -Before {
  Remove-Item Env:PD2SPC_FOLDS -ErrorAction SilentlyContinue
  $text = [IO.File]::ReadAllText((SettingsFile)) -replace '(?s)"Folds":\s*\{.*?\},?\s*', ''
  [IO.File]::WriteAllText((SettingsFile), $text, (New-Object System.Text.UTF8Encoding($false)))
} {
  try {
    Extra
    Check ($null -ne (ByName 'Steel' -Prefix) -and @(CraftCells).Count -eq 70) "to begin with both wizards are open"
    ClickEl (ById 'Runes_Fold') "the rune wizard's heading"
    WaitUntil { -not (ByName 'Steel' -Prefix) } "the rune wizard folded" 3000 | Out-Null
    ClickEl (ById 'Craft_Fold') "the crafting wizard's heading"
    WaitUntil { if (@(CraftCells).Count -eq 0) { 'folded' } } "the crafting wizard folded" 3000 | Out-Null
    $kept = [IO.File]::ReadAllText((SettingsFile))
    Check ($kept -match '"RuneWizardViewModelIsOpen":\s*false' -and $kept -match '"CraftWizardViewModelIsOpen":\s*false') "both folds are in the settings at once"

    $mode = $script:session.Mode
    End-Session
    Start-Sleep -Milliseconds 500
    Start-Session $pad $mode
    Reach 'Items'
    Extra

    Check ($null -eq (ByName 'Steel' -Prefix) -and @(CraftCells).Count -eq 0 -and $null -ne (ById 'Runes_Fold')) "the program opened again: both are still folded"
    ClickEl (ById 'Runes_Fold') "the rune wizard's heading, again"
    ClickEl (ById 'Craft_Fold') "the crafting wizard's heading, again"
    Check ([bool](WaitUntil { if ((ByName 'Steel' -Prefix) -and @(CraftCells).Count -eq 70) { 'open' } } "both unfolded" 4000)) "and unfold"
  }
  finally { $env:PD2SPC_FOLDS = 'off' }
}
