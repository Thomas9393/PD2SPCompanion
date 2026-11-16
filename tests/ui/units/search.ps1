# The window's search box and the item search's page, as only a window shows them: where the box's list and the Search page are drawn, that a click and a key reach
# what they should, and what the view itself gives the search - the settings and the fold headings it finds by
# name, a find scrolled into sight.
#
# What the two searches decide - the groups and their order, the few each lists and which, the row that asks
# before another character is loaded, the mark the arrows move, a group folded, what the page lists, when the left
# page is marked, what a "See all" line and Ctrl + Enter ask, the previous search, what is read while the game
# runs - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Search, and Grail/GrailSearchTests for the
# grail's own box.
#
# Nothing here changes what is saved, and nothing is typed through the keyboard: the boxes are set through UI
# Automation.

$script:Value = [System.Windows.Automation.ValuePattern]::Pattern

function Box { ById 'Quick_Box' }
# The box waited for: asked for at the instant a page changes, the window's tree gave nothing back once
# (after a click on the side bar), and the case stopped on a box that was there.
function Typed { $b = Box; if ($b) { $b.GetCurrentPattern($script:Value).Current.Value } }
function TypeIn([string]$text) { (WaitUntil { Box } "the window's search box" 3000).GetCurrentPattern($script:Value).SetValue($text) }
function Rows { @((All (Cond $AE::AutomationIdProperty 'Found_Row')) | Where-Object { -not $_.Current.IsOffscreen }) }
function Row([string]$name) { @(Rows | Where-Object { $_.Current.Name -eq $name })[0] }
function Heads { @((All (Cond $AE::AutomationIdProperty 'Found_Group')) | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) }
function Target([string]$name) { @((All (Cond $AE::AutomationIdProperty 'Found_Target')) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -eq $name })[0] }
function ListUp { @(Rows).Count -gt 0 -or @(Heads).Count -gt 0 }
# A group's last line, where it found more than it lists: "See all 43 results". The first in sight, down the list.
function SeeAll { @((All (Cond $AE::AutomationIdProperty 'Found_SeeAll')) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -match '^See all \d+ results$' } | Sort-Object { $_.Current.BoundingRectangle.Y })[0] }
# A key pressed and let go, by its virtual key code: the keyboard's language does not matter.
function Tap([int]$vk) { Front; [Fast]::keybd_event($vk, 0, 0, 0); Start-Sleep -Milliseconds 40; [Fast]::keybd_event($vk, 0, 2, 0); Start-Sleep -Milliseconds 250 }
function NameAsked { (ItemSearchBox).GetCurrentPattern($script:Value).Current.Value }
# Where the right page begins: past the middle of the two pages' outer bookmarks.
function PagesMiddle { BookMiddle }

Case 'search.box-list-drops-under-the-box' -Tags search {
  TypeIn 'stone'
  $row = WaitUntil { Row 'Stone Barb' } "the box's finds" 6000
  $box = (Box).Current.BoundingRectangle; $at = $row.Current.BoundingRectangle
  Check ($at.Top -ge $box.Bottom -and $at.Left -lt $box.Right -and $at.Right -gt $box.Left) "the finds are listed under the box (the box ends at $([int]$box.Bottom), the first Stone Barb is at $([int]$at.Top))"
  # Centred on the spine between the two pages.
  $middle = $box.X + $box.Width / 2
  Check ([Math]::Abs($middle - (PagesMiddle)) -lt 40) "and is centred between the pages (its middle at $([int]$middle), theirs at $([int](PagesMiddle)))"
  TypeIn ''
}

Case 'search.click-elsewhere-puts-the-list-away-and-empties-the-box' -Tags search {
  TypeIn 'stone'
  WaitUntil { Row 'Stone Barb' } "the box's finds" 6000 | Out-Null
  ClickEl (Box) "the box (a click in it is the search's own)"
  Start-Sleep -Milliseconds 300
  Check ((ListUp) -and (Typed) -eq 'stone') "a click in the box leaves its list up and its text"
  ClickEl (ById 'SideBar_Char') "the Character page (a click elsewhere)"
  Check ([bool](WaitUntil { if (-not (ListUp) -and (Typed) -eq '') { 'away' } } "the list away, the box emptied" 3000)) "a click elsewhere puts the list away and empties the box ('$(Typed)')"
}

Case 'search.row-clicked-is-gone-to' -Tags search {
  ClickEl (ById 'SideBar_Char') "the Character page"
  TypeIn 'stone'
  ClickEl (WaitUntil { Row 'Stone Barb' } "the box's finds" 6000) "Stone Barb, the open character's"
  Check ([bool](WaitUntil { if ((ItemsPage) -and -not (ListUp)) { 'there' } } "the find gone to" 6000)) "a find clicked from another page: the Items page is shown, the list away"
  Check ((Typed) -eq '') "and the box is emptied ('$(Typed)')"
}

Case 'search.enter-goes-to-the-first-find-and-esc-closes' -Tags search, keys {
  ClickEl (ById 'SideBar_Char') "the Character page"
  ClickEl (Box) "the box"
  TypeIn 'stone'
  WaitUntil { Row 'Stone Barb' } "the box's finds" 6000 | Out-Null
  # Down and up again: the mark moves in the list, the caret stays in the box and the list stays up.
  Tap 0x28; Tap 0x26
  Check ((ListUp) -and (Typed) -eq 'stone') "Down and Up move the mark: the list stays up, the text stays"
  Tap 0x0D
  Check ([bool](WaitUntil { if ((ItemsPage) -and -not (ListUp) -and (Typed) -eq '') { 'there' } } "the first find gone to" 6000)) "Enter goes to the find marked - the first: the Items page is shown, the list away, the box emptied"
  # And the keyboard let go of: the page's keys reach it at once (the caret stayed in the box).
  Check ([bool](WaitUntil { if ((Focused).Current.AutomationId -ne 'Quick_Box') { 'let go' } } "the box let go of" 3000)) "the caret is no longer in the box"

  ClickEl (Box) "the box"
  TypeIn 'stone'
  WaitUntil { Row 'Stone Barb' } "the box's finds" 6000 | Out-Null
  Tap 0x1B
  Check ([bool](WaitUntil { if (-not (ListUp) -and (Typed) -eq '') { 'closed' } } "the search closed" 3000)) "Esc puts the list away and empties the box"
}

# The view's part of it: the row's question and its two answers, LOAD and CANCEL, are there to click. What they decide is
# proven without a window (SearchBoxTests).
Case 'search.other-character-find-asks' -Tags search -Start Home -Before {
  # Finder: PhySpearZon's save under another name, made aside - a rename works on a folder of its own.
  $made = "$pad\made"
  if (Test-Path $made) { [IO.Directory]::Delete($made, $true) }
  New-Item -ItemType Directory $made | Out-Null
  Copy-Item "$pad\saves\PhySpearZon.d2s" $made
  & $script:Cli rename "$made\PhySpearZon.d2s" Finder | Out-Null
  Copy-Item "$made\Finder.d2s" "$pad\saves"
} {
  function Asking { $a = ById 'Found_Ask'; if ($a -and -not $a.Current.IsOffscreen) { $a.Current.Name } }
  TypeIn 'stone barb'
  ClickEl (WaitUntil { Row 'Stone Barb' } "the other characters' finds" 8000) "Stone Barb, on a character that is not open"
  Check ([bool](WaitUntil { if ((Asking) -match '^Open (PhySpearZon|Finder)\?$') { 'asks' } } "the row asking" 3000)) "its row asks: '$(Asking)'"
  Check ($null -ne (ById 'Found_AskYes') -and $null -ne (ById 'Found_AskNo') -and -not (IsLoaded)) "with its two answers; nothing is loaded yet"
  $words = @('Found_AskYes', 'Found_AskNo') | ForEach-Object { ((ById $_).FindFirst($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text))).Current.Name }
  Check ($words[0] -eq 'OPEN' -and $words[1] -eq 'CANCEL') "worded for what they do: '$($words -join "', '")'"
  ClickEl (ById 'Found_AskNo') "CANCEL"
  Check ([bool](WaitUntil { if (-not (Asking) -and (Row 'Stone Barb')) { 'back' } } "the row back" 3000)) "CANCEL: the row is as it was"
  ClickEl (Row 'Stone Barb') "Stone Barb again"
  WaitUntil { if (Asking) { 'asks' } } "the row asking" 3000 | Out-Null
  ClickEl (ById 'Found_AskYes') "LOAD"
  Check ([bool](WaitUntil { if ((IsLoaded) -and (ItemsPage) -and -not (ListUp)) { 'there' } } "the character loaded, the find gone to" 15000)) "LOAD: the character is loaded and the Items page shown"
  Check ((Typed) -eq '') "and the box is emptied ('$(Typed)')"
}

Case 'search.setting-found-by-its-name' -Tags search, settings {
  TypeIn 'animations'
  Check ([bool](WaitUntil { if ((Target 'Animations') -and (Heads) -contains 'Settings') { 'found' } } "the Animations setting" 6000)) "a setting is found by its name, under Settings ($(@(Heads) -join ', '))"
  ClickEl (Target 'Animations') "the Animations setting"
  Check ([bool](WaitUntil { if ((ByName 'UI scale') -and -not (ListUp)) { 'settings' } } "the Settings page" 6000)) "and gone to: the Settings page is up, the list away"
}

Case 'search.fold-heading-found-by-its-name' -Tags search {
  TypeIn 'mercenary'
  Check ([bool](WaitUntil { if ((Target 'Mercenary') -and (Heads) -contains 'Sections') { 'found' } } "the Mercenary section" 6000)) "a fold's heading is found by its name, under Sections ($(@(Heads) -join ', '))"
  ClickEl (Target 'Mercenary') "the Mercenary section"
  Check ([bool](WaitUntil { if ((ItemsPage) -and -not (ListUp)) { 'items' } } "the Items page" 6000)) "and gone to: the Items page is up, the list away"
}

# A folder of the vault, found by its name: a row under Folders, gone to as a section is.
Case 'search.vault-folder-found-by-its-name' -Tags search, vault, folder-tiles {
  try {
    TypeIn 'grand'
    Check ([bool](WaitUntil { if ((Target 'Grand') -and (Heads) -contains 'Folders') { 'found' } } "the folder Grand" 6000)) "a vault folder is found by its name, under Folders ($(@(Heads) -join ', '))"
    ClickEl (Target 'Grand') "the folder Grand"
    Check ([bool](WaitUntil { if ((VaultShown) -eq 'Vault\Charms\Grand' -and -not (ListUp)) { 'there' } } "Grand shown" 6000)) "and gone to: the vault shows it, the list away ('$(VaultShown)')"
  }
  finally {
    # The Items page's left page as the cases after this one find it.
    try { VaultGoTo 'Vault' } catch { }
    try { ClickEl (ById 'Bookmark_Inventory') "inventory (left)" } catch { }
  }
}

Case 'search.see-all-line-opens-the-search-page' -Tags search {
  # A letter the character holds more of than the box lists.
  TypeIn 'a'
  $line = WaitUntil { SeeAll } "the character's See all line" 8000
  $said = $line.Current.Name
  $box = (Box).Current.BoundingRectangle; $at = $line.Current.BoundingRectangle
  Check ($said -match '^See all (\d+) results$' -and [int]$Matches[1] -gt 10 -and $at.Left -lt $box.Right -and $at.Right -gt $box.Left) "the group ends in the way to all of them: '$said'"
  ClickEl $line $said
  Check ([bool](WaitUntil { if ((ItemSearchCount) -match '^\d+ found$' -and (ItemSearchRows).Count -gt 0) { 'up' } } "the Search page with its finds" 8000)) "clicked: the Search page is up and lists what it found ('$(ItemSearchCount)')"
  Check ((NameAsked) -eq 'a' -and (Typed) -eq '' -and -not (SeeAll)) "what was typed in the box is the page's keyword; the box is empty and its list away"
  $name = (ById 'Search_Keyword').Current.BoundingRectangle
  $row = (ItemSearchRows)[0].Current.BoundingRectangle
  Check ($name.X -gt (PagesMiddle) -and $row.X -gt (PagesMiddle) -and $name.Bottom -lt $row.Top) "on the right page, its conditions over its results"
  ItemSearchEnd
}

Case 'search.see-all-line-of-a-grail-tab-opens-that-tab' -Tags search, grail {
  # A set of six pieces: more than a tab lists, and a list short enough for its last line to be in sight - the
  # list makes only the rows its room shows.
  $words = 'immortal king'
  TypeIn $words
  $line = WaitUntil { SeeAll } "a grail tab's See all line" 8000
  $said = $line.Current.Name
  $null = $said -match '^See all (\d+) results$'; $count = $Matches[1]
  ClickEl $line $said
  Check ([bool](WaitUntil { if ((ById 'Grail_Found_Sets') -and (ById 'Grail_Found_Sets').Current.Name -eq $count) { 'up' } } "the grail, its Sets tab counting $count" 8000)) "clicked: the grail is up, and the set pieces' tab says the number the line did ($count)"
  Check ((Typed) -eq '') "the box is emptied"
  # The number is drawn on its tab, on the name's line; under it the tab's found line, which is always there.
  $tab = (ById 'Grail_Tab_Sets').Current.BoundingRectangle; $found = (ById 'Grail_Found_Sets').Current.BoundingRectangle
  Check ($found.Left -gt $tab.Left -and $found.Right -lt $tab.Right -and $found.Top -ge $tab.Top -and $found.Bottom -le $tab.Bottom) "the number is on its tab"
  $progress = ById 'Grail_Progress_Sets'
  Check ($progress.Current.Name -match '^\d+ / \d+$' -and $progress.Current.BoundingRectangle.Top -ge $found.Bottom - 2 -and $progress.Current.BoundingRectangle.Bottom -le $tab.Bottom) "under it the tab says how many are found: '$($progress.Current.Name)'"
  Check ((ById 'Grail_Found_Uniques').Current.Name -match '^\d+$') "the other tab says how many it holds (uniques $((ById 'Grail_Found_Uniques').Current.Name))"
  # The tabs are a row over the list, each as wide as the next (two).
  $u = (ById 'Grail_Tab_Uniques').Current.BoundingRectangle
  Check ([Math]::Abs($u.Top - $tab.Top) -lt 2 -and $u.Right -lt $tab.Left -and [Math]::Abs($u.Width - $tab.Width) -lt 2) "the two tabs stand in a row, alike in width"
  # What was typed is in the grail's own box; emptied, the numbers go.
  $search = @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.AutomationId -ne 'Quick_Box' -and $_.Current.BoundingRectangle.X -gt (PagesMiddle) })[0]
  Check ($search.GetCurrentPattern($script:Value).Current.Value -eq $words) "what was typed is in the grail's own box"
  $search.GetCurrentPattern($script:Value).SetValue('')
  Check ([bool](WaitUntil { if (-not (ById 'Grail_Found_Sets')) { 'gone' } } "the numbers gone" 4000)) "the grail's box emptied: the tabs say no number"
  Check ((ById 'Grail_Progress_Sets').Current.Name -match '^\d+ / \d+$') "and their found lines stay ('$((ById 'Grail_Progress_Sets').Current.Name)')"
  # The grail as the cases after expect it: its first tab.
  ClickEl (ById 'Grail_Tab_Uniques') "UNIQUES"
}

# The form's Family box: there, and dim until the rarity is Crafted. What it asks is proven without a window
# (SearchFamilyTests).
Case 'search.family-box-waits-for-crafted' -Tags search {
  function Line([string]$name) { @((All (Cond $AE::ControlTypeProperty $CT::ListItem)) | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen })[0] }
  ItemSearch
  $family = WaitUntil { ById 'Search_Family' } "the Family box" 4000
  $rarity = (ById 'Search_Rarity').Current.BoundingRectangle; $at = $family.Current.BoundingRectangle
  Check (-not $family.Current.IsEnabled) "no rarity chosen: the Family box is there, and dim"
  Check ([Math]::Abs($at.Top - $rarity.Top) -lt 2 -and $at.Left -gt $rarity.Right) "beside the Rarity box"
  ClickEl (ById 'Search_Rarity') "the Rarity box"
  ClickEl (WaitUntil { Line 'Crafted' } "the line Crafted" 3000) "Crafted"
  Check ([bool](WaitUntil { if ((ById 'Search_Family').Current.IsEnabled) { 'lit' } } "the Family box lit" 3000)) "Crafted picked: the Family box is lit"
  # Crafted is a condition, and its results come a moment later - the conditions give them room then, and the
  # Family box lies elsewhere: waited for, so the click is on the box where it stands.
  WaitUntil { if ((ItemSearchCount) -match '^\d+ found$') { 'asked' } } "Crafted searched for" 6000 | Out-Null
  Start-Sleep -Milliseconds 400
  ClickEl (ById 'Search_Family') "the Family box"
  Check ([bool](WaitUntil { if ((Line 'Blood') -and (Line 'Brilliant')) { 'listed' } } "the families listed" 3000)) "it lists the craft families"
  ClickEl (Line 'Blood') "Blood"
  Check ([bool](WaitUntil { if ((ItemSearchCount) -match '^\d+ found$') { 'asked' } } "the search asked" 6000)) "a family picked is searched for ('$(ItemSearchCount)')"
  ItemSearchEnd
  Check (-not (ById 'Search_Family').Current.IsEnabled) "the brush empties it: dim again"
}

Case 'search.page-takes-no-width-from-the-pages' -Tags search, layout {
  $was = @{ Left = [int](ById 'Bookmark_Inventory').Current.BoundingRectangle.X; Right = [int](ById 'Bookmark_Edit').Current.BoundingRectangle.X }
  $tool = [int](ItemsPage).Current.BoundingRectangle.X
  ItemSearchFor 'shael'
  $now = @{ Left = [int](ById 'Bookmark_Inventory').Current.BoundingRectangle.X; Right = [int](ById 'Bookmark_Edit').Current.BoundingRectangle.X }
  Check ($now.Left -eq $was.Left -and $now.Right -eq $was.Right) "with the search up and its finds listed the two pages are where they were (bookmarks at $($now.Left) and $($now.Right))"
  Check ([int](ItemsPage).Current.BoundingRectangle.X -eq $tool) "and the inventory's tools have not moved"
  ItemSearchEnd
}

Case 'search.conditions-have-the-page-until-something-is-found' -Tags search, layout {
  ItemSearch
  Check ($null -eq (ById 'Search_Found') -and (ItemSearchRows).Count -eq 0 -and -not (ItemSearchCount)) "nothing asked: no list of results, nothing counted"
  # The form's last heading is in sight without scrolling: the conditions have the page's height.
  $bottom = $script:win.Current.BoundingRectangle.Bottom
  $properties = ByName 'Properties'
  Check ($null -ne $properties) "the conditions run down the page: Properties, the form's last section, is in sight"
  ItemSearchFor 'shael'
  $name = (ById 'Search_Keyword').Current.BoundingRectangle
  $row = (ItemSearchRows)[0].Current.BoundingRectangle
  Check ($null -ne (ById 'Search_Found') -and $row.Top -gt $name.Bottom) "something found: the results are listed under the conditions ('$(ItemSearchCount)')"
  $list = (ById 'Search_Found').Current.BoundingRectangle
  Check ($list.Height -gt ($bottom - $list.Top) * 0.5) "and they have the rest of the page ($([int]$list.Height) tall)"
  # What is asked is said on the Conditions heading, and the finds' tools stand on the Results heading.
  Check ((ById 'Search_Summary').Current.Name -match 'shael') "the Conditions heading says what is asked: '$((ById 'Search_Summary').Current.Name)'"
  $heading = (ById 'Found_Results').Current.BoundingRectangle; $tool = (ById 'Found_Select').Current.BoundingRectangle
  Check ($tool.Left -gt $heading.Right - 2 -and $tool.Top -lt $heading.Bottom -and $tool.Bottom -gt $heading.Top -and (ById 'Found_Move').Current.BoundingRectangle.Left -gt $tool.Right - 2) "Select found and Move found stand at the Results heading's end"
  # 40% conditions, 60% results: the conditions' fields are taller than their share,
  # so they have just that - measured from the conditions' top down to the Results heading, against the page.
  $top = (ById 'Found_Conditions').Current.BoundingRectangle.Top - 8
  $share = ((ById 'Found_Results').Current.BoundingRectangle.Top - $top) / ($list.Bottom - $top)
  Check ($share -gt 0.30 -and $share -lt 0.44) "the conditions have their share of the page and no more ($([int]($share * 100))% of it; 40% is theirs)"
  ItemSearchEnd
}

Case 'search.results-fold-by-their-heading' -Tags search, layout {
  ItemSearchFor 'shael'
  # As wide as its row, as the Conditions heading is: a click anywhere along it folds.
  $heading = (ById 'Found_Results').Current.BoundingRectangle; $list = (ById 'Search_Found').Current.BoundingRectangle
  Check ($heading.Width -gt $list.Width * 0.8) "the Results heading is as wide as its row, less the two tools at its end ($([int]$heading.Width) of $([int]$list.Width))"
  ClickEl (ById 'Found_Results') "the Results heading"
  Check ([bool](WaitUntil { if (-not (ById 'Search_Found') -and (ItemSearchRows).Count -eq 0) { 'folded' } } "the results folded" 4000)) "the Results heading folds the list away"
  Check ((ItemSearchCount) -match '^\d+ found$' -and $null -ne (ById 'Found_Results')) "the search stands: '$(ItemSearchCount)', its heading still there"
  ClickEl (ById 'Found_Results') "the Results heading again"
  Check ([bool](WaitUntil { if ((ItemSearchRows).Count -gt 0) { 'listed' } } "the results again" 4000)) "and unfolds it again"
  ItemSearchEnd
}

Case 'search.ctrl-enter-takes-what-is-typed-to-the-page' -Tags search, keys {
  TypeIn 'shael'
  WaitUntil { Row 'Stone Barb' } "the box's finds" 6000 | Out-Null
  ClickEl (Box) "the box"
  Front; [Fast]::keybd_event(0x11, 0, 0, 0); [Fast]::keybd_event(0x0D, 0, 0, 0); [Fast]::keybd_event(0x0D, 0, 2, 0); [Fast]::keybd_event(0x11, 0, 2, 0)
  Check ([bool](WaitUntil { if ((ItemSearchCount) -match '^\d+ found$' -and (ItemSearchRows 'Stone Barb').Count -gt 0) { 'asked' } } "the Search page" 8000)) "Ctrl + Enter in the box: the Search page asks it ('$(ItemSearchCount)')"
  Check ((NameAsked) -eq 'shael' -and (Typed) -eq '') "as its keyword, the box emptied"
  Check ([bool](WaitUntil { if ((ItemSearchBox).Current.HasKeyboardFocus) { 'in' } } "the caret in the keyword field" 3000)) "and the caret is in the page's keyword field"
  ItemSearchEnd
}

Case 'search.f3-steps-through-the-pages-results' -Tags search, keys {
  ItemSearchFor 'shael'
  # The keyboard off the form: F3 is the page's from anywhere, and a field would take Esc and the letters.
  ClickEl (ById 'Found_Results') "the Results heading (folds)"; ClickEl (ById 'Found_Results') "the Results heading (unfolds)"
  WaitUntil { if ((ItemSearchRows).Count -gt 0) { 'listed' } } "the results again" 4000 | Out-Null
  $first = (ItemSearchRows)[0].Current.Name
  Tap 0x72
  Check ((ItemSearchCount) -match '^\d+ found$' -and $null -ne (ById 'Found_Conditions')) "F3: the Search page stays as it is ('$(ItemSearchCount)')"
  # What F3 went to is the selected item: Edit, taken now, shows it.
  ClickEl (ById 'Bookmark_Edit') "Edit"
  Check ([bool](WaitUntil { if (@((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -eq $first -and $_.Current.BoundingRectangle.X -gt (PagesMiddle) }).Count) { 'shown' } } "the first result on the Edit page" 4000)) "and its first result was gone to: Edit shows $first"
  ItemSearchEnd
}

# In a window at its least size, where the left page is twice as tall as its room: the worn armor is scrolled out
# of sight first - a case on a page that happened to show the find already could not fail. From the program as
# it opens, the window sized before the character is loaded; at its end the window is shown maximized again.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SearchWindowSize {
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int t, bool repaint);
}
"@ -ErrorAction SilentlyContinue

# Whether an item's tile lies whole inside the left page's scroll: by where it is drawn - a tile scrolled away
# is still reported as on screen.
function WholeInSight([string]$name) {
  $view = (ById 'Items_Scroll').Current.BoundingRectangle
  $named = New-Object System.Windows.Automation.AndCondition((Cond $AE::AutomationIdProperty 'Tile'), (Cond $AE::NameProperty $name))
  foreach ($tile in (All $named)) {
    $b = $tile.Current.BoundingRectangle
    if ($b.Width -gt 0 -and $b.X -lt $view.Right -and $b.Top -ge $view.Top - 1 -and $b.Bottom -le $view.Bottom + 1) { return $true }
  }
  $false
}

Case 'search.find-clicked-is-brought-into-sight' -Tags search, items, layout -Start Home {
  try {
    $scale = [SearchWindowSize]::GetDpiForWindow($script:proc.MainWindowHandle) / 96.0
    # Shown as it stands when it is brought to the front, not maximized (fast.ps1's Front).
    $script:show = 5
    [Fast]::ShowWindow($script:proc.MainWindowHandle, 9) | Out-Null; Start-Sleep -Milliseconds 300
    [SearchWindowSize]::MoveWindow($script:proc.MainWindowHandle, 80, 40, ([int](1280 * $scale)), ([int](760 * $scale)), $true) | Out-Null
    Start-Sleep -Milliseconds 700
    Front
    ClickEl (WaitUntil { ByName 'Open' } "Load") "Load"
    WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "character loaded" | Out-Null
    ClickEl (ById 'SideBar_Inventory') "Items"
    WaitUntil { ById 'Bookmark_Edit' } "items page" | Out-Null
    Start-Sleep -Milliseconds 600
    $script:r = $script:win.Current.BoundingRectangle

    $piece = "Duriel's Shell"
    ItemSearchFor 'duriel'
    $row = (ItemSearchRows $piece)[0]
    Check ($null -ne $row) "the find is listed: $piece"
    $scroll = (ById 'Items_Scroll').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    $scroll.SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 500
    Check (-not (WholeInSight $piece)) "the left page scrolled to its end: the worn armor is out of sight"
    ClickEl $row "the find, in the Search page's list"
    Check ([bool](WaitUntil { if (WholeInSight $piece) { 'seen' } } "the item in sight" 4000)) "a find clicked: the left page is scrolled to it (now at $([int]$scroll.Current.VerticalScrollPercent)%)"
    ItemSearchEnd
  }
  finally {
    # Maximized again the next time the window is brought to the front.
    $script:show = 3
  }
}
