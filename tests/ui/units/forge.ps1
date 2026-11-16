# The Forge, as only a window shows it: that its bookmark is there in
# Free and not in Casual, what the empty holder says, that a click reaches a find, a box's line, the Superior
# line's picker and FORGE, that the four boxes draw what was chosen - one find after another, each box following -
# that a number typed in a box of the page reaches the holder, that the search section's heading folds it, and
# that the list of finds, scrolled to its end, asks for the rest.
#
# What the Forge decides - what a find fills the boxes with and what is made, forty finds at a time, a ring never
# Normal, a runeword's bases, the superior bonus and what it does to a runeword's base, where FORGE sends the item
# and what the strip says, the buttons this page does not offer - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Forge/ForgePageTests.
#
# The Forge keeps what its holder has for as long as the program runs, so every case here chooses its own find
# first - a find chosen is made afresh - and only the one about the empty holder starts the program for itself.

function RightEdit { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt (BookMiddle) })[0] }
function Find([string]$text) { (RightEdit).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); Start-Sleep -Milliseconds 400 }
# One of the search section's four boxes, by its name; what it shows; and a line of it chosen.
function Box([string]$name) { @((All (Cond $AE::ControlTypeProperty $CT::ComboBox)) | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen })[0] }
function Shown([string]$box) {
  # The closed box draws what is chosen as a text of its own; a pattern, where the box gives one, says the same.
  $it = Box $box
  $words = @($it.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
  if ($words.Count -gt 0) { return $words[0] }
  $pattern = $null
  if ($it.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) { return $pattern.Current.Value }
  ''
}
function Choose([string]$box, [string]$line) {
  ClickEl (Box $box) "the $box box"
  ClickEl (WaitUntil { @((All (Cond $AE::ControlTypeProperty $CT::ListItem)) | Where-Object { $_.Current.Name -eq $line -and -not $_.Current.IsOffscreen })[0] } "the line '$line'") $line
  Start-Sleep -Milliseconds 400
}
# A find of the search, by its name: the button its text sits in - the item's own name in the holder sits in none.
function Found([string]$name) {
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  foreach ($text in (All (Cond $AE::NameProperty $name))) {
    if ($text.Current.IsOffscreen) { continue }
    $parent = $walker.GetParent($text)
    # The name is the row's first text; its base, beside it, is the second - a Pike is not the unique made on one.
    if ($parent -and $parent.Current.ControlType -eq $CT::Button -and $walker.GetFirstChild($parent).Current.Name -eq $name) { return $parent }
  }
}
# The number boxes of the right page; the one holding a value.
function RightEdits { @((All (Cond $AE::ControlTypeProperty $CT::Edit)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.X -gt (BookMiddle) }) }
function BoxWith([string]$value) { @(RightEdits | Where-Object { $_.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $value })[0] }
function Texts([string]$like) { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { $_.Current.Name -like $like -and -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.Name }) }
function Said([string]$text) { [bool](WaitUntil { ByName $text } "the strip: $text" 5000) }

# The Forge taken on the right page: its bookmark, then FORGE in the holder's corner.
function OpenForge {
  ClickEl (WaitUntil { ById 'Bookmark_Forge' } "the Forge bookmark") "Forge"
  WaitUntil { ById 'Forge_Make' } "the Forge" | Out-Null
  # And its search box: with an item in the holder the page is a moment in building, and FORGE is there before
  # the boxes are - a case that typed its find at once found no box to type in (two cases, each
  # passing in a program of its own).
  WaitUntil { ById 'Forge_Search' } "the Forge's search box" | Out-Null
}
# A find chosen: typed in the search box, and its row pressed.
function Pick([string]$typed, [string]$name) {
  Find $typed
  InvokeEl (WaitUntil { Found $name } "$name among the finds") $name
}

Case 'forge.casual-has-no-bookmark' -Tags forge, play-mode -Mode Casual {
  Check ($null -ne (ById 'Bookmark_Edit') -and $null -eq (ById 'Bookmark_Forge')) "Casual: no Forge bookmark beside Edit's"
}

# The program as it opens, so the Forge has held nothing yet.
Case 'forge.opens-empty-and-forge-waits' -Tags forge -Start Home {
  ClickEl (WaitUntil { ByName 'Open' } "Load") "Load"
  WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "character loaded" | Out-Null
  ClickEl (ById 'SideBar_Inventory') "Items"
  WaitUntil { ItemsPage } "items page" | Out-Null
  ClickEl (WaitUntil { ById 'Bookmark_Forge' } "the Forge bookmark") "Forge"
  Check ($null -ne (WaitUntil { ByName 'Choose what to forge below.' } "the empty holder")) "Free: the Forge opens with nothing in its holder"
  Check (-not (ById 'Forge_Make').Current.IsEnabled) "and FORGE waits for something to forge"
}

# A search finding more than forty lists forty, says so, and the next forty join as the list is scrolled to its
# end - no button (Show all is bad design).
Case 'forge.list-scrolled-to-its-end-loads-the-rest' -Tags forge {
  OpenForge
  Find 'bow'
  $capped = WaitUntil { @(Texts '* of *') | Where-Object { $_ -match '^40 of \d+$' } | Select-Object -First 1 } "the count says 40 of more"
  $total = ($capped -replace '^40 of ', '')
  # Over the list: just under the search box.
  $b = (RightEdit).Current.BoundingRectangle
  Front; [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height + 60)) | Out-Null
  for ($i = 0; $i -lt 40 -and -not @(Texts $total).Count; $i++) { [Fast]::mouse_event(0x0800, 0, 0, -360, 0); Start-Sleep -Milliseconds 120 }
  Check (@(Texts $total).Count -gt 0) "'bow': 40 listed of $total, all $total once scrolled to the end"
  Find ''
}

Case 'forge.find-clicked-fills-the-boxes' -Tags forge {
  OpenForge
  Pick 'war trav' 'War Traveler'
  Check ($null -ne (WaitUntil { ByName 'Adds 15-25 damage' } "the item's lines")) "a unique picked by name is made and shown"
  Check ((Shown 'Type') -eq 'Boots' -and (Shown 'Base') -eq 'Battle Boots' -and (Shown 'Rarity') -eq 'Unique') "the find fills the boxes: $(Shown 'Type') / $(Shown 'Base') / $(Shown 'Rarity')"
  Check ((Shown 'Item') -like 'War Traveler*') "and the item: $(Shown 'Item')"

  # One find after another: each box follows, the name's too.
  Pick 'pike' 'Pike'
  Check ($null -ne (WaitUntil { ByName 'Two-Hand Damage' -Prefix } "the Pike's damage")) "a base picked is made plain"
  Check ((Shown 'Type') -eq 'Weapons: Spears' -and (Shown 'Base') -eq 'Pike' -and (Shown 'Rarity') -eq 'Normal') "its boxes: $(Shown 'Type') / $(Shown 'Base') / $(Shown 'Rarity')"
  Pick 'tannr' 'The Tannr Gorerod'
  WaitUntil { if ((Shown 'Rarity') -eq 'Unique') { 'unique' } } "the Rarity box at Unique" 4000 | Out-Null
  Check ((Shown 'Rarity') -eq 'Unique' -and (Shown 'Item') -like 'The Tannr Gorerod*') "a third find fills them again: $(Shown 'Rarity') / $(Shown 'Item')"
}

# Before anything is typed lower down: a box written to scrolls itself into sight, and the heading out of it.
Case 'forge.search-section-folds' -Tags forge, folds {
  OpenForge
  $type = WaitUntil { Box 'Type' } "the search section's boxes"
  # The page at its top first: a case before this one that wrote to a box lower down left it scrolled, the
  # heading just under the item's holder - where UI Automation still says it is in sight, and the click fell on
  # the holder (seen in a picture: the case passed only in a program of its own).
  $b = $type.Current.BoundingRectangle
  Front; [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 150
  1..12 | ForEach-Object { [Fast]::mouse_event(0x0800, 0, 0, 120, 0); Start-Sleep -Milliseconds 40 }
  Start-Sleep -Milliseconds 300
  ClickEl (Found 'Search') "fold the search"
  $folded = $false
  try { WaitUntil { -not (Box 'Type') } "the boxes folded away" 4000 | Out-Null; $folded = $true } catch { }
  Check $folded "the search section folds"
  ClickEl (Found 'Search') "unfold the search"
  Check ($null -ne (WaitUntil { Box 'Type' } "the boxes back")) "and unfolds: its boxes are back"
}

# Nothing waits on KEEP: a number typed is the item's at once, and the holder says so.
Case 'forge.number-typed-shows-in-the-holder' -Tags forge {
  OpenForge
  Pick 'war trav' 'War Traveler'
  WaitUntil { (Texts 'Defense: *') -contains 'Defense: 139' } "the boots as made" | Out-Null
  Check ((Texts 'Defense: *') -contains 'Defense: 139') "the boots as made: $(Texts 'Defense: *')"
  (BoxWith '190').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('160')
  $follows = $false
  try { WaitUntil { (Texts 'Defense: *') -contains 'Defense: 124' } "the holder's defense at 160%" 4000 | Out-Null; $follows = $true } catch { }
  Check $follows "Enhanced Defense typed to 160: the holder follows ($(Texts 'Defense: *'))"
  Check ((Texts '(pending*').Count -eq 0 -and (ById 'Forge_Make').Current.IsEnabled) "and nothing reads as pending"
}

Case 'forge.rarity-box-remakes-the-item' -Tags forge {
  OpenForge
  Pick 'tannr' 'The Tannr Gorerod'
  WaitUntil { if ((Shown 'Rarity') -eq 'Unique') { 'unique' } } "the Rarity box at Unique" 4000 | Out-Null
  Choose 'Rarity' 'Rare'
  # Rolled as a drop, every number at its top: a rare at 99 carries six affixes' properties.
  $props = WaitUntil { @(Texts 'Properties*') | Where-Object { $_ -match '\((\d+)\)' -and [int]$Matches[1] -ge 6 } | Select-Object -First 1 } "the rolled properties"
  Check ($null -ne (ByName 'Affixes' -Prefix) -and $null -ne $props) "made rare, rolled as a drop, its affixes the panel's to change: '$props'"
  Check (-not (Box 'Item').Current.IsEnabled) "a rare names no item: the Item box is off"
}

# The Superior line under Affixes opens the rows the base can carry, and the one picked is on the item at once.
Case 'forge.superior-line-picks-a-bonus' -Tags forge {
  OpenForge
  Pick 'sacred armor' 'Sacred Armor'
  WaitUntil { (Texts 'Defense: *') -contains 'Defense: 730' } "the plain armour" | Out-Null
  Choose 'Rarity' 'Superior'
  $line = WaitUntil { ById 'Superior_Bonus' } "the Superior line"
  Check ($line.Current.Name -eq 'None' -and (Texts 'Defense: *') -contains 'Defense: 730') "a superior base is made with no bonus: '$($line.Current.Name)', $(Texts 'Defense: *')"
  InvokeEl $line "the Superior line"
  $rows = WaitUntil { $found = Texts '*Enhanced Defense*'; if ($found.Count -ge 2) { $found } } "the picker's rows"
  Check (($rows -contains '+10-20% Enhanced Defense') -and ($rows -contains '+5-15% Enhanced Defense') -and (Texts 'Increase Maximum Durability 15-25%').Count -gt 0) "the picker lists the armour's three rows"
  Check (-not (ByTip 'Remove bonus').Current.IsEnabled) "the brush at the top, dimmed with no bonus to take off"
  InvokeEl (@((All (Cond $AE::NameProperty '+10-20% Enhanced Defense')) | Where-Object { -not $_.Current.IsOffscreen })[0]) "the Enhanced Defense row"
  $worn = $false
  try { WaitUntil { (Texts 'Defense: *') -contains 'Defense: 877' } "the defence with the bonus" 4000 | Out-Null; $worn = $true } catch { }
  Check ($worn -and (Texts '+20% Enhanced Defense*').Count -gt 0) "the row picked is on the item, at its top, over the base's top defence plus one ($(Texts 'Defense: *'))"
  Check ((ById 'Superior_Bonus').Current.Name -eq '+10-20% Enhanced Defense') "and the line names it: '$((ById 'Superior_Bonus').Current.Name)'"
  Check ([bool](WaitUntil { $b = ByTip 'Remove bonus'; if ($b -and $b.Current.IsEnabled) { 'lit' } } "the brush lit" 3000)) "with a bonus on, the brush can take it off"
}

Case 'forge.forge-lands-the-item' -Tags forge -Changes {
  OpenForge
  Pick 'war trav' 'War Traveler'
  WaitUntil { ByName 'Adds 15-25 damage' } "the item's lines" | Out-Null
  ClickEl (ById 'Forge_Make') "FORGE"
  Check (Said 'War Traveler forged to inventory.') "FORGE puts it in the inventory, and the strip says so"
  Check ($null -ne (Tile 'War Traveler')) "the boots are among the character's items"
  Check ($null -ne (ByName 'Adds 15-25 damage') -and (ById 'Forge_Make').Current.IsEnabled) "and the holder keeps one to forge again"
}
