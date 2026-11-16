# Tooltips that list what keys do, as only a window shows them: a tooltip coming up draws
# its key lines as an item's tooltip draws its strip - the key as a cap, what it does beside it - while the text UI
# Automation reads of the control stays its tooltip's own; a single shortcut after a name stays one line of words;
# a tooltip that shows a control - a worn item's - still shows it (the first way of doing this blanked those); the
# Edit page says the two drags under "n selected" and not under "Please select an item."; and Shift + drag begun
# on an item draws the rectangle the Selection menu teaches.
#
# Which lines of a text are a key's, what the Selection menus and the Edit page hand over, is proven without a
# window: tests/PD2SPCompanion.Shell.Tests/Keys/KeyedTipTests.
#
# Nothing is edited.

function Button([string]$tip) { @((All (Cond $AE::ControlTypeProperty $CT::Button)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.HelpText -like $tip })[0] }
# Every text on the page, as it stands.
function PageTexts { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) }

Case 'key-tips.key-lines-are-drawn-as-caps-the-controls-text-kept' -Tags tooltip, keys {
  ClickEl (ById 'SideBar_Char') "Character"
  $add = WaitUntil { Button 'Add points*' } "an Add points button"
  Check (($add.Current.HelpText -replace "`r?`n", ' / ') -eq 'Add points / Ctrl + LMB: 5 points / Shift + LMB: 20 points / Ctrl + Shift + LMB: the most') "the control's own text is its tooltip's, as ever"
  Hover $add
  $texts = @(TipTexts) -join ' | '
  Check ($texts -eq 'Add points | Ctrl + LMB | 5 points | Shift + LMB | 20 points | Ctrl + Shift + LMB | the most') "drawn: the name, then each key apart from what it does: '$texts'"
}

Case 'key-tips.one-shortcut-after-a-name-stays-words' -Tags tooltip, keys {
  # The side bar's Home button: the search's arrow, "Next find (F3)", stood here until the search became a page.
  Hover (WaitUntil { Button 'Home (Ctrl + 1)' } "the Home button")
  $texts = @(TipTexts) -join ' | '
  Check ($texts -eq 'Home (Ctrl + 1)') "one shortcut after a name stays a line of words: '$texts'"
}

Case 'key-tips.a-tooltip-showing-a-control-still-shows-it' -Tags tooltip, items {
  Hover (WaitUntil { Tile 'Stone Barb' } "the worn Stone Barb")
  $texts = @(TipTexts)
  # Headed as the game heads it, its socket count after the name.
  Check ($texts -contains 'Stone Barb [4]' -and $texts -contains 'RMB' -and $texts -contains 'Menu') "a worn item's tooltip, which shows a control, still shows it: $($texts.Count) lines, its name and its strip among them"
}

Case 'key-tips.words-over-the-keys-stay-words' -Tags tooltip, items, keys {
  ClickUntil { ClickEl (WaitUntil { Tile 'Stone Barb' } "the worn Stone Barb") "Stone Barb" } { OnEdit 'Stone Barb' } "Stone Barb on the Edit page" | Out-Null
  Hover (WaitUntil { Button 'Remove socket*' } "the socket stepper's minus")
  $texts = @(TipTexts) -join ' | '
  Check ($texts -like 'Remove socket*Its socketed item comes out | Shift + LMB | no sockets') "words over the keys stay words: '$($texts -replace "`r?`n", ' / ')'"
}

Case 'key-tips.selection-menu-says-the-two-drags' -Tags tooltip, items, keys {
  Pane 0 'Personal Stash'
  try {
    Start-Sleep -Milliseconds 500
    Hover (WaitUntil { Button 'Selection*' } "a Selection menu")
    $texts = @(TipTexts) -join ' | '
    Check ($texts -eq 'Selection | Shift + Drag | select area | Ctrl + Drag | add area') "the Selection menu says how: '$texts'"
  }
  # The pane as the case found it, for the cases after.
  finally { Pane 0 'Inventory' }
}

Case 'key-tips.shift-drag-on-an-item-picks-and-the-edit-page-says-the-drags' -Tags items, keys, drag {
  # The Edit page says it too, where the eye is with several picked: the two drags as key caps. Not with nothing
  # chosen (under "select an item" they looked odd).
  ClickEl (ById 'Bookmark_Edit') "Edit"
  [Fast]::keybd_event(0x1B, 0, 0, 0); [Fast]::keybd_event(0x1B, 0, 2, 0); Start-Sleep -Milliseconds 600
  $page = PageTexts
  Check ($page -contains 'Please select an item.' -and $page -notcontains 'Shift + Drag' -and $page -notcontains 'Ctrl + Drag') "nothing chosen: 'Please select an item.', and no drags under it"

  # The Selection menu teaches the two drags that start a rectangle on top of an item - and they do, on a page
  # packed tight: Shift + drag from one item of the personal stash across its neighbours picks them.
  Pane 0 'Personal Stash'
  try {
    Start-Sleep -Milliseconds 500
    $ring = WaitUntil { if (Tiles 'Steel Ring of Skill') { Tile 'Steel Ring of Skill' } } "the stash's first ring"
    DragTo $ring (CellOf $ring 3 2) 'Shift' -Hold 300
    $picked = try { WaitUntil { ByTip 'Store ' -Prefix | Where-Object { $_.Current.HelpText -match '^Store \d+ selected items' } } "several picked" 4000 } catch { $null }
    Check ($null -ne $picked) "Shift + drag begun on an item draws the rectangle: '$(if ($picked) { ($picked.Current.HelpText -split "`n")[0] })'"
    $page = PageTexts
    $said = @($page | Where-Object { $_ -match '^\d+ selected$' })
    Check ($said.Count -eq 1 -and $page -contains 'Shift + Drag' -and $page -contains 'Ctrl + Drag') "several picked: '$($said -join ', ')' with the two drags under it"
  }
  finally {
    # The picks let go, and the pane as the case found it, for the cases after.
    [Fast]::keybd_event(0x1B, 0, 0, 0); [Fast]::keybd_event(0x1B, 0, 2, 0); Start-Sleep -Milliseconds 300
    Pane 0 'Inventory'
  }
}
