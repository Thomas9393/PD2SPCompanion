# The folder tree's folds are a session's (as in Windows Explorer, which does not
# memorize a fold across sessions, so a user need not clean them up by hand): a folder unfolded in
# the left vault page's tree is unfolded while the program runs, and folded again when the program is opened
# again - the tree shows the vault's own folders and no more. Nothing of a fold is written to the settings, and a
# fold kept there by an older version ("VaultFold." by the vault, the page and the folder)
# is not read. Only a second start of the program shows that, on settings that keep the folds - which every other
# case runs without (PD2SPC_FOLDS).
#
# What a tree does while the program runs - a chevron's fold, the move picker following it, the right page's tree
# being its own - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultTreeFoldsTests.
#
# Changes nothing in the vault.

function Half($el) { PageOf $el }
# A row of a page's tree, or its chevron (-Id FoldPlace): with the vault on both pages there is one on each.
function Row([string]$side, $name, [string]$Id = 'FolderRow') {
  Steady {
    foreach ($e in (All (Cond $AE::AutomationIdProperty $Id))) {
      if (-not $e.Current.IsOffscreen -and (Half $e) -eq $side -and $e.Current.Name -eq $name) { return $e }
    }
  }
}
# The left page's vault, its tree in sight - the player may keep it put away.
function OpenVault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  Start-Sleep -Milliseconds 500
  if (-not (ById 'Vault_Tree')) { ClickEl (ById 'Vault_TreeToggle') "folder tree on"; WaitUntil { ById 'Vault_Tree' } "the tree" | Out-Null }
  WaitUntil { Row 'Left' 'Charms' } "the tree's Charms" | Out-Null
}

Case 'vault-tree-folds.fold-is-the-sessions-and-gone-when-the-program-opens-again' -Tags vault, folders, settings -Before {
  # The settings' folds read and kept for real: every other case starts the program with them off.
  $env:PD2SPC_FOLDS = $null
  # And an older version's folds in them, where the settings hold any - the run's are a copy of the developer's
  # own, which may hold such folds by the same ids - every one said to be unfolded: not read. (That they are let
  # go as the settings are read is FoldsTests'.)
  $text = [IO.File]::ReadAllText((SettingsFile)) -replace '("VaultFold[^"]*"\s*:\s*)false', '${1}true'
  [IO.File]::WriteAllText((SettingsFile), $text, (New-Object System.Text.UTF8Encoding($false)))
} {
  try {
    OpenVault

    # As it opens: Charms folded, its folder Small out of the tree. Unfolded by its chevron.
    Check ($null -eq (Row 'Left' 'Small') -and $null -ne (Row 'Left' 'Jewels')) "the tree opens folded: Small is not in it"
    ClickEl (Row 'Left' 'Charms' 'FoldPlace') "unfold Charms"
    Check ([bool](WaitUntil { Row 'Left' 'Small' } "Small listed" 3000)) "Charms unfolded: Small is in the tree"

    # Unfolded while the program runs: another page gone to and the vault come back to.
    Reach 'Items'
    OpenVault
    Check ($null -ne (Row 'Left' 'Small')) "the vault come back to: Charms is unfolded still"

    # The program closed and started again, on the same pad and the same settings.
    $mode = $script:session.Mode
    End-Session
    Start-Sleep -Milliseconds 500
    Start-Session $pad $mode
    Reach 'Items'

    OpenVault
    Start-Sleep -Milliseconds 400
    Check ($null -eq (Row 'Left' 'Small') -and $null -ne (Row 'Left' 'Charms')) "the program opened again: the tree is folded, Small out of it"
  }
  finally { $env:PD2SPC_FOLDS = 'off' }
}
