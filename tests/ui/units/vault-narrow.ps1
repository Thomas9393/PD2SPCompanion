# The vault page on a narrow page: at the
# window's least size the tools stay one line, what does not fit listed under a "more" button whose rows do what
# the tools do; the sheet narrows in fours, a folder's tile at a time, down to the four columns that are the fewest
# it has - the map and the tree give way only where even
# four would not fit; the folders' tiles, four cells wide, fill each row of it. Wide again, it is all back. All of it is measured in the window: these are
# the cases of what was vault-narrow.ps1, each sizing the window for itself and putting it back as wide as the
# screen.
#
# What the page makes of being told it has no room - the map and the tree put away, their buttons saying why -
# is proven without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultRoomTests.
#
# Nothing in the vault is changed.
Add-Type -AssemblyName System.Windows.Forms
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 400 }

# Whether something is drawn in the window: a tool left off the line is still there, laid far out of it.
function InSight($el) {
  if (-not $el) { return $false }
  $b = $el.Current.BoundingRectangle
  -not $b.IsEmpty -and $b.X -ge $script:r.X -and $b.X + $b.Width -le $script:r.X + $script:r.Width -and $b.Width -gt 0
}
function Any([string]$id) { Steady { @((All (Cond $AE::AutomationIdProperty $id)))[0] } }
function Tiles2 { @((All (Cond $AE::AutomationIdProperty 'SheetFolder')) | Where-Object { InSight $_ }) }
# How many of the folders' tiles stand side by side: the places across they begin at.
function Across { @(Tiles2 | ForEach-Object { [int]$_.Current.BoundingRectangle.X } | Sort-Object -Unique).Count }
function MoreRow([string]$like) { Steady { foreach ($row in (All (Cond $AE::AutomationIdProperty 'Vault_MoreRow'))) { if ($row.Current.Name -like $like -and -not $row.Current.IsOffscreen) { return $row } } } }
function Scroll($id) { (ById $id).GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current }
function Right($el) { $b = $el.Current.BoundingRectangle; $b.X + $b.Width }
function Bin { @((All (Cond $AE::HelpTextProperty 'Delete selected item (Del)')) | Where-Object { InSight $_ })[0] }

# The vault on the left page, at its top.
function OpenVault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  Start-Sleep -Milliseconds 500
  VaultGoTo 'Vault'
}
# The window at its least - asked for less than it takes, it is as small as it goes - for what a case looks at,
# and as wide as the screen again whatever came of it: the cases after this one are drawn wide.
function AtLeast([scriptblock]$look) {
  try {
    Size 900 600
    WaitUntil { if (InSight (Any 'Vault_More')) { 'more' } } "the more button" 4000 | Out-Null
    & $look
  }
  finally {
    $script:show = 3
    try { Wide } catch { }
  }
}

Case 'vault-narrow.wide-every-tool-on-the-line' -Tags vault, vault-narrow, layout {
  OpenVault

  # Wide: every tool on the line, no "more"; the map and the tree there.
  Check (-not (InSight (Any 'Vault_More')) -and (InSight (Any 'Vault_RenameFolder'))) "the window wide: every tool is on the line, and there is no more button"
  Check ((InSight (Any 'Vault_Map')) -and (InSight (Any 'Vault_Tree'))) "the map and the tree are there"

  # And what the page shows stands at the line's far end, a gap between it and the tools (aligned right
  # when there is room), Compare outermost.
  $gap = (Any 'Vault_TreeToggle').Current.BoundingRectangle.X - (Right (Bin))
  $edge = (Right (Any 'Vault_Count')) - (Right (Any 'Vault_Compare'))
  Check ($gap -gt 300 -and [math]::Abs($edge) -le 12) "the window wide: the view buttons are at the line's far end (a gap of $([int]$gap) after the tools, $([int]$edge) from the page's edge)"
}

Case 'vault-narrow.tools-stay-one-line-with-a-more-button' -Tags vault, vault-narrow, layout {
  OpenVault
  AtLeast {
    $least = $script:win.Current.BoundingRectangle
    "  the window at its least: $([int]$least.Width) x $([int]$least.Height)"

    # The tools: one line, the rest under the more button.
    # Against the bin, which never leaves the line.
    $bin = @((All (Cond $AE::HelpTextProperty 'Delete selected item (Del)')) | Where-Object { InSight $_ })[0].Current.BoundingRectangle
    $more = (Any 'Vault_More').Current.BoundingRectangle
    Check ([math]::Abs($more.Y - $bin.Y) -le 2) "the tools stay one line: the more button stands on it, at its end"
    Check (-not (InSight (Any 'Vault_RenameFolder'))) "what does not fit is off the line (Rename folder, the first to go)"
    $bins = @((All (Cond $AE::HelpTextProperty 'Delete selected item (Del)')) | Where-Object { InSight $_ })
    Check ($bins.Count -eq 1 -and [math]::Abs($bins[0].Current.BoundingRectangle.Y - $more.Y) -le 4) "the bin never leaves it"
    # The view's buttons begin with the two socket buttons (the sockets shown or faded, the socket
    # guard - the Items page's two): the gap is measured to the first of the view's buttons that is on the line -
    # either of those two may be left off it at this width. To the tree's toggle it read 250, two buttons more,
    # and failed for a line that was laid out as it should be.
    $view = @('Vault_Sockets', 'Vault_SocketGuard', 'Vault_TreeToggle' | ForEach-Object { Any $_ } | Where-Object { InSight $_ } |
              Sort-Object { $_.Current.BoundingRectangle.X })
    Check ($view.Count -ge 1 -and $view[-1].Current.AutomationId -eq 'Vault_TreeToggle') "the socket buttons that are on the line stand before the tree's toggle ($(($view | ForEach-Object { $_.Current.AutomationId }) -join ', '))"
    $gap = $view[0].Current.BoundingRectangle.X - (Right (Bin))
    $after = $more.X - (Right (Any 'Vault_Compare'))
    # Between the bin and the view buttons: the two menus, the star and the folder's lock (without the
    # star 175, the two menus alone 130).
    Check ($gap -lt 215 -and $after -ge 0 -and $after -le 14) "narrow, the view buttons close up with the rest, the more button after them (a gap of $([int]$gap), more $([int]$after) after)"
  }
}

Case 'vault-narrow.more-list-does-what-the-tools-do' -Tags vault, vault-narrow, layout {
  OpenVault
  AtLeast {
    # The list under the more button does what the tools do: Rename folder, a folder's tile selected (the tool is
    # the selected folder's).
    VaultGoTo 'Charms'
    ClickEl (WaitUntil { VaultFolderTile 'Small' } "the tile Small" 3000) "the tile Small"
    ClickUntil { ClickEl (Any 'Vault_More') "more" } { MoreRow 'Rename folder*' } "the list of tools left off" 800 | Out-Null
    if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-narrow-more.png') }
    Check ($null -ne (MoreRow 'Rename folder*') -and $null -ne (MoreRow 'Cut*')) "the more button lists them, by what their tooltips say"
    ClickEl (MoreRow 'Rename folder*') "Rename folder, in the list"
    Check ([bool](WaitUntil { ById 'SheetFolder_Name' } "the folder's name box" 3000)) "a row of the list does what its tool does: Rename folder opens the name box on the selected folder's tile"
    Keys '{ESC}'
    WaitUntil { if (-not (ById 'SheetFolder_Name')) { 'shut' } } "the name box shut" 3000 | Out-Null
    VaultGoTo 'Vault'
  }
}

Case 'vault-narrow.sheet-narrows-in-fours-and-the-map-stays' -Tags vault, vault-narrow, layout {
  OpenVault
  $cell = 28 * (Unit)
  $wideSheet = (ById 'Vault_Pages').Current.BoundingRectangle.Width
  AtLeast {
    # The sheet has given the room, not the map: fewer columns than wide, four at the least, and the map still
    # beside them.
    $sheet = (ById 'Vault_Pages').Current.BoundingRectangle
    Check ($sheet.Width -ge 4 * $cell -and $sheet.Width -lt $wideSheet) "the sheet is narrower, four columns or more ($([int]$sheet.Width) across where it was $([int]$wideSheet), a cell $cell)"
    Check (InSight (Any 'Vault_Map')) "and the map has kept its place beside it"
    Check (-not (Scroll 'Vault_Pages').HorizontallyScrollable) "what is in sight is all of the sheet: no way sideways"
  }

  # Between the least and the wide: the tree open, the map beside the sheet, and no bar across.
  try {
    Size 1600 900
    Start-Sleep -Milliseconds 900
    $sheet = (ById 'Vault_Pages').Current.BoundingRectangle
    Check ($sheet.Width -ge 4 * $cell) "at 1600 across: four columns of the sheet or more are in sight ($([int]$sheet.Width))"
    Check (-not (Scroll 'Vault_Pages').HorizontallyScrollable) "and the sheet has no way sideways"
  }
  finally {
    $script:show = 3
    try { Wide } catch { }
  }
}

Case 'vault-narrow.folder-tiles-fill-the-row-and-all-back-when-wide' -Tags vault, vault-narrow, folder-tiles, layout {
  OpenVault
  $wide = Across
  Check ($wide -ge 4) "the window wide: the top's folders stand side by side ($wide across)"
  AtLeast {
    # The sheet is a whole number of tiles wide, a tile four of its columns: as many to a row as it has fours,
    # filling it; the rest under them - nothing goes sideways.
    $cell = 28 * (Unit)
    $fours = { [int][Math]::Floor(((ById 'Vault_Pages').Current.BoundingRectangle.Width + 2) / (4 * $cell)) }
    WaitUntil { if ((Across) -lt $wide -and (Across) -eq (& $fours)) { 'rows' } } "the tiles in rows of the sheet's fours" 4000 | Out-Null
    Check ((Across) -ge 1 -and (Across) -lt $wide -and (Across) -eq (& $fours)) "the sheet narrow: its folders' tiles stand $(Across) to a row, as many as it has fours ($(& $fours))"
    Check (-not (Scroll 'Vault_Pages').HorizontallyScrollable) "and the sheet does not go sideways for them"
    if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-narrow.png') }
  }

  # Wide again: everything is back, as it was asked for.
  Check ([bool](WaitUntil { if ((InSight (Any 'Vault_Map')) -and -not (InSight (Any 'Vault_More'))) { 'back' } } "the map back, the more button gone" 4000)) "the window wide again: the map is back and every tool is on the line"
  Check ([bool](WaitUntil { if ((Across) -eq $wide) { 'rows' } } "the tiles side by side again" 4000)) "and the folders' tiles stand side by side again ($(Across) across)"
  $gap = (Any 'Vault_TreeToggle').Current.BoundingRectangle.X - (Right (Bin))
  Check ($gap -gt 300) "and the view buttons at the far end again ($([int]$gap))"
}
