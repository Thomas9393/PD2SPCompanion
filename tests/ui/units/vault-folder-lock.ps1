# A vault folder's lock, as only a window shows it: that the tools' lock is there to press, lit while the folder shown is locked and saying what it holds
# back, and that Ctrl + A on a locked folder picks nothing and says so. (A locked folder's row wears a small lock -
# vault-tree.ps1 - and the lock is set for the folder shown.)
#
# What the lock holds back and says is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Vault/VaultFolderLockTests.
#
# On the pad's vault: Small, holding Small Charm of Fortune and Garnet Small Charm of Life side by side.
Add-Type -AssemblyName System.Windows.Forms

function Strip([string]$text, [int]$ms = 4000) { try { [bool](WaitUntil { ByName $text } "the strip: $text" $ms) } catch { $false } }
# The vault on the left page, in Small, its two charms drawn; the grail on the right.
function InSmall {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  ClickEl (ById 'Bookmark_Grail') "grail (right)"
  WaitUntil { -not (ById 'Item_Name') } "the Edit page gone" | Out-Null
  VaultGoTo 'Vault'
  VaultGoTo 'Small'
  WaitUntil { Tile 'Small Charm of Fortune' } "the charms" | Out-Null
  Start-Sleep -Milliseconds 400
}

Case 'vault-folder-lock.lock-reaches-the-folder' -Tags vault, vault-folder-lock -Changes {
  InSmall

  # Off to begin with; on, its tooltip says what it holds back; Ctrl + A then picks nothing and says so.
  $lock = ById 'Vault_FolderLock'
  Check ($lock.Current.HelpText -eq 'Folder lock off') "the tools' lock is there with the tree in sight, off to begin with ('$($lock.Current.HelpText)')"
  ClickEl $lock "the folder's lock"
  Check ([bool](WaitUntil { if ((ById 'Vault_FolderLock').Current.HelpText -like 'Folder lock on*Store all skips it') { 'on' } } "the lock on" 3000)) "locked: its tooltip says so, and what it holds back ('$((ById 'Vault_FolderLock').Current.HelpText -replace "`r?`n", ' | ')')"
  ClickEl (Tile 'Small Charm of Fortune') "a charm"; Start-Sleep -Milliseconds 300
  Front; [System.Windows.Forms.SendKeys]::SendWait('^a'); Start-Sleep -Milliseconds 500
  Check (Strip 'Folder locked.') "Ctrl + A on a locked folder: 'Folder locked.'"

  ClickEl (ById 'Vault_FolderLock') "the folder's lock, off"
  Check ([bool](WaitUntil { if ((ById 'Vault_FolderLock').Current.HelpText -eq 'Folder lock off') { 'off' } } "the lock off" 3000)) "and let go again"
}
