# A stash page's lock, as only a window shows it (a page kept for maps stays out of a dump
# into the vault): that each pane draws the lock that is its page's - the inventory's own for the inventory, a
# page lock for the cube - and that the lock button and "Fast inventory storing" with its green tick reach the
# page: Shared Page 8 locked, the vault grows and Page 8 still holds every item it held.
#
# What the lock decides - which pages have one, what it says, the settings, the lock read back when the
# character is opened again, the two presses of the store - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Inventory/PageLockTests.

# A line of text anywhere in the window containing $part: the message strip's answer. Steadied (fast.ps1): a
# line the page had a moment ago may be gone while it redraws.
function Says([string]$part) {
  Steady {
    foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
      if ($e.Current.Name.Contains($part) -and -not $e.Current.IsOffscreen) { return $e }
    }
  }
}
function Page8 { if ((& $script:Cli dump "$pad\saves\pd2_shared.stash" 2>&1 | Out-String) -match '== page 8 \((\d+)\)') { [int]$Matches[1] } else { 0 } }
# One pane, whichever the case before left.
function OnePane { if ((PaneCount) -gt 1) { ToggleSecondPane } }

Case 'page-lock.every-page-has-its-lock-in-the-list' -Tags page-lock, items {
  OnePane
  Pane 0 'Inventory'
  $own = PageLock 'Inventory'
  Check ($null -ne $own -and $own.Current.HelpText -like 'Inventory lock*') "the inventory's row has its own lock ('$(($own.Current.HelpText -split "`n")[0])')"
  $cube = PageLock 'Cube'
  Check ($null -ne $cube -and $cube.Current.HelpText -like 'Page lock*') "the cube's row has a page lock, with the cube not shown"
  Check ($null -eq (PageLock 'Materials')) "the materials tab has none"
  $row = (PageRow 'Cube').Current.BoundingRectangle; $at = $cube.Current.BoundingRectangle
  Check ($at.X -ge $row.Right - 2 -and [Math]::Abs(($at.Y + $at.Height / 2) - ($row.Y + $row.Height / 2)) -lt 4) "a lock stands in a column of its own, right of its row and level with it"
}

Case 'page-lock.locked-page-stays-out-of-fast-storing' -Tags page-lock, items, vault -Changes {
  OnePane
  Pane 0 'Shared Page 8'
  $held = Page8
  Check ($held -gt 0) "Shared Page 8 holds $held to begin with"

  ClickEl (WaitUntil { PageLock 'Shared Page 8' } "Shared Page 8's lock") "the page's lock"
  WaitUntil { if ((PageLock 'Shared Page 8').Current.HelpText -like 'Page lock on*') { 'on' } } "page lock on" | Out-Null
  Check ([bool](Says 'Shared Page 8 locked.')) "said: Shared Page 8 locked."
  Step "Shared Page 8 locked"
  Check ((Get-Content (SettingsFile) -Raw) -match 'shared:pd2_shared\.stash:Page 8') "remembered in the settings"

  $rows = VaultCount
  ClickEl (WaitUntil { ByTip 'Fast inventory storing' -Prefix } "Store all") "Store all, Page 8 locked"
  ClickEl (WaitUntil { ByTip 'Confirm storing' -Prefix } "Store all armed") "Store all's green tick"
  $grew = $false
  try { WaitUntil { (VaultCount) -gt $rows } "the vault written" 8000 | Out-Null; $grew = $true } catch { }
  Check $grew "Store all stored the other pages ($rows -> $(VaultCount))"
  Check ((Page8) -eq $held) "Page 8 still holds its ${held}, the lock kept them out ($(Page8))"
}
