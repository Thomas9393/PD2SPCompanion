# An item the character cannot use stays red when another is moved (of two same items only
# one showed not equippable, changing with whichever was picked up last). A tile's red is a part built
# only where it shows, and bound through its presenter's own DataContext it stopped listening once it went: the tile
# left in place lost its red at the next move. The view's own fault, so the window's alone to show: the color is
# read off the screen. Nothing of it is a view model's deciding - no unit test stands beside this case.
#
# Two Blades of Ali Baba on a Barbarian who meets neither's strength nor dexterity (the fixture),
# put in the pad before the program starts and loaded from the landing.

$script:GoldFind = "$script:Corpus\ui-pad\fixtures\GoldFind.d2s"

# The blades, left to right.
function Blades { @(Tiles 'Blade of Ali Baba') | Sort-Object { $_.Current.BoundingRectangle.X } }
# Whether a tile's wash is the unusable red: its top left corner, clear of the art and the holes. Red well over
# green, where the unique's gold wash has them near each other - dimmed beside a selection or not.
function IsRed($tile) {
  $b = $tile.Current.BoundingRectangle
  $bmp = New-Object System.Drawing.Bitmap 1, 1
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen([int]($b.X + 5), [int]($b.Y + 5), 0, 0, $bmp.Size)
  $c = $bmp.GetPixel(0, 0); $g.Dispose(); $bmp.Dispose()
  $c.R -gt 60 -and $c.R -gt 2.5 * $c.G
}
function BothRed([string]$when) {
  Front
  [Fast]::SetCursorPos([int]$script:r.X + 40, [int]$script:r.Y + 300) | Out-Null   # off the tiles: no hover, no tooltip
  $ok = $false
  try { WaitUntil { $t = Blades; $t.Count -eq 2 -and (IsRed $t[0]) -and (IsRed $t[1]) } "both red $when" 3000 | Out-Null; $ok = $true } catch { }
  $t = Blades
  Check $ok "$when`: both blades red (left $(IsRed $t[0]), right $(IsRed $t[1]))"
}
# One blade dragged to the cell columns right of the other's place, same row.
function DragBeside($tile, $beside, [int]$columns) {
  $b = $beside.Current.BoundingRectangle
  DragTo $tile @([int]($b.X + $b.Width * ($columns + 0.5) - $script:r.X), [int]($b.Y + $b.Height / 2 - $script:r.Y)) -Hold 800
}

Case 'red-mark-stays.both-stay-red-as-each-is-moved' -Tags items, marks -Start Home -Before {
  Copy-Item $script:GoldFind "$pad\saves\GoldFind.d2s" -Force
} {
  WaitUntil { ByName 'Open' } "the landing" | Out-Null
  ClickEl (WaitUntil { ByName 'GoldFind' } "the GoldFind card") "GoldFind"
  Start-Sleep -Milliseconds 300
  ClickEl (ByName 'Open') "Load"
  WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "character loaded" | Out-Null
  ClickEl (ById 'SideBar_Inventory') "Items"
  WaitUntil { (Blades).Count -eq 2 } "the two blades" | Out-Null
  BothRed "loaded"

  # The left one moved: the one left in place lost its red.
  $t = Blades
  $x = $t[0].Current.BoundingRectangle.X
  ClickUntil { $t = Blades; DragBeside $t[0] $t[1] 2 } { (Blades)[0].Current.BoundingRectangle.X -ne $x } "the left blade moved" 1500 | Out-Null
  BothRed "one moved"

  # And the other: what was the right one, now on the left.
  $t = Blades
  $x = $t[0].Current.BoundingRectangle.X
  ClickUntil { $t = Blades; DragBeside $t[0] $t[1] 2 } { (Blades)[0].Current.BoundingRectangle.X -ne $x } "the other blade moved" 1500 | Out-Null
  BothRed "the other moved"
}
