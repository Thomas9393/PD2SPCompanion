# The window as it was left: where it stood and how large, given back when the program opens
# again - and not the page it showed: a new start opens on the home page (the page came back
# too for a day). Every other case meets the window as a new install has it (PD2SPC_FOLDS=off); this one keeps it
# for real, and closes the program as its own cross does - a kill keeps nothing.

Case 'window-place.window-and-page-come-back' -Tags window, settings -Before {
  $env:PD2SPC_FOLDS = $null
} {
  try {
    # Shown as it stands, not maximized, for the while: the size is what is asked.
    $script:show = 5
    $h = $script:proc.MainWindowHandle
    [Fast]::ShowWindow($h, 9) | Out-Null; Start-Sleep -Milliseconds 400
    $move = $script:win.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    $move.Move(130, 90); Start-Sleep -Milliseconds 300
    $move.Resize(1500, 960); Start-Sleep -Milliseconds 500
    $was = $script:win.Current.BoundingRectangle
    Check ([int]$was.Width -gt 0) "start: the window at $([int]$was.X),$([int]$was.Y), $([int]$was.Width) x $([int]$was.Height)"

    ClickEl (ById 'SideBar_Extra') "the Extra page"
    WaitUntil { ById 'Runes_Bases' } "the Extra page" | Out-Null

    # Closed by its own cross: what it keeps is written as it closes.
    $script:win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Check ([bool]($script:proc.WaitForExit(10000))) "closed as its cross closes it"
    $mode = $script:session.Mode
    $script:session = $null
    Start-Sleep -Milliseconds 500
    Start-Session $pad $mode

    $now = $script:win.Current.BoundingRectangle
    Check ([Math]::Abs($now.X - $was.X) -le 8 -and [Math]::Abs($now.Y - $was.Y) -le 8) "opened again where it stood ($([int]$now.X),$([int]$now.Y))"
    Check ([Math]::Abs($now.Width - $was.Width) -le 8 -and [Math]::Abs($now.Height - $was.Height) -le 8) "and as large ($([int]$now.Width) x $([int]$now.Height))"
    Check ([bool](WaitUntil { ByName 'Open' } "the home page" 5000) -and $null -eq (ById 'Runes_Bases')) "on the home page, not the page it was closed on"
  }
  finally {
    $script:show = 3
    $env:PD2SPC_FOLDS = 'off'
  }
}

# Left maximized, it opens maximized - and left a window of its own, as one: asked both ways round, since either
# alone is also what a program that kept nothing might open as. Shown as it stands for the while, so that what is
# read is how the program opened it and not how the runner's own bringing to the front would show it.
Case 'window-place.left-maximized-it-opens-maximized' -Tags window, settings -Before {
  $env:PD2SPC_FOLDS = $null
} {
  try {
    $state = { $script:win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Current.WindowVisualState }
    $script:show = 5
    foreach ($left in @(@{ Show = 9; As = 'Normal' }, @{ Show = 3; As = 'Maximized' })) {
      [Fast]::ShowWindow($script:proc.MainWindowHandle, $left.Show) | Out-Null; Start-Sleep -Milliseconds 500
      Check ((& $state) -eq $left.As) "the window left $($left.As)"

      $script:win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
      Check ([bool]($script:proc.WaitForExit(10000))) "closed as its cross closes it"
      $mode = $script:session.Mode
      $script:session = $null
      Start-Sleep -Milliseconds 500
      Start-Session $pad $mode
      Check ((& $state) -eq $left.As) "opened again $($left.As) ($(& $state))"
    }

    # Restored from maximized: a window of its own, smaller than the screen it fills while maximized. The
    # maximized size itself had been kept as that place, and a restore did nothing.
    $full = $script:win.Current.BoundingRectangle
    [Fast]::ShowWindow($script:proc.MainWindowHandle, 9) | Out-Null; Start-Sleep -Milliseconds 700
    $own = $script:win.Current.BoundingRectangle
    Check ((& $state) -eq 'Normal' -and $own.Width -lt $full.Width * 0.97 -and $own.Height -lt $full.Height * 0.97) "restored, it is a window of its own size ($([int]$own.Width) x $([int]$own.Height), maximized $([int]$full.Width) x $([int]$full.Height))"
  }
  finally {
    $script:show = 3
    $env:PD2SPC_FOLDS = 'off'
  }
}
