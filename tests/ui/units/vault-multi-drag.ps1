# Several vault cards picked, as only a window shows it (multi drag and drop was lacking from the
# vault back to the inventory: the press on a picked card made it the selection alone before the drag began): that a
# press on one of several picked is told apart - let go without a drag it is a click, which selects that one
# alone; dragged, it carries them all onto the inventory.
#
# What the page decides - what a carry begun on a picked card takes with it, what the click makes of the picks,
# where both land - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Vault/VaultMultiDragTests.
#
# The vault on the right page, in Charms' folder Small.

$saves = "$pad\saves"
function Stamp($file) { (WrittenAt "$saves\$file") }
function Held { if ((& $script:Cli dump "$saves\PhySpearZon.d2s" 2>&1 | Out-String) -match 'items \((\d+)\)') { [int]$Matches[1] } }
# A vault item: its tile on the folder's pages.
function Card($name) { VaultTile $name }
function CtrlClick($el, [string]$what) {
  if ($null -eq $el) { throw "not found, not clicked: $what" }
  Front; $b = $el.Current.BoundingRectangle
  [Fast]::keybd_event(0x11, 0, 0, 0); [Fast]::Click([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)); [Fast]::keybd_event(0x11, 0, 2, 0)
}
function Selected { if ($t = ById 'Vault_Count') { $t.Current.Name } }   # "2 items - 2 selected", on the path's row
# The vault on the right page, in Small, its two charms picked.
function PickBoth {
  ClickEl (WaitUntil { ById 'Bookmark_Vault_Right' } "vault bookmark (right)") "vault (right)"
  WaitUntil { VaultPages } "Charms folder" | Out-Null
  VaultGoTo 'Small'; WaitUntil { Card 'Small Charm of Fortune' } "Small opened" | Out-Null
  CtrlClick (Card 'Small Charm of Fortune') "fortune"
  ClickUntil { if ($c = Card 'Garnet Small Charm of Life') { CtrlClick $c "garnet" } } { (Selected) -match '2 selected' } "two picked" 800 | Out-Null
}

Case 'vault-multi-drag.plain-click-on-a-picked-card-selects-it-alone' -Tags vault, selection {
  PickBoth
  ClickEl (Card 'Small Charm of Fortune') "plain click on a picked card"
  Check ([bool](WaitUntil { (Selected) -notmatch '2 selected' } "the pick collapsed" 3000)) "a plain click on a picked card selects it alone"

  # Back at the vault's top, as the next case expects to find the page.
  VaultGoTo 'Vault'
}

Case 'vault-multi-drag.picked-cards-dragged-by-one-all-come-out' -Tags vault, drag -Mode Casual -Changes {
  $count = VaultCount; $held = Held
  # An empty inventory cell: right of the Horadric Cube's top row, read off its tile. The inventory in
  # the first pane, whatever the pad was left showing.
  Pane 0 'Inventory'
  $cell = CellOf (WaitUntil { Tile 'Horadric Cube' } "the cube") 2 0 2 2
  PickBoth

  $was = Stamp 'PhySpearZon.d2s'
  DragTo (Card 'Small Charm of Fortune') $cell
  WaitUntil { (Stamp 'PhySpearZon.d2s') -ne $was } "character written" 10000 | Out-Null
  WaitUntil { (VaultCount) -eq $count - 2 } "two rows gone from the vault" 10000 | Out-Null
  Check ((Held) -eq $held + 2) "the character holds both ($held -> $($held + 2))"
}
