# A page kept hidden lays out what changed in it while it was away (back from the game on the
# Items page, the scrollbar was missing with the mercenary's section still partly out of sight). The
# character had no mercenary, so the page had nothing to scroll; the game started and the program closed the
# character; a mercenary was hired in game; the character loaded again, the program went back to the Items page -
# and the mercenary's section ran off the page's foot with no way to scroll to it.
# A change inside a hidden page is lost to the layout (PageDeck.Reclaim), and only a change of the window's size put
# it right - so the window is never minimized or resized here.
#
# What the program decides in this - the character closed for the game, the page and bookmarks it comes back to,
# the mercenary on the Items page's model - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Window/ResumeAfterGameTests. The layout is the window's, and is here.
#
# The game is pretended (PD2SPC_FAKE_GAME: running while its file exists) and the save swapped while the character
# is closed. The saves are real ones, before and after (TestCorpus\ui-pad\fixtures), copied into the pad.
# Nothing is written but by loading. The case begins on the home page and loads GoldFind itself.

$script:Fixtures = "$script:Corpus\ui-pad\fixtures"

# The left page's scroll area - whether it scrolls - and the bottom of the lowest "Boots" slot on it.
function Scrolls {
  $w = $script:win.Current.BoundingRectangle; $middle = $w.X + $w.Width / 2
  # In brackets: bare, PowerShell hands a command the type's name as a string, not the condition.
  foreach ($e in (All ([System.Windows.Automation.Condition]::TrueCondition))) {
    try {
      $b = $e.Current.BoundingRectangle
      if ($b.IsEmpty -or $b.X -ge $middle -or $b.Height -lt ($w.Height * 0.6)) { continue }
      $p = $null
      if ($e.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$p)) { return [pscustomobject]@{ Scrollable = $p.Current.VerticallyScrollable; Bottom = $b.Bottom } }
    } catch { }
  }
}
function LowestSlot { @((All (Cond $AE::NameProperty 'Boots')) | ForEach-Object { $_.Current.BoundingRectangle.Bottom } | Sort-Object)[-1] }
function LoadGoldFind {
  ClickEl (WaitUntil { ByName 'GoldFind' } "the GoldFind card" 15000) "GoldFind"
  Start-Sleep -Milliseconds 300
  ClickEl (WaitUntil { $b = ByName 'Open'; if ($b -and $b.Current.IsEnabled) { $b } } "Load" 15000) "Load"
}

Case 'resume-layout.hidden-items-page-lays-out-the-new-mercenary' -Tags window, layout, game, items -Start Home -Before {
  Remove-Item "$pad\game-running.flag" -ErrorAction SilentlyContinue
  $env:PD2SPC_FAKE_GAME = "$pad\game-running.flag"
  Copy-Item "$script:Fixtures\GoldFind-no-mercenary.d2s" "$pad\saves\GoldFind.d2s" -Force
} {
  $save = "$pad\saves\GoldFind.d2s"
  $flag = "$pad\game-running.flag"
  try {
    LoadGoldFind
    WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "character loaded" | Out-Null
    ClickEl (ById 'SideBar_Inventory') "Items"
    WaitUntil { ItemsPage } "items page" | Out-Null
    Start-Sleep -Milliseconds 800
    $before = Scrolls
    Check ($null -ne (ByName 'Mercenary  (none hired)') -and -not $before.Scrollable -and (LowestSlot) -lt $before.Bottom) "no mercenary: everything on the page, nothing to scroll"

    # The game starts: the program's own look, every two seconds, closes the character.
    Set-Content $flag 'running'
    Check ($null -ne (WaitUntil { if (-not (ItemsPage)) { ByName 'GoldFind' } } "home, the character closed for the game" 15000)) "the game seen: the character closed"

    # The game ends, a mercenary hired in it; the character loaded again goes back to the Items page.
    Copy-Item "$script:Fixtures\GoldFind-mercenary.d2s" $save -Force
    Remove-Item $flag
    Start-Sleep -Seconds 3
    LoadGoldFind
    Check ($null -ne (WaitUntil { ItemsPage } "back on the Items page" 15000)) "loaded again: back on the Items page"
    Start-Sleep -Milliseconds 1500
    $after = Scrolls
    $lowest = LowestSlot
    Check ($lowest -gt $after.Bottom) "the mercenary's section now runs past the page's foot (slot's bottom $([int]$lowest), page's $([int]$after.Bottom))"
    Check ([bool]$after.Scrollable) "and the page scrolls to it"
  }
  finally {
    Remove-Item $flag -ErrorAction SilentlyContinue
    Remove-Item Env:PD2SPC_FAKE_GAME -ErrorAction SilentlyContinue
  }
}
