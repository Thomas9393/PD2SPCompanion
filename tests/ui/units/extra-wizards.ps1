# The Extra page's two wizards, as only a window shows them: that a row of the rune wizard says in a tooltip what its runeword grants;
# that the crafting wizard's chart is drawn, a recipe clicked is told of under it and a click anywhere else
# lets it go, the strip standing where it stood; and that both cards fold.
#
# What a cell counts, what the strip says of a recipe and what a row's tooltip holds are proven without a
# window: tests/PD2SPCompanion.Shell.Tests/Extra/CraftWizardTests, tests/PD2SPCompanion.Tests/Collection/CraftPlanTests.
#
# Nothing is edited.

function Extra {
  ClickEl (ById 'SideBar_Extra') "Extra"
  WaitUntil { ById 'Craft_Fold' } "the crafting wizard's heading" | Out-Null
}
function CraftCells { @((All (Cond $AE::AutomationIdProperty 'Craft_Cell')) | Where-Object { -not $_.Current.IsOffscreen }) }
# Both cards open, whichever way a case before left them.
function BothOpen {
  if (-not (ByName 'Steel' -Prefix)) { ClickEl (ById 'Runes_Fold') "the rune wizard, open"; WaitUntil { ByName 'Steel' -Prefix } "the runewords" | Out-Null }
  if (@(CraftCells).Count -eq 0) { ClickEl (ById 'Craft_Fold') "the crafting wizard, open"; WaitUntil { if (@(CraftCells).Count -gt 0) { 'chart' } } "the chart" | Out-Null }
}

# With a character open the mode is the character's: the pair stands, dimmed, and each button says why.
Case 'extra-wizards.mode-pair-is-dimmed-with-a-character-open' -Tags extra, tooltip {
  Extra
  $soft = WaitUntil { ById 'Extra_Softcore' } "the pair's first button"
  $hard = ById 'Extra_Hardcore'
  Check ($null -ne $hard -and -not $soft.Current.IsEnabled -and -not $hard.Current.IsEnabled) "a character open: both buttons are dimmed"
  Check ($soft.Current.HelpText -eq 'Follows open character.') "and say why ('$($soft.Current.HelpText)')"
  Hover $soft
  $tip = (TipTexts) -join ' | '
  Check ($tip -like '*Follows open character.*') "hovered, the dimmed button shows it ('$tip')"
}

Case 'extra-wizards.runeword-row-says-what-it-grants' -Tags extra, wizards, tooltip {
  Extra; BothOpen
  Hover (WaitUntil { ByName 'Steel' -Prefix } "Steel's row")
  $tip = (TipTexts) -join ' | '
  Check ($tip -like 'Steel*' -and $tip -like '*Increased Attack Speed*') "a runeword's row says what the word grants ('$tip')"
}

Case 'extra-wizards.recipe-clicked-is-told-of-and-let-go-elsewhere' -Tags extra, wizards {
  Extra; BothOpen
  $cells = @(CraftCells)
  Check ($cells.Count -eq 70) "the chart is drawn: $($cells.Count) recipes"

  ClickEl $cells[0] "the chart's first recipe"
  $title = WaitUntil { $t = ById 'Craft_Title'; if ($t -and $t.Current.Name -eq 'Blood Amulet') { $t } } "the recipe's name" 3000
  Check ($null -ne $title) "a recipe clicked is told of under the chart ('$($title.Current.Name)')"
  $grants = ById 'Craft_Grants'
  Check ($null -ne $grants -and $grants.Current.Name.Length -gt 10) "with what its family grants ('$(($grants.Current.Name -split "`n")[0].Trim())' ...)"
  $foot = (ById 'Craft_Grants').Current.BoundingRectangle; $lastRow = $cells[-1].Current.BoundingRectangle

  # Anywhere else: the page's own title.
  ClickEl (ByName 'Extra') "the page's title"
  $none = $null
  try { $none = WaitUntil { ByName 'No recipe selected' } "the strip, with no recipe" 3000 } catch { }
  Check ($null -ne $none -and $null -eq (ById 'Craft_Title')) "a click elsewhere lets the recipe go"
  $after = @(CraftCells)[-1].Current.BoundingRectangle
  Check ([Math]::Abs($after.Y - $lastRow.Y) -lt 1 -and $none.Current.BoundingRectangle.Y -gt $after.Y) "and nothing moved: the chart stands where it stood, the strip's room under it"
}

Case 'extra-wizards.both-cards-fold' -Tags extra, wizards {
  Extra; BothOpen
  $craftAt = (ById 'Craft_Fold').Current.BoundingRectangle.Y

  ClickEl (ById 'Runes_Fold') "the rune wizard's heading"
  WaitUntil { -not (ByName 'Steel' -Prefix) } "the runewords away" 3000 | Out-Null
  $up = (ById 'Craft_Fold').Current.BoundingRectangle.Y
  Check ($up -lt $craftAt - 50) "the rune wizard folds, and the crafting wizard comes up under its heading ($([int]$craftAt) -> $([int]$up))"

  ClickEl (ById 'Craft_Fold') "the crafting wizard's heading"
  WaitUntil { if (@(CraftCells).Count -eq 0) { 'folded' } } "the chart away" 3000 | Out-Null
  Check ($null -ne (ById 'Craft_Summary')) "the crafting wizard folds: its heading and its count stay"

  ClickEl (ById 'Craft_Fold') "the crafting wizard, open again"
  ClickEl (ById 'Runes_Fold') "the rune wizard, open again"
  WaitUntil { if ((ByName 'Steel' -Prefix) -and @(CraftCells).Count -eq 70) { 'open' } } "both open again" 4000 | Out-Null
  Check ($true) "and both open again"
}

Case 'extra-wizards.bases-toggle-puts-the-counts-on-the-rows' -Tags extra, wizards {
  Extra; BothOpen
  $toggle = WaitUntil { ById 'Runes_Bases' } "the bases' toggle"
  Check ($toggle.Current.HelpText -like 'Count bases*Bases counted from vault folder Bases*') "the toggle says what it counts and from where ('$($toggle.Current.HelpText -replace "`r?`n", ' | ')')"
  # On to begin with, whatever the settings the run began from had it as: the developer's own may have it off.
  if (-not (ByName 'Steel (' -Prefix)) { ClickEl $toggle "the bases' toggle, on to begin with"; Start-Sleep -Milliseconds 400 }
  $counted = [bool](WaitUntil { ByName 'Steel (' -Prefix } "Steel, counted" 3000)
  Check ($counted) "counted: a row's name has its number after it ('$((ByName 'Steel' -Prefix).Current.Name)')"

  ClickEl $toggle "the bases' toggle, off"
  $plain = $null
  try { $plain = WaitUntil { ByName 'Steel' } "Steel, with no number" 3000 } catch { }
  Check ($null -ne $plain) "off: the rows are as they were, by their runes alone"

  ClickEl (ById 'Runes_Bases') "the bases' toggle, on again"
  Check ([bool](WaitUntil { ByName 'Steel (' -Prefix } "Steel, with its number" 3000)) "and on again: the number is back"
}
