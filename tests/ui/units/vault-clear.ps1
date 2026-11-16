# Clean the open vault, as only a window shows it: that the Settings page's Clean row has
# its bin, the bin opens the check list, the ticks reach CLEAN, and CLEAN reaches the vault.
#
# What the list decides - nothing ticked to begin with, what is ticked deleted and the rest kept, the copy left in
# Backups - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultClearTests.

function Says([string]$part) {
  Steady {
    foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
      if ($e.Current.Name.Contains($part) -and -not $e.Current.IsOffscreen) { return $e }
    }
  }
}

Case 'vault-clear.check-list-waits-for-a-tick-then-cleans' -Tags vault, settings -Changes {
  ClickEl (WaitUntil { ById 'SideBar_Settings' } "Settings") "Settings"
  ClickEl (WaitUntil { $b = ById 'Vault_Clear'; if ($b -and $b.Current.IsEnabled) { $b } } "the Clean row's bin" 8000) "clean vault"
  Check ([bool](WaitUntil { Says "Clean $(MainVault)" } "the check list" 3000)) "the bin opens the check list, named for the vault"
  Check (-not (ById 'Clear_Confirm').Current.IsEnabled) "nothing ticked: CLEAN waits"

  # Shut by the cross in its header, with no CANCEL at its foot (every panel the same); opened again.
  Check ($null -eq (ByName 'CANCEL')) "the check list has no CANCEL"
  $cross = @((All (Cond $AE::HelpTextProperty 'Close')) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.AutomationId -ne 'CloseButton' })[0]
  $confirm = (ById 'Clear_Confirm').Current.BoundingRectangle
  Check ($null -ne $cross -and $cross.Current.BoundingRectangle.Y -lt $confirm.Y) "its cross stands in its header, over CLEAN"
  ClickEl $cross "the check list's cross"
  Check ([bool](WaitUntil { if (-not (ById 'Clear_Confirm')) { 'shut' } } "the check list shut" 3000)) "the cross shuts it, nothing cleaned"
  ClickEl (ById 'Vault_Clear') "clean vault, again"
  WaitUntil { ById 'Clear_Confirm' } "the check list again" 3000 | Out-Null

  ClickEl (ById 'Clear_Trades') "trade records"
  Check ([bool](WaitUntil { if ((ById 'Clear_Confirm').Current.IsEnabled) { 'ready' } } "CLEAN" 3000)) "ticked: CLEAN is there"
  ClickEl (ById 'Clear_Confirm') "CLEAN"
  Check ([bool](WaitUntil { Says 'Cleaned. Copy in vault backups.' } "the deletion" 8000)) "the strip says it is cleaned, and where the copy is"
  Check ([bool](WaitUntil { Says '0 trades' } "the Vault row" 3000)) "the Vault row counts no trades"
}
