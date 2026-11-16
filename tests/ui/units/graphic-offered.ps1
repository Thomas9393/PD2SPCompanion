# The Edit page's Graphic box, as only a window shows it (it was refused to every unique
# and set item): drawn for an item whose picture is picked by the item's own number, and not for one whose row
# names its picture.
#
# Which items are offered it - a magic ring, a unique ring, a set amulet, a unique jewel; not Annihilus - is
# proven without a window: tests/PD2SPCompanion.Shell.Tests/Window/GraphicBoxTests.
#
# Nothing is changed: items are clicked and the first pane's container picked, and put back on the inventory.

function Offered([string]$name) {
  ClickUntil { ClickEl (WaitUntil { Tile $name } "the tile of $name") $name } { OnEdit "$name" } "$name on the Edit page" | Out-Null
  Start-Sleep -Milliseconds 300
  [bool](ByName 'Graphic')
}

Case 'graphic-offered.box-drawn-for-a-unique-ring-and-not-for-annihilus' -Tags window, items, edit {
  Pane 0 'Personal Stash'
  Check (Offered 'Skull Grip') "a unique ring has it"
  Pane 0 'Inventory'
  Check (-not (Offered 'Annihilus')) "Annihilus, drawn from its own row, has none"
}
