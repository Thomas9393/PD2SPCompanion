# The Items page's list of pages, scrolled (beside a small grid the section was as tall as the
# list, a lot of gap): that beside the inventory's grid the list is shorter than its rows and
# scrolls the rest; that its bar stands beside the locks, not over them; that the row of the page shown is in sight
# however far down the list it is; and that something carried to the list's foot scrolls it.
#
# On the pad's PhySpearZon: thirteen rows - the inventory, the cube, the personal stash, nine shared pages and the
# materials.

function List { ById 'Pane_PagesScroll' }
function Scrolled { $p = $null; if ((List).TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$p)) { $p.Current } }
# A row whether or not it is in sight, and whether it is: by where it lies against the list's frame.
function RowAnywhere([string]$name) { @((All (Cond $AE::AutomationIdProperty 'PageRow')) | Where-Object { $_.Current.Name -eq $name })[0] }
function Seen([string]$name) { InPagesList (RowAnywhere $name) }

Case 'pages-scroll.list-is-held-to-the-grid-and-scrolls-the-rest' -Tags pages-list, items, layout {
  Pane 0 'Inventory'
  $list = (List).Current.BoundingRectangle
  $row = (RowAnywhere 'Inventory').Current.BoundingRectangle
  $all = 13 * ($row.Height + 1)
  Check ($list.Height -lt $all - 20 -and $list.Height -ge 5 * $row.Height) "beside the inventory's grid the list is shorter than its thirteen rows, and six rows at the least ($([int]$list.Height) for $([int]$all))"
  Check ((Scrolled).VerticallyScrollable) "and scrolls the rest"
  Check ((Seen 'Inventory') -and -not (Seen 'Materials')) "the first rows in sight, the last not"

  # Its bar beside the locks, in room of its own.
  $lock = (PageLock 'Shared Page 1').Current.BoundingRectangle
  $bar = @((List).FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::ScrollBar)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Height -gt $_.Current.BoundingRectangle.Width })[0]
  Check ($null -ne $bar -and $bar.Current.BoundingRectangle.X -ge $lock.X + $lock.Width - 1) "the bar stands to the right of the locks, not over them (bar at $([int]$bar.Current.BoundingRectangle.X), the lock ends at $([int]($lock.X + $lock.Width)))"

  # Hard by the locks' column (no gap between the lock column and the
  # scrollbar; a bar's own 8 of room stood between them). The column is 21 wide, a rule of 1 at its left, and a
  # lock stands in its middle: the column ends 10 past the lock's middle - and there the bar begins, 10 wide, up
  # to the list's frame of 1.
  $unit = Unit
  $b = $bar.Current.BoundingRectangle
  $ends = $lock.X + $lock.Width / 2 + 10 * $unit
  Check ([math]::Abs($b.X - $ends) -le 1.5) "the bar begins where the locks' column ends: no gap ($([math]::Round($b.X - $ends, 1)) px off)"
  Check ([math]::Abs($b.Width - 10 * $unit) -le 1.5) "the bar is 10 wide, its room at its left gone ($([math]::Round($b.Width / $unit, 1)))"
  Check ([math]::Abs(($list.X + $list.Width) - ($b.X + $b.Width)) -le 1.5) "and reaches the list's own right edge ($([math]::Round(($list.X + $list.Width) - ($b.X + $b.Width), 1)) px off)"

  # A page far down shown: its row is in sight - and the first one again, come back to.
  Pane 0 'Materials'
  Check (Seen 'Materials') "the materials shown: their row is in sight"
  Pane 0 'Inventory'
  Check ([bool](WaitUntil { if (Seen 'Inventory') { 'seen' } } "the inventory's row in sight" 3000)) "the inventory shown again: its row is in sight"
}

Case 'pages-scroll.carried-to-the-lists-foot-it-scrolls' -Tags pages-list, items, drag -Changes {
  Pane 0 'Inventory'
  $rune = WaitUntil { Tile 'Hel Rune' } "a rune in the inventory"
  $b = $rune.Current.BoundingRectangle
  $from = @([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2))
  $l = (List).Current.BoundingRectangle
  $foot = @([int]($l.X + $l.Width / 3), [int]($l.Y + $l.Height - 8))
  $was = (Scrolled).VerticalScrollPercent

  Front; [Fast]::SetCursorPos($from[0], $from[1]) | Out-Null; Start-Sleep -Milliseconds 100
  [Fast]::mouse_event(2, 0, 0, 0, 0); Start-Sleep -Milliseconds 80
  try {
    for ($i = 1; $i -le 14; $i++) { [Fast]::SetCursorPos([int]($from[0] + ($foot[0] - $from[0]) * $i / 14), [int]($from[1] + ($foot[1] - $from[1]) * $i / 14)) | Out-Null; Start-Sleep -Milliseconds 20 }
    # Held at the foot, the hand not quite still: each move scrolls a little.
    for ($i = 0; $i -lt 16; $i++) { [Fast]::SetCursorPos($foot[0] + ($i % 2), $foot[1]) | Out-Null; Start-Sleep -Milliseconds 40 }
    $now = (Scrolled).VerticalScrollPercent
    Check ($now -gt $was + 5) "a rune carried to the list's foot scrolls it ($([int]$was)% -> $([int]$now)%)"
    Check (Seen 'Materials') "and the last rows come into sight under it"
  }
  finally {
    # Put back by Esc, the button still down: let go of over a row, it would go to that row's page.
    [Fast]::keybd_event(0x1B, 0, 0, 0); [Fast]::keybd_event(0x1B, 0, 2, 0); Start-Sleep -Milliseconds 300
    [Fast]::mouse_event(4, 0, 0, 0, 0); Start-Sleep -Milliseconds 400
  }
  Pane 0 'Inventory'
  Check ($null -ne (Tile 'Hel Rune')) "put back by Esc, the rune is where it was"
}
