param([string]$pad, [string]$out = "$pad\pseudo")
# The editor in the pseudo-language (every key "[Ŧëxţ]"): each page visited, a screenshot, and every name and
# tooltip on screen that is not pseudo text listed for reading - game text (item names, properties) stays
# English by design; anything else is a string the framework missed. On test copies; settings restored after.
# Not a case, and not run by run.ps1: its output is read. Run: pseudo-language.ps1 -pad <pad>
# Its last section, definite misses, is texts equal to a template key: the 17 seen there were data or game
# text that happen to match a key - seed remarks and folder names the pad already keeps, and game type names
# (Ring, Armor, Helm, Weapon, Strength) on the Grail and Extra pages. Anything else there is a real miss.
. "$PSScriptRoot\fast.ps1"
$saves = "$pad\saves"
New-Item -ItemType Directory -Force $out | Out-Null
python "$PSScriptRoot\..\words.py" --pseudo "$pad\lang" | Out-Null
# The pseudo text of a plain key, as tests/words.py makes it (PowerShell 5.1's JSON reader cannot read the
# pack: it folds "BUY" and "Buy" together).
$from = 'aceinoruyACEINORUY'
$to = -join (@(0xE0, 0xE7, 0xEB, 0xEF, 0xF1, 0xF6, 0x155, 0xFC, 0xFF, 0xC0, 0xC7, 0xCB, 0xCF, 0xD1, 0xD6, 0x154, 0xDC, 0x178) | ForEach-Object { [char]$_ })
function P([string]$english) {
  $chars = $english.ToCharArray() | ForEach-Object { $i = $from.IndexOf($_); if ($i -ge 0) { $to[$i] } else { $_ } }
  '[' + (-join $chars) + ']'
}

$before = "$pad\pseudo-before"
Remove-Item $before -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $before | Out-Null
Copy-Item "$saves\PhySpearZon.d2s", "$saves\pd2_shared.stash" $before
$settingsFile = SettingsFile
$settingsBytes = [IO.File]::ReadAllBytes($settingsFile)
$all = [ordered]@{}
# Every plain key of the template, case kept: on screen in English, under a pack that translates every key,
# it is a definite miss - text that reached the screen without passing L(...).
$keys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$console = [Console]::OutputEncoding
[Console]::OutputEncoding = [Text.Encoding]::UTF8   # keys hold "—" and "·"
python -c "import json,sys; sys.stdout.reconfigure(encoding='utf-8'); [print(k) for k in json.load(open(sys.argv[1], encoding='utf-8-sig'))['strings'] if '{' not in k and chr(10) not in k]" "$PSScriptRoot\..\..\src\PD2SPCompanion.Shell.Desktop\Languages\_template.json" |
  ForEach-Object { [void]$keys.Add($_) }
[Console]::OutputEncoding = $console
$missed = [ordered]@{}

function Dump([string]$page) {
  Start-Sleep -Milliseconds 700
  Shot "$out\$page.png"
  $seen = @()
  foreach ($e in (All [System.Windows.Automation.Condition]::TrueCondition)) {
    try { if ($e.Current.IsOffscreen) { continue } } catch { continue }
    foreach ($text in @($e.Current.Name, $e.Current.HelpText)) {
      if ($text -and $text -match '[A-Za-z]{3}' -and $text -notmatch '\[') { $seen += $text.Replace("`r", '').Replace("`n", ' / ') }
    }
  }
  $script:all[$page] = $seen | Sort-Object -Unique
  $script:missed[$page] = @($script:all[$page] | Where-Object { $script:keys.Contains($_) })
}

try {
  $text = [Text.Encoding]::UTF8.GetString($settingsBytes)
  $json = $text | ConvertFrom-Json
  $json | Add-Member -NotePropertyName Language -NotePropertyValue 'qps-ploc' -Force
  [IO.File]::WriteAllText($settingsFile, ($json | ConvertTo-Json -Depth 20), (New-Object System.Text.UTF8Encoding($false)))
  $env:PD2SPC_LANGUAGES = "$pad\lang"
  try {
    Launch $script:EditorExe $pad
    Dump 'landing'
    ClickEl (WaitUntil { ByName 'Open' } "Load") "Load"
    WaitUntil { (ById 'SideBar_Inventory').Current.IsEnabled } "loaded" | Out-Null
    ClickEl (ById 'SideBar_Char') "Character"; Dump 'character'
    ClickEl (ById 'SideBar_Inventory') "Items"; WaitUntil { ById 'Bookmark_Edit' } "items" | Out-Null; Dump 'items'
    Pane 0 (P 'Personal Stash')
    $tile = WaitUntil { if (Tiles 'Steel Ring of Skill') { Tile 'Steel Ring of Skill' } } "a ring"
    ClickEl $tile "the ring"; Dump 'items-selected'
    ClickEl (ById 'Bookmark_Edit') "Edit"; Dump 'items-edit'
    ClickEl (ById 'Bookmark_Vault_Left') "Vault left"; Dump 'vault-left'
    ClickEl (ById 'Bookmark_Inventory') "Inventory"
    # The right vault is offered once the left pane has left it.
    $right = WaitUntil { $b = ById 'Bookmark_Vault_Right'; if ($b -and $b.Current.IsEnabled) { $b } } "the right vault offered"
    ClickEl $right "Vault"; Dump 'vault'
    ClickEl (ById 'Bookmark_Grail') "Grail"; Dump 'grail'
    ClickEl (ById 'Bookmark_Trade') "Trade"; Dump 'trade'
    ClickEl (ById 'SideBar_Extra') "Extra"; Dump 'extra'
    ClickEl (ById 'SideBar_Settings') "Settings"; Dump 'settings'
    ClickEl (ById 'SideBar_Experimental') "Experimental"; Dump 'experimental'
  }
  finally { Close }
}
finally {
  $env:PD2SPC_LANGUAGES = ''
  Copy-Item "$before\PhySpearZon.d2s", "$before\pd2_shared.stash" $saves -Force
  [IO.File]::WriteAllBytes($settingsFile, $settingsBytes)
  "settings restored"
}
foreach ($page in $all.Keys) { "== $page"; $all[$page] | ForEach-Object { "   $_" } }
"== definite misses (template keys shown in English; $($keys.Count) keys read)"
foreach ($page in $missed.Keys) { $missed[$page] | ForEach-Object { "   ${page}: $_" } }
