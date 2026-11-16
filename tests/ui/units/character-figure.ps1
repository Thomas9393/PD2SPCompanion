# The character drawn in its gear:
# on the landing page the chosen card's figure, facing the viewer, in place of the class picture; opened, the
# Character page's card draws it too - and nothing about a layer missing or failing is logged on the way.
# Screenshots are left in the pad for a person to look at: what the figure looks like is nobody else's to judge.
#
# That a figure is made and handed to the cards is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Character/FigureTests.
#
# On the card pad, with the corpus's Amazon copied in before the program starts.

function Card($name) { foreach ($c in (All (Cond $AE::ControlTypeProperty $CT::ListItem))) { if (-not $c.Current.IsOffscreen -and $c.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name))) { return $c } } }
function Figures { @((All (Cond $AE::NameProperty 'Character figure')) | Where-Object { -not $_.Current.IsOffscreen }) }

Case 'character-figure.landing-card-and-character-page-draw-it' -Tags home, character, figure -Pad card -Start Home -Before {
  $corpus = $script:Corpus
  Copy-Item "$corpus\saves\PhySpearZon.d2s" "$pad\saves\PhySpearZon.d2s" -Force
  # This start's lines only: the pad's log is every case's that ran on it, and another's character - a bare
  # Necromancer made for the golem's test - has a layer the game ships no art for.
  $script:figureLogged = @(Get-Content "$pad\editor.log" -ErrorAction SilentlyContinue).Count
} {
  ClickUntil { if ($c = Card 'PhySpearZon') { ClickEl $c "PhySpearZon's card" } } { ByName 'Open' } "PhySpearZon chosen" 800 | Out-Null
  Check ([bool](WaitUntil { if ((Figures).Count -gt 0) { $true } } "the figure on the landing page" 10000)) "landing: the chosen card shows the figure"
  Start-Sleep -Milliseconds 400; Shot "$pad\figure-landing.png"

  ClickEl (ByName 'Open') "Load"
  WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "PhySpearZon loaded" | Out-Null
  ClickEl (ById 'SideBar_Char') "Character"
  Check ([bool](WaitUntil { if ((Figures).Count -gt 0) { $true } } "the figure on the Character page" 10000)) "Character page: the card shows the figure"
  Start-Sleep -Milliseconds 400; Shot "$pad\figure-character.png"

  $log = Get-Content "$pad\editor.log" -ErrorAction SilentlyContinue | Select-Object -Skip $script:figureLogged | Where-Object { $_ -match 'figure' }
  Check (-not $log) "no layer missing or failing in the log$(if ($log) { ": $($log -join ' | ')" })"
}
