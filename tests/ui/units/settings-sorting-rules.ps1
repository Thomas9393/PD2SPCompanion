# Settings, the sorting rules' row (a row for the rules file sorted storing
# reads, written in the loot filter's words since the same day): that it names the file and how many rules it holds,
# and has its two buttons. Neither is pressed: one opens the file in another program, the other Explorer.
#
# Nothing is changed (the rules file the pad's vault folder lacks is written with the default, as any store would).

Case 'settings-sorting-rules.row-names-the-file-and-its-rules' -Tags settings {
  ClickEl (ById 'SideBar_Settings') "Settings"
  $row = WaitUntil { $t = ById 'Settings_SortingRules'; if ($t -and $t.Current.Name -match 'rules?$') { $t } } "the sorting rules' count" 6000
  Check ($row.Current.Name -match '^smart-sort\.filter . \d+ rules$') "the row names the file and its rules, none skipped ('$($row.Current.Name -replace '[^\x20-\x7E]', '-')')"
  Check ($null -ne (ByTip 'Edit sorting rules')) "it has its edit button"
  Check ($null -ne (ByTip 'Show in folder')) "and its folder button"
  if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'settings-sorting-rules.png') }
}
