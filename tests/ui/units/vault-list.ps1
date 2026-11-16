# A mode's several vaults, as only a window shows them: that Settings' Vault row has a box that says the open vault and opens the list over the
# page; that a name set in the list's box and Enter make a vault, and a row clicked opens it; that a row's Copy
# is there to press; and that the import's panel comes up for the file chosen, with its two ways in, its ticks
# and its IMPORT. The file picker is answered for the case (PD2SPC_PICK_OPEN).
#
# What each of them decides - the vault made, copied, opened and remembered, what the ticks count, what comes in
# and under which name - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultListTests.
#
# No character is opened: the Vault row is there without one, as it was for the script these cases come from.
Add-Type -AssemblyName System.Windows.Forms

function Says([string]$part) {
  Steady {
    foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
      if ($e.Current.Name.Contains($part) -and -not $e.Current.IsOffscreen) { return $e }
    }
  }
}
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 300 }
# An element by its id and its name: a row of the list, a row's button, a tick of the check list.
function Named([string]$id, [string]$name) {
  $c = New-Object System.Windows.Automation.AndCondition((Cond $AE::AutomationIdProperty $id), (Cond $AE::NameProperty $name))
  OnScreen $c
}
# What the Vault row's box says: the open vault's name.
function Open { $b = ById 'Vault_Open'; if ($b) { @($b.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })[0] } }
function List { ClickEl (WaitUntil { ById 'Vault_Open' } "the Vault row's box") "the vaults' list"; WaitUntil { ById 'Vault_New' } "the list" 3000 | Out-Null }
function SetText($el, [string]$text) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); Start-Sleep -Milliseconds 200 }

# The pad's own vault written out under another name - 6 items, one at its top, three under Charms (none of its
# own), and Jewels with one of its own and one in Magic - and the file picker told to answer with it.
$script:MakeShared = {
  $env:PD2SPC_VAULTS = "$pad\vtsmart"
  $file = Join-Path $pad 'Shared.pd2vault'
  Remove-Item $file -Force -ErrorAction SilentlyContinue
  & $script:Cli vaultio --export $file | Out-Null
  $env:PD2SPC_PICK_OPEN = $file
}

Case 'vault-list.new-vault-is-named-and-a-row-opens-it' -Tags vault, vault-list, settings -Start Home -Changes {
  ClickEl (WaitUntil { ById 'SideBar_Settings' } "Settings") "Settings"
  Check ([bool](WaitUntil { if ((Open) -eq (MainVault)) { 'named' } } "the Vault row" 8000)) "the Vault row's box says the open vault ('$(Open)')"

  # A new one, named in the list's box.
  List
  Check ([bool](Named 'Vault_Row' (MainVault))) "the list has the mode's first vault"
  ClickEl (ById 'Vault_New') "new vault"
  SetText (WaitUntil { ById 'Vault_NewName' } "the name box" 3000) 'Playtest'
  Keys '{ENTER}'
  Check ([bool](WaitUntil { Named 'Vault_Row' 'Playtest' } "Playtest's row" 5000)) "New vault: Playtest is in the list"
  Check (Test-Path "$pad\vtsmart\$(PadSeason)\Softcore\Playtest.pd2lib") "and is a file of the mode's"

  # A row clicked: that vault is open.
  ClickEl (WaitUntil { Named 'Vault_Row' 'Playtest' } "Playtest's row" 3000) "Playtest"
  Check ([bool](WaitUntil { if ((Open) -eq 'Playtest') { 'open' } } "Playtest open" 5000)) "a row clicked: the box says Playtest"
  Check ([bool](Says 'Opened Playtest.')) "and the strip says so"
}

Case 'vault-list.copy-makes-a-vault-of-the-row' -Tags vault, vault-list, settings -Start Home -Changes {
  ClickEl (WaitUntil { ById 'SideBar_Settings' } "Settings") "Settings"
  WaitUntil { if ((Open) -eq (MainVault)) { 'named' } } "the Vault row" 8000 | Out-Null
  List
  ClickEl (Named 'Vault_Copy' (MainVault)) "copy $(MainVault)"
  Check ([bool](WaitUntil { if (Test-Path "$pad\vtsmart\$(PadSeason)\Softcore\$(MainVault) (2).pd2lib") { 'made' } } "the copy" 5000)) "Copy: $(MainVault) (2) is made"
  Keys '{ESC}'
}

Case 'vault-list.import-ticks-and-comes-in-as-a-folder' -Tags vault, vault-list, settings -Start Home -Before $script:MakeShared {
  try {
    ClickEl (WaitUntil { ById 'SideBar_Settings' } "Settings") "Settings"
    WaitUntil { if ((Open) -eq (MainVault)) { 'named' } } "the Vault row" 8000 | Out-Null

    # The import, as a folder of the open vault: Jewels unticked, and what is beneath it with it.
    List
    ClickEl (ById 'Vault_Import') "import"
    Check ([bool](WaitUntil { Says 'Import Shared' } "the import's panel" 8000)) "Import: the panel names the file"
    $count = { $c = ById 'Import_Count'; if ($c) { $c.Current.Name } }
    # The count comes a beat after the panel's title: read the file first (one run in two).
    Check ([bool](WaitUntil { if ((& $count) -eq '6 / 6 items') { 'counted' } } "the whole file counted" 3000)) "as a vault, the whole file ('$(& $count)')"
    ClickEl (ById 'Import_AsFolder') "as a folder"
    Check ([bool](WaitUntil { Named 'Import_Tick' 'Jewels' } "the check list" 3000)) "as a folder: the check list has the file's folders"
    Check ([bool](Named 'Import_Tick' 'Items in this folder')) "and an entry for a folder's own items"
    ClickEl (Named 'Import_Tick' 'Jewels') "Jewels unticked"
    Check ([bool](WaitUntil { if ((& $count) -eq '4 / 6 items') { 'counted' } } "the count" 3000)) "Jewels unticked: '$(& $count)'"
    if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-import.png') }
    ClickEl (ById 'Import_Confirm') "IMPORT"
    Check ([bool](WaitUntil { Says 'Imported as Shared: 4 items.' } "the import" 10000)) "IMPORT: the strip says what came"
    Check ((Open) -eq (MainVault)) "the vault open is still the vault open ('$(Open)')"
  }
  finally { $env:PD2SPC_PICK_OPEN = $null }
}

Case 'vault-list.import-as-a-vault-opens-it' -Tags vault, vault-list, settings -Start Home -Before $script:MakeShared {
  try {
    ClickEl (WaitUntil { ById 'SideBar_Settings' } "Settings") "Settings"
    WaitUntil { if ((Open) -eq (MainVault)) { 'named' } } "the Vault row" 8000 | Out-Null

    # As a vault of its own, which is how the panel comes up: added, and opened.
    List
    ClickEl (ById 'Vault_Import') "import"
    WaitUntil { Says 'Import Shared' } "the import's panel" 8000 | Out-Null
    ClickEl (WaitUntil { ById 'Import_Confirm' } "IMPORT" 3000) "IMPORT"
    Check ([bool](WaitUntil { Says 'Imported and opened Shared.' } "the import" 10000)) "as a vault: the strip says it is opened"
    Check ([bool](WaitUntil { if ((Open) -eq 'Shared') { 'open' } } "Shared open" 5000)) "the box says Shared"
    Check (Test-Path "$pad\vtsmart\$(PadSeason)\Softcore\Shared.pd2lib") "and it is a file of the mode's"
    List
    Check ([bool](Named 'Vault_Row' (MainVault))) "the one open before is still in the list"
  }
  finally { $env:PD2SPC_PICK_OPEN = $null }
}

# The vaults' backups: listed from the vaults' list, and one brought back through the import -
# a vault of its own, under the name of the vault it is a copy of. A copy is laid in the pad's Backups for the case.
$script:MakeCopy = {
  $copies = Join-Path $pad 'vtsmart\Backups'
  New-Item -ItemType Directory -Force $copies | Out-Null
  Copy-Item "$pad\vtsmart\$(PadSeason)\Softcore\$(MainVault).pd2lib" (Join-Path $copies "$(MainVault).cleared.20261012-120000.pd2lib") -Force
}
Case 'vault-list.backup-is-listed-and-brought-back-as-a-vault' -Tags vault, vault-list, settings -Start Home -Changes -Before $script:MakeCopy {
  ClickEl (WaitUntil { ById 'SideBar_Settings' } "Settings") "Settings"
  WaitUntil { if ((Open) -eq (MainVault)) { 'named' } } "the Vault row" 8000 | Out-Null
  List
  ClickEl (WaitUntil { ById 'Vault_Backups' } "the list's Vault backups button" 3000) "Vault backups"
  $row = WaitUntil { Named 'VaultCopy_Row' (MainVault) } "the copy's row" 4000
  Check ($null -ne $row) "Vault backups: the copy of $(MainVault) is listed"
  Check ([bool](Says 'Before cleaning')) "with why it was taken"
  if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-backups.png') }

  # Back, a layer up: the vaults' list again - and the backups' list from there once more.
  ClickEl (ById 'VaultCopies_Back') "Back to vaults"
  Check ([bool](WaitUntil { ById 'Vault_New' } "the vaults' list" 3000)) "Back: the vaults' list again"
  ClickEl (ById 'Vault_Backups') "Vault backups"
  $row = WaitUntil { Named 'VaultCopy_Row' (MainVault) } "the copy's row" 4000

  # Brought back: the import's panel, and IMPORT makes a vault of it beside the one there is.
  ClickEl $row "the copy"
  ClickEl (WaitUntil { ById 'Import_Confirm' } "the import's IMPORT" 4000) "IMPORT"
  Check ([bool](WaitUntil { if (Test-Path "$pad\vtsmart\$(PadSeason)\Softcore\$(MainVault) (2).pd2lib") { 'made' } } "the vault brought back" 6000)) "brought back as a vault of its own: $(MainVault) (2)"
  Check ([bool](WaitUntil { if ((Open) -eq "$(MainVault) (2)") { 'open' } } "it opened" 5000)) "and it is the open vault ('$(Open)')"
}
