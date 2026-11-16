# The day a vault item was stored, and the vault's sort, as only a window shows them: that a vault item's
# tooltip says the day it was stored, in a section of its own at the foot, and that the sort menu's choice is
# drawn - the folder laid out again in the order picked, and turned round by the same order picked again.
#
# What the page decides - the date itself, kept or new, the order laid out and turned round, that a sort writes
# nothing - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultStoredDayTests and VaultSheetTests.
#
# The vault on the left page, the grail on the right, in Charms' folder Small.

function Shown { VaultShown }
function Entry($name) { VaultRow $name -Listed }
function GoTo($name) {
  VaultList $name
  ClickEl (Entry $name) "folder '$name'"
  WaitUntil { (Shown) -like "*$name" } "the path says $name" 3000 | Out-Null
}
function X($name) { [int](Tile $name).Current.BoundingRectangle.X }
# An option of the sort menu: a button of that name in the menu's panel.
function Option($name) { foreach ($b in (All (Cond $AE::ControlTypeProperty $CT::Button))) { if (-not $b.Current.IsOffscreen -and $b.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $b } } }
function SortBy($name) {
  ClickUntil { ClickEl (ByTip 'Sort' -Prefix) "the sort menu" } { Option $name } "the sort menu's $name" 800 | Out-Null
  ClickEl (Option $name) "sort by $name"
  Start-Sleep -Milliseconds 500
}

$a = 'Small Charm of Fortune'; $b = 'Garnet Small Charm of Life'
# The vault on the left, the grail on the right, in Small with its two charms in sight.
function InSmall {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  ClickEl (ById 'Bookmark_Grail') "grail (right)"
  WaitUntil { -not (ById 'Item_Name') } "the Edit page gone" | Out-Null
  GoTo 'Small'
  WaitUntil { Tile $a } "the charms" | Out-Null
}

Case 'vault-stored-sort.the-day-stored-is-read-at-the-foot-of-the-items-tooltip' -Tags vault, tooltip {
  InSmall
  Hover (Tile $a)
  $tip = TipTexts
  $at = [array]::FindIndex([string[]]$tip, [Predicate[string]]{ param($t) $t -match '^Stored \d{4}-\d{2}-\d{2}$' })
  Check ($at -ge 0) "a vault item's tooltip says the day it was stored ($(@($tip | Select-Object -Last 3) -join ' | '))"
  $name = [array]::IndexOf([string[]]$tip, $a)
  Check ($name -ge 0 -and $at -gt $name) "under what the item says of itself (the name at $name, the day at $at)"
  Check ($null -ne (ById 'Tip_Stored')) "in a section of its own"
  [Fast]::SetCursorPos([int]$script:r.X + 40, [int]$script:r.Y + 40) | Out-Null; Start-Sleep -Milliseconds 300
}

Case 'vault-stored-sort.sort-picked-again-turns-the-folder-round' -Tags vault, sort {
  InSmall
  # Sorted by Name, the two charms lie side by side from the folder's first cell, one before the other; the same
  # order picked again turns the folder round - the other first, on the cell the one lay on. Which way round the
  # first pick leaves them is the order remembered's affair (by Name already, it turns it); that the second pick
  # swaps them is the sheet drawn again.
  SortBy 'Name'
  Check ($null -ne (ByName 'Sorted by Name.')) "Sort by Name: the strip says so"
  $wasA = X $a; $wasB = X $b
  Check ($wasA -ne $wasB) "the two charms lie side by side (Garnet at $wasB, Small at $wasA)"
  SortBy 'Name'
  Check ([bool](WaitUntil { if ((X $a) -eq $wasB -and (X $b) -eq $wasA) { 'turned' } } "the two swapped" 3000)) "Name picked again: the folder is turned round, each on the other's cell (Garnet at $(X $b), Small at $(X $a))"
}
