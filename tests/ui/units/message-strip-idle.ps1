# The message strip is put away when nothing has been said for a while, and comes back under the pointer (idle for 15
# seconds it is hidden, and the cursor moved onto it brings it back, to show its history). Read off the screen, since that is what the change is: the strip's own
# rectangle as it is drawn - shown, then gone, then back with the pointer on it and its history in a tooltip.
#
# What the log behind the strip decides - bright, dimmed after five seconds, idle after fifteen, back the moment
# something is said, the ten its history keeps - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Window/MessageStripTests.
#
# The wait is shortened for the case (PD2SPC_STRIP_IDLE), so it has a start of the program to itself. It begins
# on the home page: the line it times is the one the load says. Changes nothing saved - the bin is pressed
# once, which asks.

Add-Type -AssemblyName System.Drawing

# The strip's line, by its words; and how much of a rectangle of the screen is the strip's black backing, in
# hundredths: most of it while the strip is shown, whatever it says, and far less of the bar's ornament behind
# it (measured: 77 shown, 42 put away - the ornament has its own dark).
function Line([string]$like) { Steady { foreach ($t in (All (Cond $AE::ControlTypeProperty $CT::Text))) { if (-not $t.Current.IsOffscreen -and $t.Current.Name -like $like) { return $t } } } }
function Backing($rect) {
  $w = [int]$rect.Width; $h = [int]$rect.Height
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bmp.Size); $g.Dispose()
  $dark = 0; $n = 0
  for ($x = 0; $x -lt $w; $x += 2) { for ($y = 0; $y -lt $h; $y += 2) { $c = $bmp.GetPixel($x, $y); if (($c.R + $c.G + $c.B) / 3.0 -lt 22) { $dark++ }; $n++ } }
  $bmp.Dispose()
  [math]::Round(100.0 * $dark / [math]::Max(1, $n), 1)
}
function Away { Front; [Fast]::SetCursorPos([int]($script:r.X + $script:r.Width / 2), [int]($script:r.Y + $script:r.Height / 2)) | Out-Null }
function TipUp { try { @((All (Cond $AE::ControlTypeProperty $CT::ToolTip))).Count -gt 0 } catch { $false } }

Case 'message-strip-idle.put-away-when-idle-and-back-under-the-pointer' -Tags window, strip -Start Home -Before {
  # Past the five seconds a message is bright for, so shown and gone are told apart, not bright and dim.
  $env:PD2SPC_STRIP_IDLE = '7500'
} {
  try {
    ClickEl (WaitUntil { ByName 'Open' } "Load") "Load"
    WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "loaded" | Out-Null
    $said = [Diagnostics.Stopwatch]::StartNew()
    $line = WaitUntil { Line 'Opened *' } "the strip's line"
    $b = $line.Current.BoundingRectangle
    # The strip about its words: a band of it, clear of the words' own ends.
    $band = New-Object System.Windows.Rect ($b.X - 6), ($b.Y - 2), 260, ($b.Height + 4)
    Away

    # Shown, its words dimmed: six seconds in.
    Start-Sleep -Milliseconds ([math]::Max(0, 6000 - $said.ElapsedMilliseconds))
    $shown = Backing $band
    # Idle: put away, and the bar's ornament is what is there.
    Start-Sleep -Milliseconds ([math]::Max(0, 9200 - $said.ElapsedMilliseconds))
    $gone = Backing $band
    Check ($shown -gt 50 -and $gone -lt $shown - 20) "nothing said for a while: the strip is put away (its backing, of the band: $shown -> $gone in a hundred)"

    # The pointer on where it lies: back, with its history.
    Front; [Fast]::SetCursorPos([int]($b.X + 40), [int]($b.Y + $b.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 120
    [Fast]::SetCursorPos([int]($b.X + 44), [int]($b.Y + $b.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 500
    $back = Backing $band
    Check ($back -gt $shown - 10) "the pointer over it: it is back ($back)"
    Check ([bool](WaitUntil { if (TipUp) { 'up' } } "the history" 4000)) "and its history comes up, as before"

    # The pointer gone: it fades out again - part way a moment after, gone a second after.
    Away
    Start-Sleep -Milliseconds 300
    $fading = Backing $band
    Start-Sleep -Milliseconds 1200
    $again = Backing $band
    Check ($again -lt $shown - 20) "the pointer away: put away again ($again)"
    Check ($fading -gt $again + 3) "and on the way it fades: still partly there a moment after ($fading, then $again)"

    # Something said: it is there again.
    ClickEl (ById 'SideBar_Inventory') "Items"
    WaitUntil { ItemsPage } "items page" | Out-Null
    ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
    WaitUntil { ById 'Vault_Pages' } "the sheet" | Out-Null
    ClickEl (WaitUntil { VaultTile 'Tome of Town Portal' } "the tome") "the tome"
    ClickEl (WaitUntil { ByTip 'Delete selected item (Del)' } "the bin") "the bin, once"
    $asked = WaitUntil { Line 'Delete *' } "the bin's question on the strip" 4000
    Away
    Start-Sleep -Milliseconds 600
    $told = Backing $band
    Check ($null -ne $asked -and $told -gt $gone + 20) "something said: the strip is there again ($told)"
  }
  finally { $env:PD2SPC_STRIP_IDLE = $null }
}
