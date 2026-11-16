# Where an item's tooltip opens when it fits neither under the item nor over it, and what it does when it is taller
# than the screen: under the item for every item that fits, the game's own way; beside the
# item, with the screen's whole height, for the one that does not; and cut to the screen with a scrollbar where even
# that is not enough - moved by the [ and ] keys, which the strip lists only while the bar shows.
#
# All of it is the window's to show: where the tooltip is drawn, its bar, and the keys reaching it. Only who has
# the [ and ] keys - a tooltip, and only while one that scrolls is up - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Tips/TipScrollTests.
#
# A small screen is pretended on the real one (PD2SPC_TIP_ROOM "side,most": the room under and over an item never
# more than side, the screen's height most), read as the program starts: such a case sets it before its start and
# takes it away as it ends. Nothing is edited.

function Key([int]$vk) { [Fast]::keybd_event($vk, 0, 0, 0); Start-Sleep -Milliseconds 40; [Fast]::keybd_event($vk, 0, 2, 0); Start-Sleep -Milliseconds 400 }
function Tip { @((All (Cond $AE::ControlTypeProperty $CT::ToolTip)))[0] }
function FirstTipTexts { $t = Tip; if ($t) { @($t.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) } }
function Strip {
  $t = @(FirstTipTexts); $keys = 'RMB', 'Alt', 'Ctrl', 'Tab', '[ ]'
  for ($i = 0; $i -lt $t.Count - 1; $i++) { if ($keys -contains $t[$i]) { "$($t[$i]) - $($t[$i + 1])" } }
}
# How far down the tooltip's own scroll area is, in hundredths; -1 where it does not scroll.
function Scrolled {
  $area = $script:win.FindFirst($TS::Descendants, (Cond $AE::AutomationIdProperty 'Tip_Scroll'))
  $p = $null
  if ($area -and $area.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$p) -and $p.Current.VerticallyScrollable) { [int]$p.Current.VerticalScrollPercent } else { -1 }
}

Case 'tip-room.opens-under-the-item-where-it-fits' -Tags tooltip, items {
  $tile = WaitUntil { Tile 'Stone Barb' } "the worn Stone Barb"
  Hover $tile
  $t = (Tip).Current.BoundingRectangle; $i = $tile.Current.BoundingRectangle
  Check ($t.Y -ge $i.Bottom -and (Scrolled) -eq -1 -and -not (@(Strip) -like '`[ `]*')) "room enough: under the item (tooltip top $([int]$t.Y), item bottom $([int]$i.Bottom)), no bar, no keys for one"
}

Case 'tip-room.opens-beside-the-item-where-it-fits-neither-under-nor-over' -Tags tooltip, items -Before {
  $env:PD2SPC_TIP_ROOM = '150,4000'
} {
  try {
    $tile = WaitUntil { Tile 'Stone Barb' } "the worn Stone Barb"
    Hover $tile
    $t = (Tip).Current.BoundingRectangle; $i = $tile.Current.BoundingRectangle
    Check ($t.X -ge $i.Right -or $t.Right -le $i.X) "no room under or over: beside the item (tooltip $([int]$t.X)..$([int]$t.Right), item $([int]$i.X)..$([int]$i.Right))"
    Check ($t.Bottom -le $i.Y -or $t.Y -ge $i.Bottom -or $t.X -ge $i.Right -or $t.Right -le $i.X) "and never over the item"
    Check ((Scrolled) -eq -1 -and -not (@(Strip) -like '`[ `]*')) "whole, with no bar and no keys for one: '$(@(Strip) -join '; ')'"
  }
  finally { Remove-Item Env:PD2SPC_TIP_ROOM -ErrorAction SilentlyContinue }
}

Case 'tip-room.cut-to-the-screen-it-scrolls-by-the-bracket-keys' -Tags tooltip, items, keys -Before {
  $env:PD2SPC_TIP_ROOM = '150,300'
} {
  try {
    $tile = WaitUntil { Tile 'Stone Barb' } "the worn Stone Barb"
    Hover $tile
    $t = (Tip).Current.BoundingRectangle
    Check ($t.Height -le 300 * 1.25 + 2) "a screen 300 high: the tooltip is cut to it ($([int]$t.Height) px at this display's scale)"
    Check ((Scrolled) -eq 0 -and @(Strip) -contains '[ ] - Scroll') "it scrolls, from the top, and the strip says how: '$(@(Strip) -join '; ')'"
    Check (@(Strip) -contains 'RMB - Menu') "the strip's own line stays under what scrolls"
    Key 0xDD; Key 0xDD
    $down = Scrolled
    Check ($down -gt 0) "] twice: down ($down%)"
    Key 0xDB
    Check ((Scrolled) -lt $down) "[ once: up again ($(Scrolled)%)"
    for ($n = 0; $n -lt 40; $n++) { [Fast]::keybd_event(0xDD, 0, 0, 0); Start-Sleep -Milliseconds 25 }
    [Fast]::keybd_event(0xDD, 0, 2, 0); Start-Sleep -Milliseconds 400
    Check ((Scrolled) -eq 100) "] held: to the foot ($(Scrolled)%)"
  }
  finally { Remove-Item Env:PD2SPC_TIP_ROOM -ErrorAction SilentlyContinue }
}
