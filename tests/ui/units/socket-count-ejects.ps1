# The socket count's box, as only a window shows it: that a count set in the stepper's box reaches the item - and,
# under what is socketed, turns the rest out into the file.
#
# What a count does whichever way it is set - the die, the Demonic Cube, the box - where the runes land, and what
# the die says of its range is proven without a window: tests/PD2SPCompanion.Shell.Tests/Edit/SocketCountTests.
#
# On the pad's PhySpearZon: the Witchwild String in the personal stash rolls 2-4 and holds three runes.

function Dump { & $script:Cli dump "$pad\saves\PhySpearZon.d2s" 2>&1 | Out-String }
# The bow's sockets in the file, "filled/total".
function Bow { if ((Dump) -match 'Witchwild String[^\n]*\n[^\n]*?(\d)/(\d) sockets') { "$($Matches[1])/$($Matches[2])" } elseif ((Dump) -match 'Witchwild String') { '0/0' } }
function Stash { $m = [regex]::Match((Dump), '== stash \((\d+)\)'); if ($m.Success) { [int]$m.Groups[1].Value } else { 0 } }
function Box { ById 'Sockets_Count' }
function Count { (Box).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }

Case 'socket-count-ejects.box-turns-the-runes-out' -Tags sockets, edit -Changes {
  $stash = Stash
  Check ((Bow) -eq '3/3') "start: the bow holds three runes in three sockets ($(Bow)), the stash $stash items"
  Pane 0 'Personal Stash'
  ClickUntil { ClickEl (WaitUntil { Tile 'Witchwild String' } "the bow") "the bow" } { OnEdit 'Witchwild String' } "the bow selected" 800 | Out-Null
  WaitUntil { (Box) -and (Count) -eq '3' } "the stepper reads 3" | Out-Null
  (Box).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('0')
  Kept "the save written (none set)"
  WaitUntil { OnEdit 'Witchwild String' } "the bow still open" 5000 | Out-Null
  Check ((Bow) -eq '0/0') "0 set in the box and kept: the bow has no sockets ($(Bow))"
  Check ((Stash) -eq $stash + 3) "all three runes lie in the stash ($stash -> $(Stash))"
}
