# A stash page laid out again, as only a window shows it:
# that the Sort menu is drawn on the header of a pane showing the personal stash and on no pane showing the
# inventory or the cube, and that the Close gaps button beside it reaches the page - read back from the save.
#
# What a sort and Close gaps decide - every item kept, none over another, from the top left, the first by name there,
# Undo, a locked page left as it lies, a shared page alone laid out - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Inventory/StashArrangeTests.
#
# The pad's personal stash holds 38 items laid out by hand, with gaps.

# The items of one page of a file, as the dump lists them: name, footprint, cell, and what tells it from the
# others - its name with its id, or its name alone for a rune or a gem, which has none. Not what sits in
# sockets: those lines stand further in. (A character's lines begin "  #45  [code]", a shared stash's
# "       [code]".)
function Page([string]$file, [string]$header) {
  $lines = @(& $script:Cli dump $file --no-stats 2>&1 | ForEach-Object { "$_" })
  $items = @(); $in = $false
  for ($n = 0; $n -lt $lines.Count; $n++) {
    if ($lines[$n] -match '^\s*== ') { $in = $lines[$n] -match ('^\s*== ' + [regex]::Escape($header) + ' \('); continue }
    if ($in -and $lines[$n] -match '^(?:  #\d+\s+|       )\[.{4}\] (.+?)   \((\d+)x(\d+),' ) {
      $name = $Matches[1]; $w = [int]$Matches[2]; $h = [int]$Matches[3]
      $facts = $lines[$n + 1]
      if ($facts -match '\bat (\d+),(\d+)') {
        $x = [int]$Matches[1]; $y = [int]$Matches[2]
        $id = if ($facts -match '\bid (\d+)') { "$name|$($Matches[1])" } else { $name }
        $items += [pscustomobject]@{ Name = $name; W = $w; H = $h; Id = $id; X = $x; Y = $y }
      }
    }
  }
  , $items
}
function Ids($items) { (@($items | ForEach-Object { $_.Id } | Sort-Object) -join ',') }
function Cells($items) { (@($items | ForEach-Object { "$($_.Id)@$($_.X),$($_.Y)" } | Sort-Object) -join ' ') }

function Strip([string]$text, [int]$ms = 4000) { try { [bool](WaitUntil { ByName $text } "the strip: $text" $ms) } catch { $false } }
function Sorts { @((All (Cond $AE::ControlTypeProperty $CT::Button)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.HelpText.StartsWith('Sort') }) }
# One pane, whichever the case before left: a second pane on a stash page has a Sort menu of its own.
function OnePane {
  if ((PaneCount) -gt 1) {
    ToggleSecondPane
    WaitUntil { if ((PaneCount) -eq 1) { 'one' } } "one pane shown" 4000 | Out-Null
  }
}

Case 'stash-arrange.sort-menu-stands-on-a-stash-page-only' -Tags stash-arrange, items {
  OnePane
  Pane 0 'Inventory'
  Start-Sleep -Milliseconds 400
  Check ((Sorts).Count -eq 0) "the inventory shown: no Sort menu"
  Pane 0 'Cube'
  Start-Sleep -Milliseconds 400
  Check ((Sorts).Count -eq 0) "the cube shown: none either"
  Pane 0 'Personal Stash'
  Check ([bool](WaitUntil { if ((Sorts).Count -eq 1) { 'one' } } "the Sort menu on the stash's header" 4000)) "the personal stash shown: its Sort menu"
  # As the next case expects the page.
  Pane 0 'Inventory'
}

Case 'stash-arrange.close-gaps-button-reaches-the-page' -Tags stash-arrange, items -Changes {
  $save = "$pad\saves\PhySpearZon.d2s"
  $start = Page $save 'stash'
  Check ($start.Count -eq 38) "start: the personal stash holds $($start.Count) items, as laid by hand"

  OnePane
  Pane 0 'Personal Stash'
  WaitUntil { (Sorts).Count -eq 1 } "the Sort menu on the stash's header" | Out-Null
  ClickEl (WaitUntil { ById 'Pane_CloseGaps' } "the Close gaps button" 3000) "Close gaps"
  Check (Strip 'Personal Stash: gaps closed.') "Close gaps: 'Personal Stash: gaps closed.'"
  Kept "the tidy"
  $tidy = Page $save 'stash'
  Check ((Ids $tidy) -eq (Ids $start)) "every item is still there, by its id ($($tidy.Count))"
  Check ((Cells $tidy) -ne (Cells $start)) "and something moved: the gaps are closed"
}

# A locked page: its Sort and Close gaps are dimmed, and lit again once it is unlocked (they stood
# lit and refused on the strip, where Swap beside them was dimmed). Nothing is moved; the lock is taken off again.
Case 'stash-arrange.locked-page-dims-sort-and-close-gaps' -Tags stash-arrange, items, page-lock {
  OnePane
  Pane 0 'Personal Stash'
  $gaps = WaitUntil { ById 'Pane_CloseGaps' } "the Close gaps button" 3000
  Check ($gaps.Current.IsEnabled) "unlocked: Close gaps is lit"

  ClickEl (WaitUntil { PageLock 'Personal Stash' } "the stash's lock") "lock the stash"
  try {
    Check ([bool](WaitUntil { if (-not (ById 'Pane_CloseGaps').Current.IsEnabled) { 'dim' } } "Close gaps dimmed" 3000)) "locked: Close gaps is dimmed"
    Check (@(Sorts | Where-Object { $_.Current.IsEnabled }).Count -eq 0) "and so is Sort"

    # And each says why, under the pointer, in its own tooltip - which a dimmed button still shows.
    Hover (ById 'Pane_CloseGaps')
    $said = @(TipTexts) -join ' | '
    Check ($said -like '*Close gaps*' -and $said -like '*Page locked.*') "Close gaps hovered, dimmed: its name and 'Page locked.' ('$said')"
    Hover (@(Sorts)[0])
    $said = @(TipTexts) -join ' | '
    Check ($said -like '*Sort*' -and $said -like '*Page locked.*') "Sort hovered, dimmed: its name and 'Page locked.' ('$said')"
  }
  finally {
    ClickEl (PageLock 'Personal Stash') "unlock the stash"
  }

  Check ([bool](WaitUntil { if ((ById 'Pane_CloseGaps').Current.IsEnabled) { 'lit' } } "Close gaps lit" 3000)) "unlocked again: Close gaps is lit"
  Pane 0 'Inventory'
}
