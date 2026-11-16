# The socket guard's button and a jewel's layer, as only a window shows them: the guard's
# button beside the one that shows the sockets, saying its state in its tooltip; and "Edit jewel" on a jewel's
# socket row opening the Edit page again over the jewel, under the way back - a view the page makes for itself,
# since markup cannot hold a control inside itself.
#
# What the guard refuses and in which words, which rows offer the button, what a number changed in the layer
# writes - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Edit/SocketJewelTests.
#
# On the pad's PhySpearZon: the worn Stone Barb holds two Shael Runes, a Perfect Skull and the Ruby Jewel of Envy.

function Says([string]$text, [int]$ms = 5000) { try { [bool](WaitUntil { ByName $text } "the strip: $text" $ms) } catch { $false } }
function Tagged([string]$tip) { ByTip $tip -Prefix }

# The guard is a setting: put back with the pad.
Case 'socket-jewel.guard-button-says-its-state' -Tags sockets, items -Changes {
  $guard = WaitUntil { Tagged 'Socket guard off' } "the socket guard, off"
  ClickEl $guard "socket guard"
  Check (Says 'Socket guard on.') "the guard clicked: 'Socket guard on.'"
  Check ($null -ne (WaitUntil { Tagged 'Socket guard on' } "the guard, on" 3000)) "and its tooltip says on"
  ClickEl (Tagged 'Socket guard on') "socket guard"
  Check (Says 'Socket guard off.') "off again: 'Socket guard off.'"
}

Case 'socket-jewel.edit-jewel-opens-the-layer' -Tags sockets, edit {
  ClickUntil { ClickEl (WaitUntil { Tile 'Stone Barb' } "Stone Barb") "Stone Barb" } { OnEdit 'Stone Barb' } "Stone Barb on the Edit page" | Out-Null
  # One Edit jewel, on the tools' line over the row of sockets (not a button on the jewel's own row):
  # dimmed until the jewel's socket is the one selected.
  $open = WaitUntil { ById 'Sockets_EditJewel' } "the tools' Edit jewel"
  Check (-not $open.Current.IsEnabled) "no socket selected: Edit jewel is dimmed"
  ClickEl (WaitUntil { @((All (Cond $AE::AutomationIdProperty 'Socket_Cell')) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -eq 'Ruby Jewel of Envy' })[0] } "the jewel's socket") "the jewel's socket"
  ClickEl (WaitUntil { $e = ById 'Sockets_EditJewel'; if ($e -and $e.Current.IsEnabled) { $e } } "Edit jewel, live" 3000) "Edit jewel"
  Check ($null -ne (WaitUntil { OnEdit 'Ruby Jewel of Envy' } "the jewel in the panel" 5000)) "the panel shows the jewel"
  Check ($null -ne (WaitUntil { ByName 'Back to Stone Barb' } "the way back" 3000)) "under 'Back to Stone Barb'"
  ClickEl (ByName 'Back to Stone Barb') "back"
  Check ($null -ne (WaitUntil { OnEdit 'Stone Barb' } "Stone Barb again" 5000)) "the way back: the panel shows Stone Barb again"
  Check ($null -eq (ByName 'Back to Stone Barb')) "and no way back is offered there"
}
