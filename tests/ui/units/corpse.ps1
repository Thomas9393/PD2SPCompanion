# The corpse, as only a window shows it: a character that died and left the game without
# taking its body up has a Corpse panel over its worn slots - what it wore, a box an item - and the button there
# takes all of it back on. On a real save, a level 86 Amazon with twelve items on its corpse
# (TestCorpus, specimens/corpse-twelve-worn-items-s13.d2s), laid in the pad for the case.
#
# What the session decides - the items the corpse's until taken, none lifted, stored or deleted off it, all of them
# back in their slots and the corpse gone from the save - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Inventory/CorpseTests.

function Card($name) { foreach ($c in (All (Cond $AE::ControlTypeProperty $CT::ListItem))) { if (-not $c.Current.IsOffscreen -and $c.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $c } } }
function Open($name) {
  ClickUntil { if ($c = Card $name) { ClickEl $c "$name's card" } } { ByName 'Open' } "$name chosen" 800 | Out-Null
  ClickEl (ByName 'Open') "Load"
  WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "$name loaded" | Out-Null
  ClickEl (ById 'SideBar_Inventory') "Items"
  WaitUntil { ItemsPage } "items page" | Out-Null
}
function Says([string]$text) { try { [bool](WaitUntil { ByName $text } "the strip: $text" 4000) } catch { $false } }
$script:LayCorpse = {
  $corpus = $script:Corpus
  Remove-Item "$pad\saves\LightFury.*" -ErrorAction SilentlyContinue
  Copy-Item "$corpus\specimens\corpse-twelve-worn-items-s13.d2s" "$pad\saves\LightFury.d2s" -Force
}

Case 'corpse.panel-shows-what-lies-on-it-and-its-button-takes-it-back' -Tags items, corpse -Pad card -Start Home -Changes -Before $script:LayCorpse {
  Open 'LightFury'

  $detail = WaitUntil { ById 'Corpse_Detail' } "the corpse's panel" 5000
  Check ($detail.Current.Name -eq '12 items on the corpse') "the Corpse panel says what lies on it ('$($detail.Current.Name)')"
  $take = WaitUntil { ById 'Corpse_Take' } "the corpse's button" 3000
  Check ($take.Current.HelpText -eq 'Take all from corpse') "and has its button, Take all from corpse ('$($take.Current.HelpText)')"
  if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'corpse-before.png') }

  ClickEl $take "Take all from corpse"
  Check (Says 'Corpse: 12 items put back on.') "taken: 'Corpse: 12 items put back on.'"
  Check ([bool](WaitUntil { if (-not (ById 'Corpse_Take')) { 'gone' } } "the panel gone" 4000)) "and the Corpse panel is gone"
  if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'corpse-after.png') }
}

# One way, out (every action allowed, no item in):
# an item on the corpse is dragged off it as any item is dragged - here onto the Cube's row of the pages' list -
# and the corpse has one fewer.
Case 'corpse.an-item-is-dragged-off-the-corpse' -Tags items, corpse, drag -Pad card -Start Home -Changes -Before $script:LayCorpse {
  Open 'LightFury'

  $detail = WaitUntil { ById 'Corpse_Detail' } "the corpse's panel" 5000
  # The grid waited for as the panel is: a character's pages are laid out in the moments after it opens,
  # and the panel's line was in sight a moment before its grid - asked for at once, there was none.
  $grid = WaitUntil { ById 'Corpse_Grid' } "the corpse's grid" 5000
  $lying = @(WaitUntil { $tiles = @($grid.FindAll($TS::Descendants, (Cond $AE::AutomationIdProperty 'Tile'))); if ($tiles.Count -ge 12) { $tiles } } "the corpse's items drawn" 5000)
  Check ($lying.Count -eq 12) "twelve items drawn in the corpse's grid ($($lying.Count))"

  $one = @($lying | Sort-Object { $_.Current.BoundingRectangle.Width * $_.Current.BoundingRectangle.Height })[0]
  $name = $one.Current.Name
  DragTo $one (PageRow 'Cube') -Hold 700
  Check ([bool](WaitUntil { if ((ById 'Corpse_Detail').Current.Name -eq '11 items on the corpse') { 'out' } } "eleven left" 5000)) "$name dragged onto the Cube's row: eleven left on the corpse"
  $left = @((ById 'Corpse_Grid').FindAll($TS::Descendants, (Cond $AE::AutomationIdProperty 'Tile')))
  Check ($left.Count -eq 11) "and eleven drawn ($($left.Count))"
}
