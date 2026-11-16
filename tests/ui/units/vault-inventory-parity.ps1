# The vault and the inventory alike, as only a window shows it: the Delete key and Ctrl + A
# reach the inventory's bin - Ctrl + A taking the section the pointer is over - and Esc, and a right click, reach a
# vault card while the mouse is dragging it, and put it back - nothing withdrawn, nothing written.
#
# What the two pages decide - the inventory's title row, Duplicate taking the picks, the bin asking again once
# Ctrl + A has widened them, what the vault's bin says, a carry ended taking nothing - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Vault/VaultInventoryParityTests.
#
# For the drags: the vault on the right page; on the left, the second pane on Shared Page 8.

Add-Type -AssemblyName System.Windows.Forms
$saves = "$pad\saves"
$tomeId = 2218325626
function Stamp($file) { (WrittenAt "$saves\$file") }
function InStash { (& $script:Cli dump "$saves\pd2_shared.stash" 2>&1 | Out-String) -match "id $tomeId\b" }
# A vault item: its tile on the folder's pages.
function Card($name) { VaultTile $name }
function Texts { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) }
function Said([string]$like) { @(Texts | Where-Object { $_ -like $like }).Count -gt 0 }
function PressKeys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }
function CtrlClick($el, [string]$what) { Front; [Fast]::keybd_event(0x11, 0, 0, 0); try { ClickEl $el $what } finally { [Fast]::keybd_event(0x11, 0, 2, 0) } }
# A drag of a vault card that something interrupts halfway (Esc, a right-click), then let go on the cell.
function DragInterrupted($from, $to, [scriptblock]$interrupt) {
  $b = $from.Current.BoundingRectangle
  $a = @([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)); $z = @(([int]$script:r.X + $to[0]), ([int]$script:r.Y + $to[1]))
  Front
  [Fast]::SetCursorPos($a[0], $a[1]) | Out-Null; [Fast]::mouse_event(2, 0, 0, 0, 0)
  try {
    for ($i = 1; $i -le 12; $i++) {
      [Fast]::SetCursorPos([int]($a[0] + ($z[0] - $a[0]) * $i / 12), [int]($a[1] + ($z[1] - $a[1]) * $i / 12)) | Out-Null; Start-Sleep -Milliseconds 15
      if ($i -eq 8) { & $interrupt; Start-Sleep -Milliseconds 150 }
    }
    Start-Sleep -Milliseconds 100
  }
  finally { [Fast]::mouse_event(4, 0, 0, 0, 0) }
}
# An empty cell low on Shared Page 8, in the second pane: its fourth across, twelfth down (vault-drag-shared.ps1's).
function LowCell($tome) { PaneCell 1 3 11 (CellPx $tome) }
# The second pane on Shared Page 8, the vault on the right page at its top.
function TomeBesideSharedPage {
  WaitUntil { ItemsPage } "items page" | Out-Null
  Pane 0 'Personal Stash'
  Pane 1 'Shared Page 8'
  Start-Sleep -Milliseconds 500
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Right' } "vault bookmark (right)") "vault (right)"
  WaitUntil { VaultPages } "the vault on the right" | Out-Null
  VaultGoTo 'Vault'
}

# Nothing is deleted: the bin only asks, twice. The keys are sent through the keyboard.
Case 'vault-inventory-parity.delete-key-asks-again-after-ctrl-a' -Tags items, keys -Typing {
  Pane 0 'Personal Stash'
  $a = 'Steel Ring of Skill'; $b = 'Felicitous Ring of Regrowth'

  # Armed for two by the key, Ctrl+A widens the picks: Delete asks again, nothing goes.
  ClickUntil { ClickEl (Tile $a) $a } { OnEdit $a } "$a selected" 800 | Out-Null
  CtrlClick (Tile $b) $b
  PressKeys '{DEL}'
  Check (Said 'Delete 2 selected items? *') "Delete asks for two"
  $held = @(Tiles $a).Count + @(Tiles $b).Count
  PressKeys '^a'
  Check ($null -eq (ByTip 'Confirm deletion' -Prefix)) "Ctrl+A: the bin is no longer armed"
  PressKeys '{DEL}'
  Check (@(Tiles $a).Count + @(Tiles $b).Count -eq $held -and (Said 'Delete * selected items? *')) "the next Delete asks again, nothing deleted"
  PressKeys '{ESC}'

  # The inventory in the first pane again, as the page was found.
  Pane 0 'Inventory'
}

Case 'vault-inventory-parity.esc-puts-back-a-dragged-card' -Tags vault, drag, keys -Changes {
  TomeBesideSharedPage
  $tome = WaitUntil { Card 'Tome of Town Portal' } "the tome's card"
  $was = Stamp 'pd2_shared.stash'
  DragInterrupted $tome (LowCell $tome) { [Fast]::keybd_event(0x1B, 0, 0, 0); [Fast]::keybd_event(0x1B, 0, 2, 0) }
  Start-Sleep -Milliseconds 1500
  Check ((Stamp 'pd2_shared.stash') -eq $was -and -not (InStash)) "Esc during a vault drag: put back, nothing written"
  Check ($null -ne (Card 'Tome of Town Portal')) "and the card is where it was"
}

Case 'vault-inventory-parity.right-click-puts-back-a-dragged-card' -Tags vault, drag -Changes {
  TomeBesideSharedPage
  $tome = WaitUntil { Card 'Tome of Town Portal' } "the tome's card"
  $was = Stamp 'pd2_shared.stash'
  DragInterrupted $tome (LowCell $tome) { [Fast]::mouse_event(8, 0, 0, 0, 0); [Fast]::mouse_event(16, 0, 0, 0, 0) }
  Start-Sleep -Milliseconds 1500
  Check ((Stamp 'pd2_shared.stash') -eq $was -and -not (InStash)) "right-click during a vault drag: put back, nothing written"
  Check ($null -ne (Card 'Tome of Town Portal')) "and the card is where it was"
}
