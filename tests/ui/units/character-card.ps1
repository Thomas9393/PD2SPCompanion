# The character card's tools, as only a window shows them: that the Revive button is drawn on a dead hardcore character's
# card and its click arrives; that Clone opens the name picker, whose box and CLONE answer reach the page; that
# Delete's click reaches the question and its DELETE the files.
#
# What the page then decides - the file revived, the copy's name inside it, the original untouched, nothing of a
# deleted character kept, its backups neither, and that clone and delete are the chosen card's and not an open
# character's - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Home/CharacterCardTests.
#
# On the card pad, with a dead hardcore "Grave" made before the program starts (pd2spc create --hardcore --dead).

function Dump($name) { & $script:Cli dump "$pad\saves\$name.d2s" 2>&1 | Out-String }
function Card($name) { foreach ($c in (All (Cond $AE::ControlTypeProperty $CT::ListItem))) { if (-not $c.Current.IsOffscreen -and $c.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $c } } }

# The fixture, made fresh: the pad keeps a "Gravecopy" of an earlier run, which would refuse the clone.
$makeGrave = {
  Remove-Item "$pad\saves\Grave.*", "$pad\saves\Gravecopy.*" -ErrorAction SilentlyContinue
  $corpus = $script:Corpus
  & $script:Cli create --class Barbarian --template "$corpus\saves\PhySpearZon.d2s" --out "$pad\saves\Grave.d2s" `
    --level 20 --hardcore --dead | Out-Null
}

Case 'character-card.revive-click-revives-and-the-button-goes' -Tags home, character-card -Pad card -Start Home -Before $makeGrave {
  $saves = "$pad\saves"
  Check ((Dump 'Grave') -cmatch 'DEAD') "start: Grave is a dead hardcore character"
  ClickUntil { if ($c = Card 'Grave') { ClickEl $c "Grave's card" } } { ByName 'Open' } "Grave chosen" 800 | Out-Null
  ClickEl (ByName 'Open') "Load"
  WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "Grave loaded" | Out-Null

  $was = (WrittenAt "$saves\Grave.d2s")
  ClickEl (WaitUntil { ByTip 'Revive' } "the revive button") "Revive"
  WaitUntil { (WrittenAt "$saves\Grave.d2s") -ne $was } "Grave written" 8000 | Out-Null
  Check ((Dump 'Grave') -cnotmatch 'DEAD') "revive: Grave is no longer dead"
  Check ($null -eq (ByTip 'Revive')) "revive: the button is gone"
}

Case 'character-card.clone-and-delete-go-through-their-answers' -Tags home, character-card -Pad card -Start Home -Before $makeGrave {
  $saves = "$pad\saves"
  # Clone and delete act on the selected card in the list, not on an open character: nothing is opened.
  ClickUntil { if ($c = Card 'Grave') { ClickEl $c "Grave's card" } } { ByName 'Open' } "Grave chosen" 800 | Out-Null

  # Clone through the name picker: the copy's name, then its CLONE answer. A second apart, since Clone toggles the box.
  $box = ClickUntil { ClickEl (WaitUntil { $b = ByTip 'Clone'; if ($b -and $b.Current.IsEnabled -and -not $b.Current.IsOffscreen) { $b } } "the clone button") "Clone" } `
    { $e = ById 'NameBox'; if ($e -and -not $e.Current.IsOffscreen) { $e } } "the name box" 1000
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Gravecopy')
  # In capitals: ByName ignores case, and the Clone button's own tooltip says "Clone" while the pointer rests on it.
  ClickEl (WaitUntil { @((All (Cond $AE::NameProperty 'CLONE'))) |
    Where-Object { $_.Current.Name -ceq 'CLONE' -and -not $_.Current.IsOffscreen } | Select-Object -First 1 } "the CLONE answer") "CLONE"
  WaitUntil { Test-Path "$saves\Gravecopy.d2s" } "the copy written" 8000 | Out-Null
  $copy = Dump 'Gravecopy'
  Check ($copy -match "Gravecopy\s+-\s+level 20 Barbarian") "clone: Gravecopy.d2s holds a character named Gravecopy"
  Check ((Dump 'Grave') -match "Grave\s+-\s+level 20 Barbarian") "clone: Grave is unchanged"

  # Delete, asked first: Grave is still the chosen card.
  ClickEl (WaitUntil { $d = ByTip 'Delete (Del)'; if ($d -and -not $d.Current.IsOffscreen) { $d } } "the delete button") "Delete"
  ClickEl (WaitUntil { ByName 'DELETE' } "the confirm button") "confirm"
  WaitUntil { -not (Test-Path "$saves\Grave.d2s") } "Grave's save removed" 8000 | Out-Null
  Check (-not (Get-ChildItem $saves -Filter 'Grave.*' | Where-Object { $_.BaseName -eq 'Grave' })) "delete: none of Grave's files are left"
  Check (-not (Test-Path "$saves\PD2SPCompanion Backups\Grave")) "delete: Grave's backups are gone too"
  Check (Test-Path "$saves\Gravecopy.d2s") "delete: the copy is untouched"
}
