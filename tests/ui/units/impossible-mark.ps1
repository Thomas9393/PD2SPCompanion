# The alert of an item the game could not hold, on a grid tile (as a worn slot marks it),
# as only a window shows it: that it is drawn, in
# the danger red at the tile's bottom right corner, and that the tile is drawn again without it once the item is
# put right. The color is read off the screen.
#
# Which items wear it, and that the tile reads the item again rather than a kept answer, is proven without a
# window: tests/PD2SPCompanion.Shell.Tests/Edit/BasicsTests.
#
# A Cap with six sockets - a cap takes two - is given to the pad's stash with the command line tool before the
# program starts, the stash cleared first, and a Harlequin Crest beside it.

# How much of a tile's bottom right corner is the alert's red (#CC2200) - a thin ring and a wash of it, so drawn
# darker than the color named: red well over green and blue. With the pointer off the tile.
function RedAt($tile) {
  [Fast]::SetCursorPos([int]($script:r.X + $script:r.Width * 0.75), [int]($script:r.Y + 60)) | Out-Null
  Start-Sleep -Milliseconds 500
  $b = $tile.Current.BoundingRectangle
  $bmp = New-Object System.Drawing.Bitmap 20, 20
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen([int]($b.X + $b.Width - 21), [int]($b.Y + $b.Height - 21), 0, 0, $bmp.Size)
  $red = 0
  for ($x = 0; $x -lt 20; $x++) { for ($y = 0; $y -lt 20; $y++) {
    $c = $bmp.GetPixel($x, $y)
    if ($c.R -gt 90 -and $c.G -lt 70 -and $c.B -lt 50 -and $c.R -gt 2 * $c.G) { $red++ }
  } }
  $g.Dispose(); $bmp.Dispose()
  $red
}
function CapAndCrest {
  $save = "$pad\saves\PhySpearZon.d2s"
  $kept = "$pad\impossible-mark-give"
  Remove-Item $kept -Recurse -Force -ErrorAction SilentlyContinue
  & $script:Cli clear $save | Out-Null
  & $script:Cli give $save --base 'cap:6' --unique 'Harlequin Crest' --backup $kept | Out-Null
}

Case 'impossible-mark.alert-is-drawn-at-the-tiles-corner' -Tags items, marks -Before { CapAndCrest } {
  Pane 0 'Personal Stash'
  $cap = WaitUntil { Tile 'Cap' } "the cap in the stash"
  $crest = WaitUntil { Tile 'Harlequin Crest' } "the helm beside it"
  $onCap = RedAt $cap; $onCrest = RedAt $crest
  Check ($onCap -ge 12) "a cap with six sockets: the alert at its tile's corner ($onCap red pixels)"
  Check ($onCrest -eq 0) "the helm beside it, a unique as the game makes it: none ($onCrest)"

  # And its tooltip says why, at its foot (the mark said nothing).
  Hover $cap
  $why = @(TipTexts | Where-Object { $_ -match '^6 sockets' })
  Check ($why.Count -eq 1) "the cap's tooltip says why: '$($why -join ' | ')'"
  Hover $crest
  Check (@(TipTexts | Where-Object { $_ -match 'sockets, and the most' }).Count -eq 0) "the helm's says nothing of it"
}

Case 'impossible-mark.alert-goes-with-its-cause' -Tags items, marks, edit -Before { CapAndCrest } {
  Pane 0 'Personal Stash'
  WaitUntil { Tile 'Cap' } "the cap in the stash" | Out-Null
  $before = RedAt (Tile 'Cap')
  Check ($before -ge 12) "start: the cap wears the alert ($before red pixels)"

  ClickUntil { ClickEl (Tile 'Cap') "the cap" } { OnEdit 'Cap' } "the cap on the Edit page" | Out-Null
  $remove = WaitUntil { $b = ByTip 'Remove socket' -Prefix; if ($b -and $b.Current.IsEnabled) { $b } } "Remove socket"
  DragTo $remove $remove -Modifier Shift
  WaitUntil { -not (ByTip 'Remove socket' -Prefix).Current.IsEnabled } "no sockets" 4000 | Out-Null
  Kept "the sockets taken off"
  $after = RedAt (Tile 'Cap')
  Check ($after -eq 0) "its sockets taken off: the alert is gone ($after red pixels)"
}
