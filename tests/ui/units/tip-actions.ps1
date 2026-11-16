# An item's tooltip where the item can be acted on, as only a window shows it (after
# Diablo 4's): the strip drawn at the tooltip's foot, the comparison in a box of its own beside the tooltip, and
# the keys reaching it while the pointer is over an item - Tab opens and shuts it, once a press however long it is
# held; Ctrl alone turns it to the mercenary's slot; Alt alone to the next slot, and leaves the window be while
# no comparison is in sight. (A plain right click
# opens the item's menu - units/right-click.ps1 - and the strip names the menu.)
#
# What the strip says of each item, what the comparison shows - a shield aimed at the shield and a weapon at the
# weapon, both pieces for a weapon held in both hands, "Empty", the mercenary's piece - and what the menu's rows
# say ("Replace" and what it names, "Take off (2)", "Withdraw") are proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Tips/TipStripTests and TipComparisonTests.
#
# On the pad's PhySpearZon: Laying of Hands and a Veil of Steel worn, a second Veil of Steel in the inventory,
# Naj's Circlet on the mercenary, a Nagelring on one finger and the other bare.

function Key([int]$vk, [switch]$Down, [switch]$Up) {
  if ($Down -or -not $Up) { [Fast]::keybd_event($vk, 0, 0, 0) }
  if ($Up -or -not $Down) { Start-Sleep -Milliseconds 40; [Fast]::keybd_event($vk, 0, 2, 0) }
  Start-Sleep -Milliseconds 500
}
# The tooltip's own texts - it stands in the main window's tree, as a tooltip - and the comparison's, a popup of
# its own beside it, found by its caption. A tooltip coming up or going as the tree is read is gone from under the
# reader ("UI that is no longer available"): read again, up to three times.
function TipAndCompareTexts {
  for ($try = 0; $try -lt 3; $try++) {
    try {
      $texts = @()
      foreach ($tip in (All (Cond $AE::ControlTypeProperty $CT::ToolTip))) {
        foreach ($t in $tip.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text))) { if ($t.Current.Name) { $texts += $t.Current.Name } }
      }
      $texts += @(CompareTexts)
      return $texts
    } catch { Start-Sleep -Milliseconds 250 }
  }
  @()
}
# What the comparison's popup says, its caption first; nothing while it is shut.
function CompareTexts {
  $caption = $script:win.FindFirst($TS::Descendants, (Cond $AE::AutomationIdProperty 'Tip_Compared'))
  if (-not $caption) { return }
  $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
  $box = $walker.GetParent($walker.GetParent($caption))
  foreach ($t in $box.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text))) { if ($t.Current.Name) { $t.Current.Name } }
}
# The strip's lines as "key - what", read off the tooltip's texts in order: a key's text, then what it does.
function Strip {
  $t = @(TipAndCompareTexts); $keys = 'RMB', 'Alt', 'Ctrl', 'Tab'
  for ($i = 0; $i -lt $t.Count - 1; $i++) { if ($keys -contains $t[$i]) { "$($t[$i]) - $($t[$i + 1])" } }
}
function WhereIs([string]$name) {
  $section = ''
  foreach ($line in @((& $script:Cli dump "$pad\saves\PhySpearZon.d2s" 2>&1 | Out-String) -split "`n")) {
    if ($line -match '^\s*== (.+?) \(\d+\)') { $section = $Matches[1]; continue }
    if ($line -match '^\s*(?:#\d+)?\s+\[\w+\s*\]\s+(.*?)\s{2,}\(' -and $Matches[1].Trim() -eq $name) { return $section }
  }
}
function Until([scriptblock]$cond, [int]$ms = 8000) { try { WaitUntil $cond "the save" $ms | Out-Null; $true } catch { $false } }
# The comparison shut again where a case leaves it open - the pointer still over its item: Tab.
function ShutComparison {
  if (@(TipAndCompareTexts) -like 'Equipped*' -or @(TipAndCompareTexts) -like 'Mercenary*') { Key 0x09 }
}

Case 'tip-actions.strip-is-drawn-at-the-tooltips-foot' -Tags tooltip, items {
  Hover (WaitUntil { Tile 'Laying of Hands' } "worn gloves")
  Check ((@(Strip) -join '; ') -eq 'RMB - Menu') "a worn item's strip: '$(@(Strip) -join '; ')'"
  $tiered = @(TipAndCompareTexts | Where-Object { $_ -like '*(2 Items)' })
  $bare = @(TipAndCompareTexts | Where-Object { $_ -match '\D2$' })
  Check ($tiered.Count -gt 0 -and $bare.Count -eq 0) "a set piece's bonus line names its tier in words: $($tiered[0])"

  # The helm in the grid, another worn: the comparison shut - the menu, and the key that opens the comparison.
  Hover (WaitUntil { Tile 'Veil of Steel' 1 } "the helm in the grid")
  $strip = @(Strip) -join '; '
  Check ($strip -eq 'RMB - Menu; Tab - Compare') "the comparison shut: '$strip'"
  Check (-not (@(TipAndCompareTexts) -like 'Equipped*')) "the comparison is shut to begin with"
}

Case 'tip-actions.tab-opens-the-comparison-beside-the-tooltip' -Tags tooltip, items, keys {
  Hover (WaitUntil { Tile 'Veil of Steel' 1 } "the helm in the grid")
  # Tab opens it: the box beside, the strip - still in the item's tooltip - with the keys alone.
  Key 0x09
  $strip = @(Strip) -join '; '
  Check (@(TipAndCompareTexts) -contains 'Equipped - Helm' -and $strip -eq 'RMB - Menu; Ctrl - Mercenary; Tab - Hide comparison') "Tab: 'Equipped - Helm', and the strip with the comparison's keys: '$strip'"
  Check (@(@(CompareTexts) -like 'Veil of Steel*').Count -ge 1) "the worn helm in the box beside the one in the grid"

  Key 0x09
  Check (-not (@(TipAndCompareTexts) -like 'Equipped*') -and @(Strip) -contains 'Tab - Compare') "Tab again: the box gone, 'Compare' offered"
}

Case 'tip-actions.ctrl-alone-turns-it-to-the-mercenary' -Tags tooltip, items, keys {
  Hover (WaitUntil { Tile 'Veil of Steel' 1 } "the helm in the grid")
  Key 0x09
  # Ctrl alone turns it to the mercenary's, and it stays there once the key is let go.
  Key 0x11
  $strip = @(Strip) -join '; '
  Check (@(TipAndCompareTexts) -contains 'Mercenary - Helm' -and @(TipAndCompareTexts) -contains "Naj's Circlet" -and $strip -eq "RMB - Menu; Ctrl - Character; Tab - Hide comparison") "Ctrl tapped: the mercenary's piece, and it stays: '$strip'"
  Key 0x11
  Check (@(TipAndCompareTexts) -contains 'Equipped - Helm') "Ctrl again: the character's"
  ShutComparison
}

Case 'tip-actions.tab-held-turns-it-over-once' -Tags tooltip, items, keys {
  Hover (WaitUntil { Tile 'Veil of Steel' 1 } "the helm in the grid")
  # Tab held: the key repeats, and the comparison turns over once.
  for ($i = 0; $i -lt 6; $i++) { [Fast]::keybd_event(0x09, 0, 0, 0); Start-Sleep -Milliseconds 60 }
  Start-Sleep -Milliseconds 300
  $during = [bool](@(TipAndCompareTexts) -like 'Equipped*')
  [Fast]::keybd_event(0x09, 0, 2, 0); Start-Sleep -Milliseconds 400
  Check ($during -and [bool](@(TipAndCompareTexts) -like 'Equipped*')) "Tab held through six repeats: open once, and open still when let go"
  Key 0x09
  Check (-not (@(TipAndCompareTexts) -like 'Equipped*')) "a press after it: shut again"
}

Case 'tip-actions.alt-turns-the-slot' -Tags tooltip, items, keys, right-click -Changes {
  # A ring, which two slots take; both fingers bare. Shut: Alt turns nothing. Open: "Empty", and Alt alone the next slot.
  MenuOf (WaitUntil { Tile 'Nagelring' } "the worn ring") 'the worn ring'
  MenuPick 'Take off'
  Hover (WaitUntil { Tile 'Nagelring' } "the ring in the grid")
  $strip = @(Strip) -join '; '
  Check ($strip -eq 'RMB - Menu; Tab - Compare') "a ring in the grid, both fingers bare: '$strip'"
  Key 0x12
  Check ($null -ne (ById 'SideBar_Inventory') -and @(Strip).Count -eq 2) "Alt pressed and let go alone, the comparison shut: the tooltip and the window as they were"
  Key 0x09
  $strip = @(Strip) -join '; '
  Check (@(TipAndCompareTexts) -contains 'Equipped - Ring' -and @(TipAndCompareTexts) -contains 'Empty' -and $strip -eq 'RMB - Menu; Alt - Next slot; Tab - Hide comparison') "open over a bare finger: 'Equipped - Ring', 'Empty': '$strip'"
  Key 0x12
  Check (@(TipAndCompareTexts) -contains 'Equipped - Ring') "Alt tapped, the comparison open: the next slot, the box still up"
  ShutComparison

  # The menu's two rows put it on either finger.
  MenuOf (Tile 'Nagelring') 'the ring'
  Check ((MenuWords) -like 'Equip; Equip in other slot; *') "its menu offers both fingers ('$(MenuWords)')"
  MenuPick 'Equip'
  Until { (WhereIs 'Nagelring') -like 'equipped: *Finger' } | Out-Null
  $first = WhereIs 'Nagelring'
  MenuOf (WaitUntil { Tile 'Nagelring' } "the worn ring") 'the worn ring'
  MenuPick 'Take off'
  Until { (WhereIs 'Nagelring') -notlike 'equipped*' } | Out-Null
  MenuOf (WaitUntil { Tile 'Nagelring' } "the ring in the grid again") 'the ring'
  MenuPick 'Equip in other slot'
  Check ((Until { $w = WhereIs 'Nagelring'; $w -like 'equipped: *Finger' -and $w -ne $first })) "'Equip in other slot' puts it on the other finger: $first before, $(WhereIs 'Nagelring') after"
}

Case 'tip-actions.vault-card-names-the-menu' -Tags tooltip, vault {
  ClickEl (ById 'Bookmark_Vault_Right') "vault (right)"
  Hover (WaitUntil { VaultTile 'Tome of Town Portal' } "the tome's card")
  $strip = @(Strip) -join '; '
  Check ($strip -eq 'RMB - Menu') "a vault card nothing wears, beside the inventory: '$strip'"
}
