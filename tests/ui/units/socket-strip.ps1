# The Edit page's Sockets section as a row of sockets, as only a window shows it: that the sockets stand in a row under one line of tools, a click
# selecting one and the line under the row saying it; that the mouse drags a socket out of the row onto a cell of
# the inventory, and a gem from the inventory onto the section - the Edit page staying on its item the while; that
# a right click on a socket opens its menu; that a socket cut is pasted by the menu of a bare cell; that Esc and
# the right button call a carry off; and that the section is lit while a gem is carried over it.
#
# What the section and the pages decide - what the tools offer, a runeword less a rune, where a socket dragged out
# or pasted lands, a copy, each refusal - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Edit/SocketStripTests, UnsocketTests and SocketCopyTests.
#
# On the pad's PhySpearZon: Stone Barb worn, holding two Shael Runes, a Perfect Skull and a jewel; the inventory's
# second row's seventh and eighth cells bare.
Add-Type -AssemblyName System.Windows.Forms

function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }
function Cells { @((All (Cond $AE::AutomationIdProperty 'Socket_Cell')) | Where-Object { -not $_.Current.IsOffscreen }) }
function Row { (Cells | ForEach-Object { $_.Current.Name }) -join ' | ' }
function Caption { "$((ById 'Sockets_Caption').Current.Name) / $((ById 'Sockets_CaptionEffect').Current.Name)" }
function Says([string]$like, [int]$ms = 4000) { try { [bool](WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -like $like })[0] } "the strip: $like" $ms) } catch { $false } }
function Mid($el) { $b = $el.Current.BoundingRectangle; @([int]($b.X + $b.Width / 2 - $script:r.X), [int]($b.Y + $b.Height / 2 - $script:r.Y)) }
# Stone Barb on the Edit page, its four sockets in the row.
function OnBarb {
  ClickEl (WaitUntil { Tile 'Stone Barb' } "the worn Stone Barb") "Stone Barb"
  WaitUntil { if (@(Cells).Count -eq 4) { 'four' } } "its four sockets" 4000 | Out-Null
}

Case 'socket-strip.row-of-sockets-one-selected-and-said' -Tags sockets, edit {
  OnBarb
  Check ((Row) -eq 'Shael Rune | Shael Rune | Perfect Skull | Ruby Jewel of Envy') "the sockets stand in a row, in the order they were filled ('$(Row)')"
  $cells = Cells
  Check ([math]::Abs($cells[0].Current.BoundingRectangle.Y - $cells[3].Current.BoundingRectangle.Y) -le 1 -and $cells[3].Current.BoundingRectangle.X -gt $cells[0].Current.BoundingRectangle.X) "side by side on one line"
  $tools = ById 'Sockets_Unsocket'
  Check ($tools.Current.BoundingRectangle.Y -lt $cells[0].Current.BoundingRectangle.Y) "the tools on a line over the row"
  Check ((Caption) -eq 'Shael Rune / +20% Increased Attack Speed' -and (ById 'Sockets_UnsocketAll').Current.IsEnabled) "none chosen: the first filled one selected and said (2026-10-28), Unsocket all live ('$(Caption)')"
  $row = $cells[0].Current.BoundingRectangle; $strip = (ById 'Sockets_Strip').Current.BoundingRectangle; $all = (ById 'Sockets_UnsocketAll').Current.BoundingRectangle
  $middle = $strip.X + $strip.Width / 2
  $page = ((ById 'Item_Name').Current.BoundingRectangle)
  Check ([math]::Abs($row.Width - $row.Height) -le 1 -and $strip.X -gt $all.Right) "the sockets are as wide as they are tall, and the row stands in from the tools ($([int]$row.Width)x$([int]$row.Height))"

  # The first one selected opens the caption, under the row's middle, a line to each thing it gives.
  # The heading of the section under this one, by its whole name: "Affixes*" met the item level's own line first -
  # "affixes 84+" - which stands over the sockets and moved for nothing.
  function Under { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -eq 'Affixes & mods' })[0].Current.BoundingRectangle.Y }
  $folded = Under
  ClickEl $cells[0] "the first socket"
  Check ([bool](WaitUntil { if ((Caption) -eq 'Shael Rune / +20% Increased Attack Speed') { 'said' } } "the caption" 3000)) "clicked: said under the row - its name, and what it gives this item ('$(Caption)')"
  Check ((ById 'Sockets_Unsocket').Current.IsEnabled -and -not (ById 'Sockets_EditJewel').Current.IsEnabled) "Unsocket is live for it; Edit jewel is not a rune's"
  $name = (ById 'Sockets_Caption').Current.BoundingRectangle
  Check ([math]::Abs(($name.X + $name.Width / 2) - $middle) -le 3 -and $name.Y -gt $row.Bottom) "the caption stands under the row's middle"
  $open = Under
  Check ([math]::Abs($open - $folded) -le 1) "the caption's room stood with none selected: nothing under it moved as the first was ($([int]$folded) -> $([int]$open))"

  # Another selected, with more lines to it: the room is one for every socket, and nothing under it moves.
  ClickEl $cells[3] "the jewel"
  Check ([bool](WaitUntil { if ((ById 'Sockets_EditJewel').Current.IsEnabled) { 'live' } } "Edit jewel live" 3000)) "a jewel selected: Edit jewel is live"
  $lines = @(((ById 'Sockets_CaptionEffect').Current.Name -split "`n") | Where-Object { $_.Trim() })
  Check ($lines.Count -eq 2) "what it gives, a line to each ($($lines.Count) lines)"
  Check ([math]::Abs((Under) - $open) -le 1) "and nothing under the section moved for it ($([int]$open) -> $([int](Under)))"

  # A right click: selected, and its menu.
  MenuOf $cells[2] 'the skull'
  Check ((MenuIcons) -eq 'Cut; Copy; Paste; Delete' -and (MenuWords) -eq 'Unsocket; Unsocket all') "a socket's menu: Cut, Copy, Paste and the bin (2026-10-21) at its head, Unsocket first ('$(MenuIcons)' / '$(MenuWords)')"
  Check ((Caption) -like 'Perfect Skull / *') "and the right click selected it ('$(Caption)')"
  Keys '{ESC}'
}

Case 'socket-strip.dragged-out-onto-a-cell-and-back-in' -Tags sockets, edit, drag -Changes {
  OnBarb
  $cell = CellPx (WaitUntil { Tile 'Annihilus' } "the Annihilus")
  $p = PaneCell 0 6 1 $cell

  DragTo (Cells)[2] $p
  Check ([bool](WaitUntil { if ((Row) -eq 'Shael Rune | Shael Rune | Ruby Jewel of Envy | Empty socket') { 'out' } } "the skull out of the row" 4000)) "a socket dragged out: gone from the row, the rest closed up ('$(Row)')"
  $at = Mid (WaitUntil { Tile 'Perfect Skull' } "the skull in the inventory")
  Check ([math]::Abs($at[0] - $p[0]) -le 4 -and [math]::Abs($at[1] - $p[1]) -le 4) "and lies on the cell it was let go of on ($($at -join ',') against $($p -join ','))"

  # Back: carried from the grid onto the section - the Edit page stays on Stone Barb while it is carried.
  DragTo (Tile 'Perfect Skull') (ById 'Sockets_Strip')
  Check (Says 'Perfect Skull socketed into Stone Barb.') "a gem dragged from the inventory onto the section: 'Perfect Skull socketed into Stone Barb.'"
  Check ((Row) -eq 'Shael Rune | Shael Rune | Ruby Jewel of Envy | Perfect Skull' -and (ById 'Item_Name').Current.Name -eq 'Stone Barb') "it is in the next socket, and the Edit page is still on Stone Barb ('$(Row)')"
}

# Carried out of the row onto another item with an empty socket: into it, as every carried
# gem goes into the item it is let go of on. The Edit page stays on the item it came from.
Case 'socket-strip.carried-onto-another-item-goes-into-its-empty-socket' -Tags sockets, edit, drag -Changes {
  OnBarb
  $veil = WaitUntil { Tile 'Veil of Steel' } "a Veil of Steel, one socket empty"
  DragTo (Cells)[2] $veil
  Check (Says 'Perfect Skull socketed into Veil of Steel.') "the skull let go of on the helm: 'Perfect Skull socketed into Veil of Steel.'"
  Check ((Row) -eq 'Shael Rune | Shael Rune | Ruby Jewel of Envy | Empty socket' -and (ById 'Item_Name').Current.Name -eq 'Stone Barb') "out of the barb's row, the Edit page still on Stone Barb ('$(Row)')"
  Check ($null -eq (Tile 'Perfect Skull')) "and not lying loose in a grid"
}

Case 'socket-strip.cut-is-pasted-by-a-bare-cells-menu' -Tags sockets, edit, clipboard, menu -Changes {
  OnBarb
  $cell = CellPx (Tile 'Annihilus')
  ClickEl (Cells)[2] "the skull"
  ClickEl (WaitUntil { $c = ById 'Sockets_Cut'; if ($c -and $c.Current.IsEnabled) { $c } } "the tools' Cut" 3000) "Cut"
  Check (Says '1 item cut. Paste to move it.') "Cut: '1 item cut. Paste to move it.'"
  Check ((Row) -like '*Perfect Skull*') "and nothing has left its socket"

  $p = PaneCell 0 7 1 $cell
  Front; [Fast]::SetCursorPos([int]($script:r.X + $p[0]), [int]($script:r.Y + $p[1])) | Out-Null; Start-Sleep -Milliseconds 150
  [Fast]::mouse_event(8, 0, 0, 0, 0); Start-Sleep -Milliseconds 60; [Fast]::mouse_event(16, 0, 0, 0, 0)
  WaitUntil { if (MenuUp) { 'up' } } "the bare cell's menu" 3000 | Out-Null
  MenuPick 'Paste'
  Check ([bool](WaitUntil { if ((Row) -notlike '*Perfect Skull*') { 'out' } } "the skull out" 4000)) "pasted by a bare cell's menu: it is out of its socket ('$(Row)')"
  $at = Mid (WaitUntil { Tile 'Perfect Skull' } "the skull in the inventory")
  Check ([math]::Abs($at[0] - $p[0]) -le 4 -and [math]::Abs($at[1] - $p[1]) -le 4) "on that cell ($($at -join ',') against $($p -join ','))"
}

# A strip of the screen as its pixels, and how much of one reading differs from another, in hundredths: the
# section's wash under a carried gem is ten parts white in a hundred over a drawn stone, which no color names.
Add-Type -AssemblyName System.Drawing
function Patch([int]$x, [int]$y, [int]$w, [int]$h) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size); $g.Dispose()
  $all = for ($i = 0; $i -lt $w; $i++) { for ($j = 0; $j -lt $h; $j++) { $bmp.GetPixel($i, $j) } }
  $bmp.Dispose()
  , @($all)
}
function Differs($a, $b) {
  $n = 0
  for ($i = 0; $i -lt $a.Count; $i++) { if ([math]::Abs($a[$i].R - $b[$i].R) -gt 6 -or [math]::Abs($a[$i].G - $b[$i].G) -gt 6 -or [math]::Abs($a[$i].B - $b[$i].B) -gt 6) { $n++ } }
  [math]::Round(100.0 * $n / $a.Count)
}
function Centre($el) { $b = $el.Current.BoundingRectangle; @([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) }
# The left button pressed on a point of the screen, the pointer taken to another in steps and held there; let go.
function Press($a) { Front; [Fast]::SetCursorPos($a[0], $a[1]) | Out-Null; Start-Sleep -Milliseconds 80; [Fast]::mouse_event(2, 0, 0, 0, 0); Start-Sleep -Milliseconds 80 }
function GoTo($a, $b) {
  for ($i = 1; $i -le 14; $i++) { [Fast]::SetCursorPos([int]($a[0] + ($b[0] - $a[0]) * $i / 14), [int]($a[1] + ($b[1] - $a[1]) * $i / 14)) | Out-Null; Start-Sleep -Milliseconds 20 }
  [Fast]::SetCursorPos($b[0] + 1, $b[1]) | Out-Null; Start-Sleep -Milliseconds 60
  [Fast]::SetCursorPos($b[0], $b[1]) | Out-Null; Start-Sleep -Milliseconds 450
}
function Drop { [Fast]::mouse_event(4, 0, 0, 0, 0); Start-Sleep -Milliseconds 800 }
function Whole { (Row) -eq 'Shael Rune | Shael Rune | Perfect Skull | Ruby Jewel of Envy' -and $null -eq (Tile 'Perfect Skull') }

Case 'socket-strip.carry-is-called-off-by-esc-and-by-the-right-button' -Tags sockets, edit, drag, keys -Changes {
  OnBarb
  $p = PaneCell 0 6 1 (CellPx (WaitUntil { Tile 'Annihilus' } "the Annihilus"))
  $bare = @([int]($script:r.X + $p[0]), [int]($script:r.Y + $p[1]))

  # Carried over a bare cell, where let go of it would land - and Esc pressed first.
  $skull = Centre (Cells)[2]
  Press $skull; GoTo $skull $bare
  [Fast]::keybd_event(0x1B, 0, 0, 0); [Fast]::keybd_event(0x1B, 0, 2, 0); Start-Sleep -Milliseconds 300
  Drop
  Check (Whole) "a socket carried out and called off by Esc: in its socket still, nothing in the inventory ('$(Row)')"

  # The same, called off by the right button: no menu comes of that click.
  $skull = Centre (Cells)[2]
  Press $skull; GoTo $skull $bare
  [Fast]::mouse_event(8, 0, 0, 0, 0); Start-Sleep -Milliseconds 60
  [Fast]::SetCursorPos($bare[0] + 2, $bare[1]) | Out-Null; Start-Sleep -Milliseconds 80
  [Fast]::mouse_event(16, 0, 0, 0, 0); Start-Sleep -Milliseconds 300
  Drop
  Check (Whole) "called off by the right button: in its socket still ('$(Row)')"
  Check (-not (MenuUp)) "and the right button opened no menu"
  if (MenuUp) { Keys '{ESC}' }
}

Case 'socket-strip.section-is-lit-while-a-gem-is-carried-over-it' -Tags sockets, edit, drag -Changes {
  OnBarb
  $p = PaneCell 0 6 1 (CellPx (WaitUntil { Tile 'Annihilus' } "the Annihilus"))
  $bare = @([int]($script:r.X + $p[0]), [int]($script:r.Y + $p[1]))

  # The skull out first: a socket of the item is free for it then.
  DragTo (Cells)[2] $p
  WaitUntil { if ((Row) -eq 'Shael Rune | Shael Rune | Ruby Jewel of Envy | Empty socket') { 'out' } } "the skull out of the row" 4000 | Out-Null
  $from = Centre (WaitUntil { Tile 'Perfect Skull' } "the skull in the inventory")

  # A patch of the section beside the row's last socket, away from the pointer and what it carries.
  $last = (Cells)[-1].Current.BoundingRectangle
  $at = @([int]($last.X + $last.Width + 60), [int]($last.Y + $last.Height / 2 - 5))
  $plain = Patch $at[0] $at[1] 60 10
  Check ((Differs $plain (Patch $at[0] $at[1] 60 10)) -le 5) "nothing carried: the section is as it is drawn"

  Press $from; GoTo $from (Centre (Cells)[0])
  $over = Differs $plain (Patch $at[0] $at[1] 60 10)
  Check ($over -ge 60) "a gem carried over the section: the section is lit ($over in a hundred of the patch changed)"
  Check ((ById 'Item_Name').Current.Name -eq 'Stone Barb') "and the Edit page is still on Stone Barb"

  # Off the section again, over the cell it came from: no longer lit.
  GoTo (Centre (Cells)[0]) $bare
  $off = Differs $plain (Patch $at[0] $at[1] 60 10)
  Check ($off -le 5) "carried off it again: no longer lit ($off in a hundred)"
  Drop
  Check ((Row) -eq 'Shael Rune | Shael Rune | Ruby Jewel of Envy | Empty socket') "let go of off the section: nothing was socketed ('$(Row)')"
}

# The second click, the double click, the tooltip: a socket says itself
# in a tooltip - every one but the one the caption under the row is of (the socket selected shows its
# caption and no tooltip; each unselected one a tooltip); the one selected, clicked
# again, stays so (one is always selected); a double click opens a jewel.
Case 'socket-strip.second-click-keeps-it-a-double-click-opens-the-jewel-and-a-tooltip-on-the-others' -Tags sockets, edit, tooltip {
  OnBarb
  $cells = Cells

  ClickEl $cells[0] "the first socket"
  WaitUntil { if ((Caption) -like 'Shael Rune / *') { 'said' } } "the caption" 3000 | Out-Null
  [Fast]::SetCursorPos([int]($script:r.X + 400), [int]($script:r.Y + 900)) | Out-Null; Start-Sleep -Milliseconds 300
  Hover $cells[1]
  $other = @(TipTexts) -join ' / '
  Check ($other -like 'Shael Rune*Increased Attack Speed*') "one selected, the caption up: another socket says itself still ('$other')"
  [Fast]::SetCursorPos([int]($script:r.X + 400), [int]($script:r.Y + 900)) | Out-Null; Start-Sleep -Milliseconds 300
  Hover $cells[0]
  Check (@(TipTexts).Count -eq 0) "and the one the caption is of has no tooltip"

  ClickEl $cells[0] "the first socket, again"
  Start-Sleep -Milliseconds 300
  Check ((Caption) -like 'Shael Rune / *') "the one selected, clicked again: it stays, said ('$(Caption)')"

  # A double click on the jewel: its layer.
  $b = $cells[3].Current.BoundingRectangle
  Front; [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 150
  [Fast]::mouse_event(2, 0, 0, 0, 0); [Fast]::mouse_event(4, 0, 0, 0, 0); Start-Sleep -Milliseconds 90
  [Fast]::mouse_event(2, 0, 0, 0, 0); [Fast]::mouse_event(4, 0, 0, 0, 0)
  Check ([bool](WaitUntil { ByName 'Back to Stone Barb' } "the jewel's layer" 5000)) "a double click on a jewel opens it to be edited"
  ClickEl (ByName 'Back to Stone Barb') "back"
  WaitUntil { if (-not (ByName 'Back to Stone Barb')) { 'back' } } "Stone Barb again" 5000 | Out-Null
}

# Several sockets at once: Ctrl + click, a rectangle from the section's empty room, and the tools on all of them.
Case 'socket-strip.several-selected-by-ctrl-and-a-rectangle-are-unsocketed-together' -Tags sockets, edit, drag, select -Changes {
  OnBarb
  $cells = Cells

  # A rectangle from the room left of the row, over the first two sockets.
  $a = $cells[0].Current.BoundingRectangle; $b = $cells[1].Current.BoundingRectangle
  $from = @([int]($a.X - 60), [int]($a.Y - 6)); $to = @([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height - 4))
  Press $from; GoTo $from $to; Drop
  Check ([bool](WaitUntil { if ((ById 'Sockets_Caption').Current.Name -eq '2 sockets selected') { 'two' } } "two selected" 3000)) "a rectangle over two sockets selects the two ('$((ById 'Sockets_Caption').Current.Name)')"
  Check (-not (ById 'Sockets_EditJewel').Current.IsEnabled -and (ById 'Sockets_Unsocket').Current.IsEnabled) "Edit jewel is one jewel's; Unsocket is live for them"

  # A click on the section's empty room lets go: the first filled one selected again.
  Front; [Fast]::SetCursorPos($from[0], $from[1]) | Out-Null; Start-Sleep -Milliseconds 120
  [Fast]::mouse_event(2, 0, 0, 0, 0); Start-Sleep -Milliseconds 40; [Fast]::mouse_event(4, 0, 0, 0, 0)
  Check ([bool](WaitUntil { if ((ById 'Sockets_Caption').Current.Name -eq 'Shael Rune') { 'first' } } "the first selected" 3000)) "a click on the section's empty room lets them go, back to the first filled one"

  # Ctrl + click: the first and the third; Unsocket takes both out.
  ClickEl (Cells)[0] "the first socket"
  CtrlClickEl (Cells)[2] "the third socket, with Ctrl"
  Check ([bool](WaitUntil { if ((ById 'Sockets_Caption').Current.Name -eq '2 sockets selected') { 'two' } } "two selected" 3000)) "Ctrl + click adds a socket to the one selected"
  ClickEl (ById 'Sockets_Unsocket') "Unsocket"
  Check ([bool](WaitUntil { if ((Row) -eq 'Shael Rune | Ruby Jewel of Envy | Empty socket | Empty socket') { 'out' } } "both out" 4000)) "Unsocket: both are out, the rest closed up ('$(Row)')"
  Check ($null -ne (Tile 'Perfect Skull') -and $null -ne (Tile 'Shael Rune')) "and lie in the inventory"
}

# A gem copied or cut in a grid is pasted straight into the item's sockets: by the tools' Paste, and by Ctrl + V with the pointer over the section.
Case 'socket-strip.gem-copied-or-cut-is-pasted-into-a-socket-by-the-tools-and-by-ctrl-v' -Tags sockets, edit, clipboard, keys -Changes {
  OnBarb
  $p = PaneCell 0 6 1 (CellPx (WaitUntil { Tile 'Annihilus' } "the Annihilus"))
  Check (-not (ById 'Sockets_Paste').Current.IsEnabled) "nothing held: the tools' Paste is dimmed"

  # The skull out, so the item has a socket for one; then copied where it lies, and pasted by the tools.
  DragTo (Cells)[2] $p
  WaitUntil { if ((Row) -eq 'Shael Rune | Shael Rune | Ruby Jewel of Envy | Empty socket') { 'out' } } "the skull out of the row" 4000 | Out-Null
  ClickEl (WaitUntil { Tile 'Perfect Skull' } "the skull in the inventory") "the skull"
  Keys '^c'
  OnBarb
  ClickEl (WaitUntil { $b = ById 'Sockets_Paste'; if ($b -and $b.Current.IsEnabled) { $b } } "the tools' Paste, live" 3000) "Paste"
  Check ([bool](WaitUntil { if ((Row) -eq 'Shael Rune | Shael Rune | Ruby Jewel of Envy | Perfect Skull') { 'in' } } "the skull's second in the socket" 4000)) "a gem copied, pasted by the tools: its second is in the next socket ('$(Row)')"
  Check ($null -ne (Tile 'Perfect Skull')) "and the one copied lies where it lay"
  Check (-not (ById 'Sockets_Paste').Current.IsEnabled) "the item has no socket left: Paste is dimmed"

  # One out again, and the skull in the inventory cut and pasted by the key, the pointer over the section.
  ClickEl (Cells)[0] "the first socket"
  ClickEl (WaitUntil { $u = ById 'Sockets_Unsocket'; if ($u -and $u.Current.IsEnabled) { $u } } "Unsocket, live" 3000) "Unsocket"
  WaitUntil { if ((Row) -like '*Empty socket') { 'room' } } "a socket free" 4000 | Out-Null
  $skulls = @(Tiles 'Perfect Skull').Count
  ClickEl (Tile 'Perfect Skull') "a skull in the inventory"
  Keys '^x'
  OnBarb
  $s = (ById 'Sockets_Strip').Current.BoundingRectangle
  Front; [Fast]::SetCursorPos([int]($s.X + $s.Width / 2), [int]($s.Y + $s.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 300
  Keys '^v'
  Check ([bool](WaitUntil { if ((Row) -notlike '*Empty socket*') { 'full' } } "the row full again" 4000)) "a gem cut, Ctrl + V over the section: it is in the socket ('$(Row)')"
  Check (@(Tiles 'Perfect Skull').Count -eq $skulls - 1) "and has left the inventory ($(@(Tiles 'Perfect Skull').Count) of $skulls left)"
}

# The sockets take gems, runes and jewels and nothing else: a ring dragged onto the Sockets section's row is not socketed, and is back where it lay.
Case 'socket-strip.ring-dragged-onto-the-sockets-is-not-socketed' -Tags sockets, edit, drag -Changes {
  $ring = 'Steel Ring of Skill'
  OnBarb
  $p = PaneCell 0 6 1 (CellPx (WaitUntil { Tile 'Annihilus' } "the Annihilus"))
  # A socket free, so only what the ring is stands in its way.
  DragTo (Cells)[2] $p
  WaitUntil { if ((Row) -eq 'Shael Rune | Shael Rune | Ruby Jewel of Envy | Empty socket') { 'out' } } "the skull out of the row" 4000 | Out-Null

  Pane 0 'Personal Stash'
  OnBarb
  $from = Mid (WaitUntil { Tile $ring } "the ring in the stash")
  $empty = (Cells)[3]
  DragTo (Tile $ring) $empty
  Start-Sleep -Milliseconds 600
  $at = Mid (WaitUntil { Tile $ring } "the ring" 4000)
  Check (@(Tiles $ring).Count -eq 1 -and [math]::Abs($at[0] - $from[0]) -le 4 -and [math]::Abs($at[1] - $from[1]) -le 4) "a ring dragged onto an empty socket: back where it lay ($($at -join ',') against $($from -join ','))"
  OnBarb
  Check ((Row) -eq 'Shael Rune | Shael Rune | Ruby Jewel of Envy | Empty socket') "and nothing went into the socket ('$(Row)')"
  Pane 0 'Inventory'
}
