# The item's card on the Edit page, its height (dragged to its maximum at a smaller
# resolution and the app enlarged, it stayed a height and did not become a ratio; the grip is the same design as the
# vault's):
# that its grip draws the card out; that the height drawn is a share of the page - the window made smaller, the
# card is smaller with it, where a height in pixels stayed as it was; and that a double click gives it its own
# height again.
#
# On the pad's PhySpearZon: Stone Barb worn.

function Grip { ById 'Item_Grip' }
# How far the grip stands under the item's name: the card's height, less what is over the name.
function Drawn { (Grip).Current.BoundingRectangle.Y - (ById 'Item_Name').Current.BoundingRectangle.Y }

Case 'item-card.grip-draws-it-out-to-a-share-of-the-page' -Tags edit, layout, window {
  try {
    Size 1700 1200
    ClickEl (WaitUntil { Tile 'Stone Barb' } "the worn Stone Barb") "Stone Barb"
    $grip = WaitUntil { Grip } "the card's grip" 4000
    $own = Drawn
    $dots = @($grip.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Pane))).Count
    $b = $grip.Current.BoundingRectangle
    Check ($b.Height -le 14 -and $b.Width -gt 300) "the grip lies across under the card, the vault's edge ($([int]$b.Width) x $([int]$b.Height))"

    # Drawn out by 90: under the most it may have, so only the share can change it.
    $to = @([int]($b.X + $b.Width / 2 - $script:r.X), [int]($b.Y + $b.Height / 2 - $script:r.Y + 90))
    DragTo $grip $to
    $drawn = WaitUntil { $d = Drawn; if ($d -gt $own + 60) { $d } } "the card drawn out" 3000
    Check ($drawn -gt $own + 60) "dragged down, the card is taller ($([int]$own) -> $([int]$drawn))"

    # The window lower by an eighth: the card is lower with it, and taller than its own still. In pixels it
    # stayed as tall as it was drawn. (Lower by a quarter, the same share of the page is under the card's own
    # height, and it stands at that.)
    Size 1700 1050
    Start-Sleep -Milliseconds 500
    $small = Drawn
    Check ($small -lt $drawn - 15 -and $small -gt $own + 4) "the window made lower, the card is lower with it - a share of the page ($([int]$drawn) -> $([int]$small), its own $([int]$own))"
    Size 1700 900
    Start-Sleep -Milliseconds 500
    Check ([math]::Abs((Drawn) - $own) -le 3) "lower still, the share is under the card's own height: it stands at that ($([int](Drawn)))"
    Size 1700 1200
    Start-Sleep -Milliseconds 500
    $again = Drawn
    Check ([math]::Abs($again - $drawn) -le 3) "and as it was, the window as it was ($([int]$again) for $([int]$drawn))"

    # A double click on the grip: the card's own height.
    $g = (Grip).Current.BoundingRectangle
    Front; [Fast]::SetCursorPos([int]($g.X + $g.Width / 2), [int]($g.Y + $g.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 150
    [Fast]::mouse_event(2, 0, 0, 0, 0); [Fast]::mouse_event(4, 0, 0, 0, 0); Start-Sleep -Milliseconds 90
    [Fast]::mouse_event(2, 0, 0, 0, 0); [Fast]::mouse_event(4, 0, 0, 0, 0)
    Check ([bool](WaitUntil { if ([math]::Abs((Drawn) - $own) -le 3) { 'own' } } "the card's own height" 3000)) "a double click on the grip: its own height again ($([int](Drawn)) for $([int]$own))"
  }
  finally {
    $script:show = 3
    try { Wide } catch { }
  }
}
