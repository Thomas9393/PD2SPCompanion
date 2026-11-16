# Two choices are a pair, as only a window shows it (the UI language's rule C): softcore and hardcore
# are two buttons side by side wherever they are chosen - Settings' Vault card and the character creation's Mode -
# softcore first and of one width; and a click on Settings' pair reaches the vault.
#
# What the pair's commands do - the other mode's vault opened and said, the pair off while a character is open -
# is proven without a window: tests/PD2SPCompanion.Shell.Tests/Home/ModePairTests.
#
# With no character open; no character is made.

function Strip([string]$text, [int]$ms = 4000) { try { [bool](WaitUntil { ByName $text } "the strip: $text" $ms) } catch { $false } }
function WidthOf($id) { [Math]::Round((ById $id).Current.BoundingRectangle.Width, 0) }

Case 'mode-pair.drawn-side-by-side-and-of-one-width' -Tags settings, mode-pair -Start Home {
  ClickEl (WaitUntil { ById 'SideBar_Settings' } "Settings") "Settings"
  $soft = WaitUntil { ById 'Vault_Softcore' } "the vault's Mode pair"
  $hard = ById 'Vault_Hardcore'
  Check ($null -ne $hard -and $soft.Current.IsEnabled -and $hard.Current.IsEnabled) "Settings, Vault: Mode is a pair of buttons, both live with no character open"
  Check ([Math]::Abs($soft.Current.BoundingRectangle.Y - $hard.Current.BoundingRectangle.Y) -lt 2 -and $hard.Current.BoundingRectangle.X -gt $soft.Current.BoundingRectangle.X) "side by side, softcore first"
  $settingsWide = (WidthOf 'Vault_Softcore') + (WidthOf 'Vault_Hardcore')

  # The character creation's Mode: the same pair, as wide.
  ClickEl (WaitUntil { ById 'SideBar_Experimental' } "Experimental") "Experimental"
  $create = WaitUntil { ById 'Create_Softcore' } "creation's Mode pair" 6000
  Check ($null -ne $create -and $null -ne (ById 'Create_Hardcore')) "character creation: Mode is the same pair"
  # Once the page has come to rest: read as it was still being laid out, the pair was half its width (the
  # lookups are quick enough to read it then).
  $script:pairWas = -1
  $createWide = WaitUntil { $now = (WidthOf 'Create_Softcore') + (WidthOf 'Create_Hardcore'); if ($now -gt 0 -and $now -eq $script:pairWas) { $now } else { $script:pairWas = $now; $null } } "creation's pair at rest" 3000
  Check ([Math]::Abs($createWide - $settingsWide) -le 2) "one width for a pair: $createWide px at creation, $settingsWide px in Settings"
}

Case 'mode-pair.settings-pair-click-opens-the-other-vault' -Tags settings, mode-pair, vault -Start Home -Changes {
  ClickEl (WaitUntil { ById 'SideBar_Settings' } "Settings") "Settings"
  ClickEl (WaitUntil { ById 'Vault_Hardcore' } "the vault's Mode pair") "HARDCORE"
  Check (Strip 'Opened hardcore vault.') "the pair opens the other mode's vault: 'Opened hardcore vault.'"
  ClickEl (ById 'Vault_Softcore') "SOFTCORE"
  Check (Strip 'Opened softcore vault.') "and back: 'Opened softcore vault.'"
}
