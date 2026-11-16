# Settings, what the program keeps and a report of a problem (data must not grow unchecked):
# that the data folder's row says how much the folder holds beside where it is, and that the report's row says what
# goes into it, its tick adding the open character and the vault.
#
# The report's button is not pressed here: it shows the file in Explorer, a window of another program. What a
# report holds is read back out of the zip without a window: tests/PD2SPCompanion.Shell.Tests/Home/ProblemReportTests.
#
# Nothing is changed.

Case 'settings-report.data-folder-says-its-size-and-the-report-row-what-goes-in' -Tags settings {
  ClickEl (ById 'SideBar_Settings') "Settings"
  $folder = WaitUntil { $t = ById 'Settings_DataFolder'; if ($t -and $t.Current.Name -match '\d (KB|MB|bytes)$') { $t } } "the data folder's size" 6000
  Check ($folder.Current.Name -match '^.+\\data . [\d.]+ (KB|MB|bytes)$') "the data folder's row says where it is and how much it holds ('$($folder.Current.Name -replace '[^\x20-\x7E]', '-')')"

  $tick = WaitUntil { ById 'Settings_ReportWithSaves' } "the report's tick" 3000
  try { $tick.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView(); Start-Sleep -Milliseconds 300 } catch { }
  try {
    Check ((ById 'Settings_ReportText').Current.Name -eq 'Logs and settings, in one zip') "a report is the logs and the settings ('$((ById 'Settings_ReportText').Current.Name)')"
    Check ($null -ne (ById 'Settings_Report')) "and has its button"
    ClickEl (ById 'Settings_ReportWithSaves') "the report's tick"
    Check ([bool](WaitUntil { if ((ById 'Settings_ReportText').Current.Name -eq 'Logs, settings, open character and vault, in one zip') { 'with' } } "the row saying so" 3000)) "ticked: the open character and the vault go in too"
  }
  finally {
    # Off again, as the program starts with it.
    try { if ((ById 'Settings_ReportText').Current.Name -ne 'Logs and settings, in one zip') { ClickEl (ById 'Settings_ReportWithSaves') "the report's tick, off" } } catch { }
  }
}
