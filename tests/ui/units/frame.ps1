# The book's frame, as only a window shows it: the two pages lie over the pillar - a slim gutter of it shows
# between them, the same width down the pages' whole height, and the pillar itself runs on above and below them; it
# stands in the middle of the gutter, and the pages are alike in width. Measured in the editor's units, so at any
# scaling: a gutter of 12, a pillar of 38. UI Automation gives a rectangle in whole pixels, its left edge and its
# width each cut down to one - so a gap between two of them reads up to two pixels wider than it is drawn (15 at
# 125% read 16 and 17), and what is asked here is asked that loosely.
#
# And the pillar's art has its middle in the pillar's (it stood a pixel left, and the eye
# focuses there, where an asymmetry is easy to catch): read off the screen above the pages, where the pillar shows whole
# between its two silver edges - the art is its own mirror image about its middle, so the picture is least unlike
# its mirror image about the axis the art is centred on, and that axis is to be the middle between the edges.
#
# And the two pages' gold frames face each other: the left page's is the right page's in a
# mirror, so the two edges at the spine are one colour, the two outer edges one colour, and the outer the brighter -
# the spine lies inside, the book's outer edges catch the light.
#
# Nothing is pressed.
Add-Type -AssemblyName System.Drawing

# The gold of a frame's upright edge near a point of the screen: of the pixels a few either side, the most golden
# (the most red over blue - the stone and the pages beside it are grey), as red, green, blue.
function Gold([int]$x, [int]$y) {
  $bmp = New-Object System.Drawing.Bitmap 13, 1
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($x - 6, $y, 0, 0, $bmp.Size); $g.Dispose()
  $best = $null
  for ($i = 0; $i -lt 13; $i++) { $c = $bmp.GetPixel($i, 0); if (-not $best -or ($c.R - $c.B) -gt ($best.R - $best.B)) { $best = $c } }
  $bmp.Dispose()
  @([int]$best.R, [int]$best.G, [int]$best.B)
}
function Alike($a, $b) { [math]::Abs($a[0] - $b[0]) -le 8 -and [math]::Abs($a[1] - $b[1]) -le 8 -and [math]::Abs($a[2] - $b[2]) -le 8 }

# The brightness of a strip of the screen, a column of numbers to each pixel across.
function Brightness([int]$x, [int]$y, [int]$w, [int]$h) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size); $g.Dispose()
  $cols = for ($i = 0; $i -lt $w; $i++) { , @(for ($j = 0; $j -lt $h; $j++) { $c = $bmp.GetPixel($i, $j); $c.R + $c.G + $c.B }) }
  $bmp.Dispose()
  , $cols
}
# How unlike its mirror image the strip is between two columns, about an axis given doubled (so that it may lie
# between two pixels): the mean difference of each pixel and the one across.
function Unlike($cols, [int]$from, [int]$to, [int]$axis2) {
  $sum = 0.0; $n = 0
  for ($i = $from; $i -le $to; $i++) {
    $m = $axis2 - $i
    if ($m -le $i -or $m -gt $to) { continue }
    for ($j = 0; $j -lt $cols[$i].Count; $j++) { $sum += [math]::Abs($cols[$i][$j] - $cols[$m][$j]); $n++ }
  }
  if ($n) { $sum / $n } else { 1e9 }
}
# The pillar above the pages: its two silver edges - the brightest column of its left third and of its right - and
# how unlike its mirror image the art between them is about their middle, and about a pixel either side of it.
function PillarArt {
  $pillar = (ById 'Frame_Pillar').Current.BoundingRectangle; $page = (ById 'Frame_PageLeft').Current.BoundingRectangle
  $w = [int]$pillar.Width + 8; $h = [int][math]::Min(40, $page.Y - $pillar.Y - 12)
  $cols = Brightness ([int]$pillar.X - 4) ([int]$pillar.Y + 6) $w $h
  $mean = @($cols | ForEach-Object { ($_ | Measure-Object -Average).Average })
  $third = [int]($w / 3)
  $l = 0; for ($i = 1; $i -lt $third; $i++) { if ($mean[$i] -gt $mean[$l]) { $l = $i } }
  $r = $w - $third; for ($i = $r + 1; $i -lt $w; $i++) { if ($mean[$i] -gt $mean[$r]) { $r = $i } }
  @{ Wide = $r - $l - 1
     Middle = Unlike $cols ($l + 1) ($r - 1) ($l + $r)
     Left = Unlike $cols ($l + 1) ($r - 1) ($l + $r - 2)
     Right = Unlike $cols ($l + 1) ($r - 1) ($l + $r + 2) }
}

Case 'frame.pages-lie-over-the-pillar-with-a-slim-gutter-between' -Tags window, layout, frame {
  $left = (WaitUntil { ById 'Frame_PageLeft' } "the left page's frame").Current.BoundingRectangle
  $right = (WaitUntil { ById 'Frame_PageRight' } "the right page's frame").Current.BoundingRectangle
  $pillar = (WaitUntil { ById 'Frame_Pillar' } "the pillar").Current.BoundingRectangle
  $unit = Unit

  $gutter = $right.X - ($left.X + $left.Width)
  Check ($gutter -ge 12 * $unit - 1 -and $gutter -le 12 * $unit + 2.5) "between the pages a gutter of 12 ($gutter px, a unit $unit)"
  Check ([math]::Abs($pillar.Width - 38 * $unit) -le 1.5) "the pillar is 38 wide ($([math]::Round($pillar.Width / $unit, 1)))"
  $middle = $left.X + $left.Width + $gutter / 2
  Check ([math]::Abs(($pillar.X + $pillar.Width / 2) - $middle) -le 2) "and stands in the gutter's middle: the pages lie over 13 of it each side"
  Check ($pillar.Y -lt $left.Y - 4 * $unit -and $pillar.Y + $pillar.Height -gt $left.Y + $left.Height + 4 * $unit) "it runs on above the pages and below them ($([int]$pillar.Y)..$([int]($pillar.Y + $pillar.Height)) about $([int]$left.Y)..$([int]($left.Y + $left.Height)))"
  Check ([math]::Abs($left.Width - $right.Width) -le 1.5 -and [math]::Abs($left.Y - $right.Y) -le 1 -and [math]::Abs($left.Height - $right.Height) -le 1) "the two pages are alike: $([int]$left.Width) and $([int]$right.Width) wide"

  # The frames face each other: read a third of the way down the pages, clear of the bookmarks and the ornaments.
  $y = [int]($left.Y + $left.Height * 0.3)
  $leftOuter = Gold ([int]$left.X + 1) $y; $leftInner = Gold ([int]($left.X + $left.Width) - 2) $y
  $rightInner = Gold ([int]$right.X + 1) $y; $rightOuter = Gold ([int]($right.X + $right.Width) - 2) $y
  Check (Alike $leftInner $rightInner) "the two pages' edges at the spine are one gold ($($leftInner -join ',') and $($rightInner -join ','))"
  Check (Alike $leftOuter $rightOuter) "and their outer edges one gold ($($leftOuter -join ',') and $($rightOuter -join ','))"
  Check (($leftOuter | Measure-Object -Sum).Sum -gt ($leftInner | Measure-Object -Sum).Sum + 100) "the outer the brighter: the spine lies inside"

  # The art's middle is the pillar's, to the pixel.
  [Fast]::SetCursorPos([int]($script:r.X + 30), [int]($script:r.Y + $script:r.Height - 30)) | Out-Null; Start-Sleep -Milliseconds 300
  $art = PillarArt
  Check ($art.Middle -lt $art.Left / 2 -and $art.Middle -lt $art.Right / 2) "the pillar's art is centred in it: least unlike its mirror image about the pillar's middle ($([math]::Round($art.Middle, 1)); a pixel left $([math]::Round($art.Left, 1)), a pixel right $([math]::Round($art.Right, 1)); $($art.Wide) px between the silver edges)"

  # Nothing of it moves with the window's size.
  try {
    Size 1600 900
    Start-Sleep -Milliseconds 700
    $art = PillarArt
    Check ($art.Middle -lt $art.Left / 2 -and $art.Middle -lt $art.Right / 2) "and in a smaller window too ($([math]::Round($art.Middle, 1)); $([math]::Round($art.Left, 1)), $([math]::Round($art.Right, 1)))"
    $left = (ById 'Frame_PageLeft').Current.BoundingRectangle; $right = (ById 'Frame_PageRight').Current.BoundingRectangle
    $unit = Unit
    $gutter = $right.X - ($left.X + $left.Width)
    Check ($gutter -ge 12 * $unit - 1 -and $gutter -le 12 * $unit + 2.5 -and [math]::Abs($left.Width - $right.Width) -le 1.5) "in a smaller window the gutter is 12 still, the pages alike ($gutter px; $([int]$left.Width), $([int]$right.Width))"
  }
  finally {
    $script:show = 3
    try { Wide } catch { }
  }
}
