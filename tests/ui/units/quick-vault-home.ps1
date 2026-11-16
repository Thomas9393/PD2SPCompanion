# A find of the vault's gone to from the home page, as only a window shows it (never blocked: it loads the
# character card highlighted in the selection list, or falls back to the first character): that the vault's finds are rows to click on the home page, and one clicked brings
# the vault's sheet on screen with the item named on the page.
#
# What the search and the pages decide - which character is loaded for it, the find gone to and selected, the
# box's count - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Vault/QuickVaultHomeTests.
#
# Nothing is kept: the box is typed into through UI Automation, so the keyboard's language does not matter.

function Box { ById 'Quick_Box' }
function TypeIn([string]$text) { (Box).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) }
function Heads { @((All (Cond $AE::AutomationIdProperty 'Found_Group')) | Where-Object { -not $_.Current.IsOffscreen }) }
function Rows { @((All (Cond $AE::AutomationIdProperty 'Found_Row')) | Where-Object { -not $_.Current.IsOffscreen }) }
# The first find under the Vault group's heading: the rows between it and the next heading. (VaultRow in the
# script this came from - a name fast.ps1 has for a folder's row, which this file must not take over.)
function FirstVaultFind {
  $heads = @(Heads | Sort-Object { $_.Current.BoundingRectangle.Y })
  $vault = @($heads | Where-Object { $_.Current.Name -eq 'Vault' })[0]
  if (-not $vault) { return $null }
  $from = $vault.Current.BoundingRectangle.Y
  $next = @($heads | Where-Object { $_.Current.BoundingRectangle.Y -gt $from })[0]
  $to = if ($next) { $next.Current.BoundingRectangle.Y } else { [double]::MaxValue }
  @(Rows | Where-Object { $y = $_.Current.BoundingRectangle.Y; $y -gt $from -and $y -lt $to } | Sort-Object { $_.Current.BoundingRectangle.Y })[0]
}

Case 'quick-vault-home.vault-find-clicked-from-home-shows-the-vault' -Tags search, vault -Start Home {
  Check (-not (ById 'SideBar_Inventory').Current.IsEnabled) "the home page: no character is open"

  TypeIn 'charm'
  $row = WaitUntil { FirstVaultFind } "a find of the vault's" 8000
  $name = $row.Current.Name
  Check ($null -ne $row) "typed 'charm' with no character open: the vault's finds are listed ($(@(Heads | ForEach-Object { $_.Current.Name }) -join ', '); the first is $name)"

  ClickEl $row "$name, in the vault"
  Check ([bool](WaitUntil { if ((ById 'SideBar_Inventory').Current.IsEnabled -and (ById 'Vault_Pages')) { 'there' } } "a character loaded, the vault shown" 20000)) "a vault find clicked with no character open: a character is loaded and the vault shown"
  $said = @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -eq $name }).Count
  Check ($said -ge 1) "its name is on the page: the item is selected ($name)"

  # Back at the vault's top, as the cases that share this start expect to find the page.
  VaultGoTo 'Vault'
}
