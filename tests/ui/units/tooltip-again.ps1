# An item's tooltip reads to UI Automation however often it is shown.
# A grid tile's tooltip was a data object
# drawn through a template: the framework kept one ToolTip a tile, and from its second showing on every walk
# of the window failed with E_FAIL while it was up - a screen reader's loss, and what made tests that read
# anything after a second hover fail now and then (sockets). Three hovers each of a stash tile, a worn item
# and a vault tile: each time the window is walked, the tooltip found, and its lines the same as the first
# time.
#
# All of it is the window's to show - the tooltip as the framework builds it and UI Automation reads it; nothing
# of it is a view model's, so there are no unit tests beside these. Nothing is written.

function PointAt($el) {
  $b = $el.Current.BoundingRectangle
  Front
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2) - 4, [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 80
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null
}
function Away { [Fast]::SetCursorPos([int]($script:r.X + $script:r.Width * 0.75), [int]($script:r.Y + 60)) | Out-Null; Start-Sleep -Milliseconds 600 }
# How many lines the tooltip up has - asked of the window itself, with no retry: the walk failing is the fault.
function Lines {
  try {
    $tips = @($script:win.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::ToolTip)))
    if ($tips.Count -ne 1) { return 0 }
    @($tips[0].FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text))).Count
  }
  catch { -1 }
}
function Thrice([scriptblock]$find, [string]$what) {
  $read = @()
  for ($n = 1; $n -le 3; $n++) {
    PointAt (& $find)
    # Up within a moment; a walk that fails is not waited out.
    $lines = 0
    for ($wait = 0; $wait -lt 12 -and $lines -eq 0; $wait++) { Start-Sleep -Milliseconds 150; $lines = Lines }
    $read += $lines
    Away
  }
  Check ($read[0] -gt 1 -and $read[1] -eq $read[0] -and $read[2] -eq $read[0]) "$what, hovered three times: $($read -join ', ') lines read"
}

Case 'tooltip-again.stash-tile-reads-each-time-it-is-shown' -Tags tooltip, items {
  Pane 0 'Personal Stash'
  try {
    WaitUntil { Tile 'Stalagmite' } "the stash's Stalagmite" | Out-Null
    Thrice { Tile 'Stalagmite' } "a stash tile"
  }
  # The pane as the case found it, for the cases after.
  finally { Pane 0 'Inventory' }
}

Case 'tooltip-again.worn-item-reads-each-time-it-is-shown' -Tags tooltip, items {
  WaitUntil { Tile 'Stone Barb' } "the worn Stone Barb" | Out-Null
  Thrice { Tile 'Stone Barb' } "a worn item"
}

Case 'tooltip-again.vault-tile-reads-each-time-it-is-shown' -Tags tooltip, vault {
  ClickEl (ById 'Bookmark_Vault_Right') "vault"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  $right = { @((All (Cond $AE::AutomationIdProperty 'Tile')) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt (BookMiddle) })[0] }
  WaitUntil $right "a vault tile" | Out-Null
  Thrice $right "a vault tile"
}
