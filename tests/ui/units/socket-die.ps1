# The socket count's die, as only a window shows it: that Shift held on a click of the die
# reaches it - the page watches for the key from above the button, which marks its own press handled - and rolls
# one to the most the base takes, which the die's own range never gives.
#
# What the die says and rolls, that the roll is the item's at once, and the count typed into the box is proven
# without a window: tests/PD2SPCompanion.Shell.Tests/Edit/SocketCountTests.
#
# The fixture is a copy of GoldFind (two Blades of Ali Baba, which roll 2-3 sockets), put in the pad
# before the program starts and loaded from the landing.

$script:GoldFind = "$script:Corpus\ui-pad\fixtures\GoldFind.d2s"

Case 'socket-die.shift-click-rolls-the-whole-range' -Tags sockets, edit -Start Home -Before {
  Copy-Item $script:GoldFind "$pad\saves\GoldFind.d2s" -Force
} {
  WaitUntil { ByName 'Open' } "the landing" | Out-Null
  ClickEl (WaitUntil { ByName 'GoldFind' } "the GoldFind card") "GoldFind"
  Start-Sleep -Milliseconds 300
  ClickEl (ByName 'Open') "Load"
  WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "character loaded" | Out-Null
  ClickEl (ById 'SideBar_Inventory') "Items"
  ClickEl (WaitUntil { ById 'Bookmark_Edit' } "Edit bookmark") "Edit"

  ClickUntil { ClickEl (WaitUntil { Tile 'Blade of Ali Baba' } "a blade") "a blade" } { OnEdit 'Blade of Ali Baba' } "a blade selected" 800 | Out-Null
  $die = WaitUntil { $d = ById 'Sockets_Die'; if ($d -and $d.Current.HelpText) { $d } } "the die, with its range"
  Check ($die.Current.HelpText -match '^Reroll within 2-3\s+Shift \+ LMB: within 1-3$') "Blade of Ali Baba: the die says 2-3, and Shift 1-3 by its numbers ('$($die.Current.HelpText -replace '\s+', ' / ')')"

  # Shift on the die: one to the most the base takes - a one, which the blade's own range never gives.
  $box = ById 'Sockets_Count'
  $wide = @{}
  for ($i = 0; $i -lt 60 -and -not $wide['1']; $i++) {
    Front; $b = $die.Current.BoundingRectangle
    [Fast]::keybd_event(0x10, 0, 0, 0); [Fast]::Click([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)); [Fast]::keybd_event(0x10, 0, 2, 0)
    Start-Sleep -Milliseconds 200
    $wide[$box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value] = $true
  }
  Check ($wide['1'] -and -not ($wide.Keys | Where-Object { $_ -notin '1', '2', '3' })) "Shift on the die reaches 1, inside 1-3 ($(($wide.Keys | Sort-Object) -join ', '))"
}
