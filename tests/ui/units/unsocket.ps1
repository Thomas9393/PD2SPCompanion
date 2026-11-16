# A gem out of its socket, as only a window shows it (SocketsEditor): that a click on a worn slot shows its item
# on the Edit page, and that a socket of its row selected, the tools' Unsocket there reaches the item - the file
# read back. (The sockets are a row of cells.)
#
# Where a gem lands, Unsocket all, a gem with nowhere to go and the pane that then takes it are proven without a
# window: tests/PD2SPCompanion.Shell.Tests/Edit/UnsocketTests.
#
# On the pad's PhySpearZon: the worn Stone Barb holds four.

function Dump { & $script:Cli dump "$pad\saves\PhySpearZon.d2s" 2>&1 | Out-String }
# Items stored loose - inventory, stash pages, cube - as the dump counts its sections.
function Stored { ([regex]::Matches((Dump), '== (?:inventory|stash|storage page \d+|cube) \((\d+)\)') | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Sum).Sum }
function Weapon { (Dump) -split '(?=\n  #)' | Where-Object { $_ -match 'Stone Barb' } | Select-Object -First 1 }

Case 'unsocket.worn-item-opened-and-a-gem-turned-out' -Tags sockets, edit -Changes {
  Check ((Weapon) -match '4/4 sockets') "start: Stone Barb holds four"
  $storedBefore = Stored

  # The weapon slot: the tall one, third among the equipped slots' children (after the two swap tabs).
  $slot = WaitUntil { $list = ById 'EquippedSlots'; if ($list) { $list.FindAll($TS::Children, (Cond $AE::ControlTypeProperty $CT::Button))[2] } } "the weapon slot"
  ClickUntil { ClickEl $slot "weapon slot" } { OnEdit 'Stone Barb' } "Stone Barb on the Edit page" | Out-Null

  # The row always has one selected: with none chosen the first filled one stands so (SocketsEditor.MarkSelected).
  Check ([bool](WaitUntil { $u = ById 'Sockets_Unsocket'; if ($u -and $u.Current.IsEnabled) { $u } } "Unsocket live on the socket selected by itself" 3000)) "none chosen, the first filled socket stands selected: Unsocket is live"
  ClickEl (WaitUntil { @((All (Cond $AE::AutomationIdProperty 'Socket_Cell')) | Where-Object { -not $_.Current.IsOffscreen })[0] } "the row's first socket") "the first socket"
  InvokeEl (WaitUntil { $u = ById 'Sockets_Unsocket'; if ($u -and $u.Current.IsEnabled) { $u } } "Unsocket, live" 3000) "Unsocket"
  Kept "the save written"
  Check ((Weapon) -match '3/4 sockets') "written: Stone Barb holds three"
  Check ((Stored) -eq $storedBefore + 1) "written: the gem is stored loose, one more than before ($storedBefore)"
}
