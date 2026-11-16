# Fast UI driving for PD2SPCompanion tests - the policy (hammer the waiting):
#   1. wait for a condition, never a fixed time: poll every 100 ms, fail loudly with what it waited for
#   2. targeted lookups: one search condition (id, tooltip, name), not a walk over every visible element
#   3. one PowerShell process per test: this file is loaded once
#   4. check results in the files on disk; a screenshot only where a person must look
#   5. a timestamped line per step, so a stall shows where it is
#   6. a failed wait leaves <pad>\ui-fail.png, the window as it stood, so no second run is needed to see why
# Clicks only while the editor is the foreground window (EnsureFront), and never on an element not found.
#   7. any error ends the test: without Stop, a failing command inside try/finally ends only that command,
#      the script runs on and can report a pass (quest-confirm did)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Fast {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int dx, int dy, int d, int e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, int f, int e);
  public static void Click(int x, int y) { SetCursorPos(x, y); mouse_event(2,0,0,0,0); mouse_event(4,0,0,0,0); }
}
"@ -ErrorAction SilentlyContinue
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class FastSeen {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int id);
  [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int k);
  [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(int thread);
}
"@ -ErrorAction SilentlyContinue

$script:AE = [System.Windows.Automation.AutomationElement]
$script:TS = [System.Windows.Automation.TreeScope]
$script:CT = [System.Windows.Automation.ControlType]
$script:clock = [Diagnostics.Stopwatch]::StartNew()

# The editor's data folder, as the editor works it out (EditorData.Root): PD2SPC_DATA, or Data beside
# the program. Only a Debug build reads PD2SPC_DATA; a release build always uses its own Data, so a
# test run against a published exe (PERF_EXE) works on that copy's settings, not these. The developer's PD2SPC_DATA is a user variable, read from there too, so a shell started
# before it was set still points the tests at the real settings, as AppData did before.
# The program is this checkout's own: a named path started master's build from a worktree, with no word of it.
$script:EditorExe = [IO.Path]::GetFullPath("$PSScriptRoot\..\..\src\PD2SPCompanion.Shell.Desktop\bin\Debug\net10.0\PD2SPCompanion.exe")
# $script:Corpus and $script:GameRoot, for the cases.
. "$PSScriptRoot\..\paths.ps1"
$script:liveData = [Environment]::GetEnvironmentVariable('PD2SPC_DATA', 'User')
if (-not $env:PD2SPC_DATA) { $env:PD2SPC_DATA = $script:liveData }
# run.ps1 gives its run a data folder of its own before this file is read. A script that reads this file by
# itself - the measuring scripts of tests/perf - had none, and started the program on the player's own folder:
# its settings rewritten and put back, its logs and whatever else the program keeps written
# there. Such a script gets one in its pad, the developer's settings copied into it for the game folder.
if ($pad -and $script:liveData -and $env:PD2SPC_DATA -eq $script:liveData -and (Test-Path "$script:liveData\settings.json")) {
  $script:ownData = "$pad\data"
  if (-not (Test-Path $script:ownData)) { New-Item -ItemType Directory $script:ownData | Out-Null }
  Copy-Item "$script:liveData\settings.json" $script:ownData -Force
  if ((Test-Path "$script:liveData\Seasons") -and -not (Test-Path "$script:ownData\Seasons")) { Copy-Item "$script:liveData\Seasons" "$script:ownData\Seasons" -Recurse }
  $env:PD2SPC_DATA = $script:ownData
}
function DataRoot { if ($env:PD2SPC_DATA) { $env:PD2SPC_DATA } else { Join-Path (Split-Path $script:EditorExe) 'Data' } }
# The pad's vaults for whatever the test runs before the editor too - its own pd2spc calls: a vault opened by one
# is upgraded where its format is older, and the backup that takes went to the player's own Vaults\Backups while
# the folder was only named at Launch.
if ($pad) { $env:PD2SPC_VAULTS = "$pad\vtsmart" }
# Every section as a new install has it, whatever the player folded and the editor kept (FoldMemory): a test that
# is about the folds themselves clears this before its Launch.
$env:PD2SPC_FOLDS = 'off'
# Nor does a vault keep its play mode, or the editor ask about one (VaultPlayMode): the tests start the
# editor under one mode after another on the same pad, and each start would write its mode into the pad's vault
# and the next be asked about it. A test that is about the vault's play mode clears this before its Launch.
$env:PD2SPC_VAULT_MODE = 'off'
# When a file was last written, across the editor's own save: it writes a new copy and swaps it in
# (File.Replace), and for that instant the file is not there. A test that polled then died on a missing
# file (craft-in-place, seen twice). Waits the swap out, up to a second.
function WrittenAt([string]$path) {
  for ($n = 0; $n -lt 50; $n++) {
    $item = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
    if ($item) { return $item.LastWriteTimeUtc }
    Start-Sleep -Milliseconds 20
  }
  throw "not found: $path"
}
# The settings the run works on - a copy in the run's own data folder (run.ps1, or the lines above) - which a
# test that changes them backs up and puts back.
function SettingsFile { Join-Path (DataRoot) 'settings.json' }

# The play mode a test runs in, written into the settings as the editor writes them: UTF-8 without a
# BOM. Read as text, so a BOM already there is dropped - not decoded into the text and written back
# behind a second one, which the editor cannot parse and so opens on its defaults. A test
# reads the settings' bytes before this and puts them back after.
function UsePlayMode([string]$mode) {
  # Whatever stands between the name and its value: a settings file a case rewrote through PowerShell's own
  # ConvertTo-Json has two spaces there, and the mode asked for was silently not set.
  $text = [IO.File]::ReadAllText((SettingsFile)) -replace '"PlayMode":\s*"\w+"', "`"PlayMode`": `"$mode`""
  [IO.File]::WriteAllText((SettingsFile), $text, (New-Object System.Text.UTF8Encoding($false)))
}

function Step($what) { "{0,6:N1}s  {1}" -f $script:clock.Elapsed.TotalSeconds, $what }

# A condition asked while the page redraws can meet an element that has just gone: that is "not yet".
function Invoke-Cond([scriptblock]$cond) {
  try { & $cond } catch {
    # Also UI Automation's passing COM failures ("Could not open the process token"), seen once in a redraw.
    $e = $_.Exception
    while ($e -and $e -isnot [System.Windows.Automation.ElementNotAvailableException] -and $e -isnot [System.Runtime.InteropServices.COMException]) { $e = $e.InnerException }
    if (-not $e) { throw }
  }
}

# The input language of the window in front, as a culture's name ("en-US", "zh-CN"). The tests type through the
# keyboard, and an input method takes the letters into a composition of its own: with Chinese on, "absorbs cold"
# reached the search box as "absorbscold" - the space confirmed the composition (the language
# switched while a run was under way).
function InputLanguage {
  try {
    $id = 0; $thread = [FastSeen]::GetWindowThreadProcessId([FastSeen]::GetForegroundWindow(), [ref]$id)
    $lang = [FastSeen]::GetKeyboardLayout($thread).ToInt64() -band 0xFFFF
    [System.Globalization.CultureInfo]::GetCultureInfo([int]$lang).Name
  } catch { '?' }
}
# English, or not read: only a language known to be another refuses a run.
function InputIsEnglish { $name = InputLanguage; $name -eq '?' -or $name -like 'en*' }

# What a timeout cannot show in its screenshot: which window has the foreground, what holds the keyboard focus,
# which of Shift, Ctrl and Alt are down, and the input language. Two tests each failed once on a key press that
# did nothing (Ctrl+A in vault-folders, a space lost from "absorbs cold" in keyboard) and left nothing
# to tell the cause by; it was the input language. Said with every timeout since.
function Standing {
  try {
    $h = [FastSeen]::GetForegroundWindow(); $id = 0; [void][FastSeen]::GetWindowThreadProcessId($h, [ref]$id)
    $front = if ($script:proc -and $h -eq $script:proc.MainWindowHandle) { 'the editor' }
             else { $who = try { (Get-Process -Id $id).ProcessName } catch { '?' }; "'$who', not the editor" }
    $f = try { [System.Windows.Automation.AutomationElement]::FocusedElement } catch { $null }
    $focus = if ($f) { try { "$($f.Current.ControlType.ProgrammaticName -replace 'ControlType\.', '') '$($f.Current.AutomationId)'" } catch { '?' } } else { 'nothing' }
    $down = @(@('Shift', 0x10), @('Ctrl', 0x11), @('Alt', 0x12) | Where-Object { [FastSeen]::GetAsyncKeyState($_[1]) -band 0x8000 } | ForEach-Object { $_[0] })
    "in front: $front; focus: $focus; keys down: $(if ($down) { $down -join '+' } else { 'none' }); input: $(InputLanguage)"
  } catch { 'standing not read' }
}

function WaitUntil([scriptblock]$cond, [string]$what, [int]$ms = 8000) {
  $until = $script:clock.ElapsedMilliseconds + $ms
  while ($script:clock.ElapsedMilliseconds -lt $until) {
    $v = Invoke-Cond $cond
    if ($v) { return $v }
    Start-Sleep -Milliseconds 50
  }
  # Read before the screenshot: taking it brings the editor to the front.
  $standing = Standing
  if ($script:pad -and $script:r) { Shot "$($script:pad)\ui-fail.png" }
  throw "timed out after $($ms)ms waiting for: $what [$standing]"
}

# The editor's window as it stands, to a PNG.
# The editor brought to the front first: the capture is of the screen, so any window over it would be
# the shot instead.
function Shot([string]$path) {
  Front
  $bmp = New-Object System.Drawing.Bitmap ([int]$script:r.Width), ([int]$script:r.Height)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen([int]$script:r.X, [int]$script:r.Y, 0, 0, $bmp.Size); $bmp.Save($path); $g.Dispose(); $bmp.Dispose()
}

function Launch([string]$exe, [string]$pad, [switch]$NoTyping) {
  # Never while the game runs: a test takes the mouse and the foreground from whoever is playing, and the
  # editor refuses to open a character while Game.exe is up, so the test would fail anyway.
  if (Get-Process Game -ErrorAction SilentlyContinue) { throw "the game is running: no UI test is started over it" }
  # Nor under an input method: its composition takes the keys the tests type (InputLanguage).
  # -NoTyping: a script that sends no letters - clicks, the wheel, keys that are not text - runs under any.
  if (-not $NoTyping -and -not (InputIsEnglish)) { throw "the input language is $(InputLanguage): the tests type through the keyboard - switch to English and run again" }
  $script:pad = $pad
  $env:PD2SPC_VAULTS = "$pad\vtsmart"; $env:PD2SPC_SAVES = "$pad\saves"
  # The editor's log in the pad too, not among the player's own (PD2SPC_LOG).
  $env:PD2SPC_LOG = "$pad\editor.log"
  # The titles' font from the pad too (PD2SPC_FONTS): none there, so every test runs in Cinzel, the shipped
  # face, whatever the developer keeps in the build's own Fonts folder. title-font puts one in.
  $env:PD2SPC_FONTS = "$pad\fonts"
  $script:proc = Start-Process $exe -PassThru
  $script:proc.Id | Out-File "$pad\editor.pid" -Encoding ascii
  # The main window, not the splash: whichever of the process's windows has the sidebar.
  $script:win = WaitUntil {
    foreach ($w in $AE::RootElement.FindAll($TS::Children, (Cond $AE::ProcessIdProperty $script:proc.Id))) {
      if ($w.FindFirst($TS::Descendants, (Cond $AE::AutomationIdProperty 'SideBar_Settings'))) { return $w }
    } } "editor main window with its sidebar" 30000
  # Its pages are laid out behind the splash, the window there and not yet in sight: the splash gone,
  # it is - a click before that fell on whatever lay under it.
  WaitUntil { @($AE::RootElement.FindAll($TS::Children, (Cond $AE::ProcessIdProperty $script:proc.Id))).Count -le 1 } "the splash gone" 30000 | Out-Null
  $script:proc.Refresh()
  Front
  $script:r = $script:win.Current.BoundingRectangle
  $script:keptAt = PadStamps
}

# How the window is shown when it is brought to the front: maximized (3), as every test runs - but for one that
# is about a window of some other size (vault-narrow), which sets this to 5, shown as it stands.
$script:show = 3
function Front {
  $h = $script:proc.MainWindowHandle
  [Fast]::ShowWindow($h, $script:show) | Out-Null
  if ([Fast]::GetForegroundWindow() -ne $h) { [Fast]::keybd_event(0x12,0,0,0); [Fast]::keybd_event(0x12,0,2,0); [Fast]::SetForegroundWindow($h) | Out-Null }
  WaitUntil { [Fast]::GetForegroundWindow() -eq $h } "editor in front (no clicks made)" 3000 | Out-Null
  $script:r = $script:win.Current.BoundingRectangle
}

function Cond($prop, $value) { New-Object System.Windows.Automation.PropertyCondition($prop, $value) }
# What is on screen only, every lookup: the pages are kept once shown and hidden behind each other (PageDeck),
# so a hidden page's controls are in the tree too - found first, clicked where nothing is.
# A lookup made outside a wait, steadied: asked while a page redraws, the walk meets an element that has just
# gone and throws (page-lock, three runs of many tests in a row and never alone: its first check
# came a moment after the Items page began drawing). Asked again a few times before that is let through.
function Steady([scriptblock]$look) {
  $failed = 0
  for ($try = 0; ; $try++) {
    try {
      $found = (& $look)
      # Said, so a log shows how often it happens and that it passed: see below.
      if ($failed) { Write-Host "  (a walk ended in E_FAIL $failed time(s) and then read)" }
      return $found
    }
    catch {
      # The tree changed under the walk: an element gone - or, now and then, the walk itself answered E_FAIL
      # with nothing wrong on screen and the next walk reading (sockets failed one run in two or three on it;
      # walked back to back after the same edit, 180 walks, it never did). A fault that lasts -
      # the tooltip shown twice was one - still fails here: five tries are under half a second.
      $e = $_.Exception
      while ($e -and $e -isnot [System.Windows.Automation.ElementNotAvailableException] -and
             -not ($e -is [System.Runtime.InteropServices.COMException] -and $e.HResult -eq -2147467259)) { $e = $e.InnerException }
      if (-not $e -or $try -ge 5) { throw }
      if ($e -is [System.Runtime.InteropServices.COMException]) { $failed++ }
      Start-Sleep -Milliseconds 80
    }
  }
}
# Every element of the window meeting a condition - on screen or not; the caller says - the walk steadied. What a
# test's own lookups go through: 154 of them walked the window themselves, and one made outside a wait as the page
# redrew threw (page-lock three times, then forge in a full run - never alone, never the same place).
function All($condition) { Steady { ,@($script:win.FindAll($TS::Descendants, $condition)) } }
# The first on screen, asked for as one question: the walk stops at it. Asked as "all of them, then the first on
# screen" every lookup walked the whole window - a tenth of a second, found or not, and a case makes dozens
# (measured: 105 ms against 12 for one that is there; one that is not there still walks it all).
function OnScreen($condition) {
  $seen = New-Object System.Windows.Automation.AndCondition($condition, (Cond $AE::IsOffscreenProperty $false))
  Steady { $script:win.FindFirst($TS::Descendants, $seen) }
}
function ById($id) { OnScreen (Cond $AE::AutomationIdProperty $id) }
# A button by its tooltip: exactly, or its first line (multi-line tooltips) with -Prefix.
function ByTip($tip, [switch]$Prefix) {
  if (-not $Prefix) { return OnScreen (Cond $AE::HelpTextProperty $tip) }
  Steady {
    foreach ($b in (All (Cond $AE::ControlTypeProperty $CT::Button))) {
      if ($b.Current.HelpText.StartsWith($tip) -and -not $b.Current.IsOffscreen) { return $b }
    }
  }
}
# By its text, ignoring case: the pages draw some labels in capitals ("LOAD"). -Prefix for a text
# that goes on, or ends in padding. Never an item's tile, which
# carries the item's name too: Tile finds those.
# A tooltip's text, which is not the thing it describes: the pointer left on a tile after a click shows
# the item's name in a tooltip, and that came first in the tree (rarename). Tooltips are
# read through ByTip (their HelpText), never by name.
function InToolTip($e) {
  $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($e)
  $null -ne $parent -and $parent.Current.ClassName -eq 'ToolTip'
}

function ByName($name, [switch]$Prefix) {
  if ($Prefix) {
    return Steady {
      foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
        if ($e.Current.Name.StartsWith($name, [StringComparison]::OrdinalIgnoreCase) -and -not $e.Current.IsOffscreen -and -not (InToolTip $e)) { return $e }
      }
    }
  }
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name, [System.Windows.Automation.PropertyConditionFlags]::IgnoreCase)
  Steady { foreach ($e in (All $c)) { if (-not $e.Current.IsOffscreen -and $e.Current.AutomationId -ne 'Tile' -and -not (InToolTip $e)) { return $e } } }
}

# The item the Edit page shows, by its name: the heading of the item's summary (Item_Name), which the Forge's
# holder wears too - whichever is on screen. The name alone says nothing of the page: the item's tile and its
# tooltip carry it as well.
function OnEdit([string]$name) { $e = ById 'Item_Name'; if ($e -and $e.Current.Name -eq $name) { $e } }
# An item's tile by the item's name - in a grid, a worn slot or the mercenary's - top to bottom, left
# to right; -Nth for the second of two alike. Tiles carry the id "Tile" and the name for UI Automation only.
function Tiles($name) {
  $c = New-Object System.Windows.Automation.AndCondition((Cond $AE::AutomationIdProperty 'Tile'), (Cond $AE::NameProperty $name))
  Steady { @((All $c) | Where-Object { -not $_.Current.IsOffscreen } | Sort-Object { $_.Current.BoundingRectangle.Y }, { $_.Current.BoundingRectangle.X }) }
}
# Nothing where there is none (or not that many): asked inside a wait, "not yet" - an index into nothing threw,
# and a test begun a moment before its tiles were built ended there (sell-follow, select-no-writes).
# The vault's folder as one sheet: its scroll (the id is still Vault_Pages), an item lying on the sheet where
# the rule lays it, and the way to a folder, which is its row in the folder tree.
function VaultPages { ById 'Vault_Pages' }
function VaultTile($name, [int]$Nth = 0) {
  $pages = VaultPages; if (-not $pages) { return }
  $area = $pages.Current.BoundingRectangle
  $found = @(Tiles $name | Where-Object { $b = $_.Current.BoundingRectangle; $b.X -ge $area.X -and $b.X -lt $area.X + $area.Width })
  if ($found.Count -gt $Nth) { $found[$Nth] }
}
# A folder of the folder shown, as its tile on the sheet: four cells by four, first on the sheet. Clicked it is
# selected, double clicked gone into.
function VaultFolderTile($name) {
  Steady { @((All (Cond $AE::AutomationIdProperty 'SheetFolder')) | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen })[0] }
}
# Where the vault page is: the path over it, whole, as the button at its end says it (the path's steps are
# buttons, Vault_Crumb).
function VaultShown { $box = ById 'Vault_Folder'; if ($box) { $box.Current.Name } }
# A folder's row: in the folder tree at the page's left, or - Listed - in the list of folders open under the
# path. The two are the same row to look at, so which it is is asked of what it lies in. -Id FoldPlace is its chevron.
function InVaultTree($el) {
  $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
  for ($e = $el; $e; $e = $walker.GetParent($e)) {
    if ($e.Current.AutomationId -eq 'Vault_Tree') { return $true }
    if ($e.Current.ControlType -eq $CT::Window) { break }
  }
  $false
}
# The tree opens on the vault's own folders, every one folded, and so does the list: a row that is
# not there is looked for again once what is folded has been unfolded (VaultUnfold) - but for -AsIs, for a case
# that is about the folds themselves and asks whether a row is out of sight.
function VaultRow($name, [switch]$Listed, [string]$Id = 'FolderRow', [switch]$AsIs) {
  $find = {
    Steady {
      foreach ($row in (All (Cond $AE::AutomationIdProperty $Id))) {
        if ($row.Current.Name -eq $name -and -not $row.Current.IsOffscreen -and (InVaultTree $row) -ne [bool]$Listed) { return $row }
      }
    }
  }
  $row = & $find
  if (-not $row -and -not $AsIs -and (VaultUnfold -Listed:$Listed)) { $row = & $find }
  $row
}
# Every folded folder of the tree - or, -Listed, of a list of folders - unfolded by its chevron, without the mouse:
# a chevron says "Unfold" while its folder is folded. Whether anything was unfolded.
function VaultUnfold([switch]$Listed) {
  $any = $false
  for ($pass = 0; $pass -lt 6; $pass++) {
    $folded = @((All (Cond $AE::AutomationIdProperty 'FoldPlace')) | Where-Object { $_.Current.HelpText -eq 'Unfold' -and (InVaultTree $_) -ne [bool]$Listed })
    if ($folded.Count -eq 0) { break }
    foreach ($chevron in $folded) { try { InvokeEl $chevron "unfold $($chevron.Current.Name)"; $any = $true } catch { } }
    Start-Sleep -Milliseconds 200
  }
  $any
}
# How many items a tree row's folder holds, as the small line under its name says it ("3 items", "3 items . 2
# folders"): the number, or '' where the line says
# none ("2 folders", "Empty"). VaultRowLine is the line whole.
function VaultRowLine($row) {
  if (-not $row) { return $null }
  $r = $row.Current.BoundingRectangle
  $line = @((All (Cond $AE::AutomationIdProperty 'FolderRow_Count')) | Where-Object {
      $b = $_.Current.BoundingRectangle
      -not $_.Current.IsOffscreen -and $b.X -ge $r.X -and $b.X + $b.Width -le $r.X + $r.Width + 1 -and $b.Y -ge $r.Y - 1 -and $b.Y + $b.Height -le $r.Y + $r.Height + 1 })[0]
  if ($line) { $line.Current.Name } else { '' }
}
function VaultRowCount($row) {
  if (-not $row) { return $null }
  if ((VaultRowLine $row) -match '^(\d+) items?') { return $Matches[1] } else { return '' }
}
# To a folder, by its row in the tree.
function VaultGoTo($name) {
  # The row is as wide as the tree's longest name, the tree as wide as its pane: the click goes to the middle of the
  # part of the row in sight, not the row's own - which, in a tree scrolling sideways, lies under the column of
  # counts and locks or past the pane (seen on the Item Library's tree).
  $row = WaitUntil { VaultRow $name } "the tree's row '$name'" 4000
  $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
  for ($tree = $row; $tree -and $tree.Current.AutomationId -ne 'Vault_Tree'; $tree = $walker.GetParent($tree)) { }
  $b = $row.Current.BoundingRectangle
  if ($tree) {
    $t = $tree.Current.BoundingRectangle
    $left = [math]::Max($b.X, $t.X); $right = [math]::Min($b.X + $b.Width, $t.X + $t.Width)
    $middle = $b.Y + $b.Height / 2
    # A row not wholly in sight is pressed without the mouse: a click at its rectangle lands on whatever lies
    # there - the next thing down the page, or the bar across the tree's foot, which covers the last of a row
    # that reaches it (a tree of taller rows shows fewer of them).
    if ($b.Y -lt $t.Y -or $b.Y + $b.Height -gt $t.Y + $t.Height - 16) { InvokeEl $row "folder '$name'" }
    else { Front; [Fast]::Click([int](($left + $right) / 2), [int]$middle) }
  }
  else { ClickEl $row "folder '$name'" }
  WaitUntil { (VaultShown) -like "*$name" } "the path says $name" 3000 | Out-Null
}
# The list of every folder, opened under the path: until it lists the folder named.
function VaultList($name) {
  ClickUntil { ClickEl (ById 'Vault_Folder') "all folders" } { VaultRow $name -Listed } "the list of folders, '$name'" 800 | Out-Null
}
function Tile($name, [int]$Nth = 0) { $found = @(Tiles $name); if ($found.Count -gt $Nth) { $found[$Nth] } }

# A point in window pixels, for ClickAt and DragTo, found from a tile rather than written down: a
# layout change moves the grids, and points written down then click the wrong thing.
# The tile's centre, or the centre of the cell $dx cells across and $dy down from its first cell -
# $w and $h are the tile's size in cells.
function CellOf($tile, [int]$dx = 0, [int]$dy = 0, [int]$w = 1, [int]$h = 1) {
  $b = $tile.Current.BoundingRectangle; $cw = $b.Width / $w; $ch = $b.Height / $h
  @([int]($b.X - $script:r.X + $cw * ($dx + 0.5)), [int]($b.Y - $script:r.Y + $ch * ($dy + 0.5)))
}

# The middle of the book: the gutter between its two pages (Frame_PageLeft, Frame_PageRight) - not the window's
# middle, which the side bar puts some way left of it (47 pixels in a window 2560 wide). Taken for it, the far end
# of the left page's tools line read as the right page's once the pages were made wider, and a case that asked
# for the left page's Compare button found none (send-across). Looked up at most twice a second: it is
# asked once for every element a search of a page turns up. Which page an element is on: PageOf.
function BookMiddle {
  if ($script:bookMiddleAt -and ([datetime]::Now - $script:bookMiddleAt).TotalMilliseconds -lt 500) { return $script:bookMiddle }
  $l = ById 'Frame_PageLeft'; $r = ById 'Frame_PageRight'
  $script:bookMiddle = if ($l -and $r) { $a = $l.Current.BoundingRectangle; ($a.X + $a.Width + $r.Current.BoundingRectangle.X) / 2 } else { $script:r.X + $script:r.Width / 2 }
  $script:bookMiddleAt = [datetime]::Now
  $script:bookMiddle
}
function PageOf($el) { $b = $el.Current.BoundingRectangle; if ($b.X + $b.Width / 2 -gt (BookMiddle)) { 'Right' } else { 'Left' } }

# A grid's cell in screen pixels, read off a tile of a grid or of the vault's sheet that is $w cells wide: not
# written down as 35, which it is only at Windows' 125% and a UI scale of 1.
function CellPx($tile, [int]$w = 1) { $tile.Current.BoundingRectangle.Width / $w }
# A point in window pixels, for ClickAt and DragTo: the middle of a cell of a pane's grid, $col across and $row
# down from 0 - for an empty cell, which has no tile to be found by (CellOf finds one from a tile). Counted from
# the pane's title line, which the grid begins under: 2 in and 37 down where a cell is 35 (measured).
function PaneCell([int]$index, [int]$col, [int]$row, [double]$cell) {
  $t = (PaneLine $index).Current.BoundingRectangle
  @([int]($t.X + $cell * (2 / 35 + $col + 0.5) - $script:r.X), [int]($t.Y + $cell * (37 / 35 + $row + 0.5) - $script:r.Y))
}

function ClickEl($el, [string]$what) {
  if ($null -eq $el) { throw "not found, not clicked: $what" }
  Front
  $b = $el.Current.BoundingRectangle
  if ($b.IsEmpty -or [double]::IsNaN($b.X) -or [double]::IsInfinity($b.X)) { throw "not on screen, not clicked: $what (InvokeEl presses it wherever it is)" }
  [Fast]::Click([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2))
}
# Presses the nearest button at or above an element through UI Automation rather than the mouse: for
# a row a list has scrolled out of sight, where a click at its rectangle would land on nothing.
function InvokeEl($el, [string]$what) {
  if ($null -eq $el) { throw "not found, not invoked: $what" }
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  for ($at = $el; $null -ne $at; $at = $walker.GetParent($at)) {
    $pattern = $null
    if ($at.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke(); return }
  }
  throw "nothing to invoke at or above: $what"
}
# Two clicks inside the system's double-click time.
function DoubleClickEl($el, [string]$what) {
  if ($null -eq $el) { throw "not found, not clicked: $what" }
  Front
  $b = $el.Current.BoundingRectangle
  $x = [int]($b.X + $b.Width / 2); $y = [int]($b.Y + $b.Height / 2)
  [Fast]::Click($x, $y); Start-Sleep -Milliseconds 60; [Fast]::Click($x, $y)
}
function ClickAt([int]$x, [int]$y) { Front; [Fast]::Click([int]$script:r.X + $x, [int]$script:r.Y + $y) }

# A drag with the left button, from one point to another (window pixels, or an element's centre),
# in small steps so the page sees it pass its threshold. Shift or Ctrl held with -Modifier.
# -Hold waits on the target before letting go: a drag within the inventory lifts the item and the
# page rebuilds (~0.3-0.7 s), and a release before that finds no cell and puts it back.
function DragTo($from, $to, [ValidateSet('', 'Shift', 'Ctrl')][string]$Modifier = '', [int]$Hold = 60) {
  function Point($p) {
    if ($p -is [System.Windows.Automation.AutomationElement]) {
      try { $b = $p.Current.BoundingRectangle } catch { return @([double]::NaN, [double]::NaN) }
      if ($b.IsEmpty) { return @([double]::NaN, [double]::NaN) }
      return @([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2))
    }
    return @(([int]$script:r.X + $p[0]), ([int]$script:r.Y + $p[1]))
  }
  $a = Point $from; $b = Point $to
  # An element gone or not laid out yet has no rectangle: no press at all, for ClickUntil to try again.
  if (($a + $b) | Where-Object { [double]::IsNaN($_) -or [double]::IsInfinity($_) }) { return }
  Front
  $key = @{ Shift = 0x10; Ctrl = 0x11 }[$Modifier]
  if ($key) { [Fast]::keybd_event($key, 0, 0, 0) }
  [Fast]::SetCursorPos($a[0], $a[1]) | Out-Null; [Fast]::mouse_event(2, 0, 0, 0, 0)
  for ($i = 1; $i -le 12; $i++) {
    [Fast]::SetCursorPos([int]($a[0] + ($b[0] - $a[0]) * $i / 12), [int]($a[1] + ($b[1] - $a[1]) * $i / 12)) | Out-Null; Start-Sleep -Milliseconds 15
  }
  Start-Sleep -Milliseconds $Hold
  [Fast]::mouse_event(4, 0, 0, 0, 0)
  if ($key) { [Fast]::keybd_event($key, 0, 2, 0) }
}

# A click that a page still fading in can swallow (a bookmark switch, a save's redraw): click, give it
# 400 ms (or -within) to show, click again - up to five times. Only for what is safe twice: a click that selects,
# or a drag written to be tried again only while what it carries still lies where it lay (`if ($c = Card ..) { DragTo $c .. }`).
function ClickUntil([scriptblock]$click, [scriptblock]$shown, [string]$what, [int]$within = 400) {
  for ($try = 1; $try -le 5; $try++) {
    & $click
    $until = $script:clock.ElapsedMilliseconds + $within
    while ($script:clock.ElapsedMilliseconds -lt $until) { $v = Invoke-Cond $shown; if ($v) { return $v }; Start-Sleep -Milliseconds 50 }
  }
  WaitUntil $shown $what 100
}

# How many screen pixels one of the editor's units is: Windows' own scaling of the window, times the editor's UI
# scale as its log says it drew at (Settings > Display, or PD2SPC_UI_SCALE). For a test that counts rows or
# cells by their size: read off the window's width instead, it was right only at the scale the window is as
# wide as its reference (vault-grips under a forced scale).
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class UnitDpi { [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h); }
"@ -ErrorAction SilentlyContinue
function Unit {
  $windows = [UnitDpi]::GetDpiForWindow([IntPtr]$script:win.Current.NativeWindowHandle) / 96.0
  $line = Get-Content "$script:pad\editor.log" -ErrorAction SilentlyContinue | Select-String 'UI scale ([\d.]+)' | Select-Object -Last 1
  $editor = if ($line) { [double]::Parse($line.Matches[0].Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) } else { 1.0 }
  $windows * $editor
}

# The pointer brought onto an element's middle and left there until its tooltip is up.
function Hover($el) {
  $b = $el.Current.BoundingRectangle
  Front
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2) - 4, [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 80
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null
  Start-Sleep -Milliseconds 1400
}
# The texts of the tooltip that is up, top to bottom; one coming up as the tree is read is read again.
function TipTexts {
  for ($try = 0; $try -lt 3; $try++) {
    try {
      $texts = @()
      foreach ($tip in (All (Cond $AE::ControlTypeProperty $CT::ToolTip))) {
        foreach ($t in $tip.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text))) { if ($t.Current.Name) { $texts += $t.Current.Name } }
      }
      return $texts
    } catch { Start-Sleep -Milliseconds 250 }
  }
  @()
}

# The window at a size - asked for less than it takes, it is as small as it goes - or as wide as the screen again.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class FastSize {
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int t, bool repaint);
}
"@ -ErrorAction SilentlyContinue
function Size([int]$wide, [int]$tall) {
  $h = $script:proc.MainWindowHandle
  $script:show = 5
  [Fast]::ShowWindow($h, 9) | Out-Null; Start-Sleep -Milliseconds 300
  [FastSize]::MoveWindow($h, 60, 40, $wide, $tall, $true) | Out-Null; Start-Sleep -Milliseconds 700
  Front
}
function Wide { $script:show = 3; Front; Start-Sleep -Milliseconds 700; $script:r = $script:win.Current.BoundingRectangle }

# ---- the Items page's panes, and the list of pages that fills them ----------------------------------------------
# A page is a row of the list beside the grids: a click shows it in the first pane, Ctrl + click in the second -
# and on the page the second shows, shuts the second (a right click opens the row's
# menu). A pane's line over its grid says the page it shows.

# A right click on an element's middle.
function RightClickEl($el, [string]$what) {
  if ($null -eq $el) { throw "not found, not right-clicked: $what" }
  Front
  $b = $el.Current.BoundingRectangle
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 120
  [Fast]::mouse_event(8, 0, 0, 0, 0); Start-Sleep -Milliseconds 40; [Fast]::mouse_event(16, 0, 0, 0, 0)
}
# A click on an element's middle with Ctrl held.
function CtrlClickEl($el, [string]$what) {
  if ($null -eq $el) { throw "not found, not clicked: $what" }
  Front
  $b = $el.Current.BoundingRectangle
  [Fast]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 120
  [Fast]::keybd_event(0x11, 0, 0, 0); Start-Sleep -Milliseconds 40
  try { [Fast]::mouse_event(2, 0, 0, 0, 0); Start-Sleep -Milliseconds 40; [Fast]::mouse_event(4, 0, 0, 0, 0) }
  finally { Start-Sleep -Milliseconds 40; [Fast]::keybd_event(0x11, 0, 2, 0) }
}

# ---- the right-click menu ---------------------------------------------------------------------------------------
# One for the window: a row of icons at its head, worded rows under it. A row or an icon is found by its words -
# "Store in vault" finds "Store in vault (3)" too.

# The menu's worded rows and its icons, as they stand; none while no menu is up.
function MenuActs { @((All (Cond $AE::AutomationIdProperty 'ActMenu_Row'))) + @((All (Cond $AE::AutomationIdProperty 'ActMenu_Icon'))) }
function MenuRow([string]$label) { @(MenuActs | Where-Object { $_.Current.Name -eq $label -or $_.Current.Name -like "$label (*" })[0] }
# The worded rows as one line, top to bottom, a dimmed one in brackets: "Withdraw; [Equip]; Move to folder...".
# The menu's own - not those of the list a row of it opens beside it (Sort by), which MenuMoreWords says.
function MenuRowsIn([string]$id) { $m = ById $id; if ($m) { @($m.FindAll($TS::Descendants, (Cond $AE::AutomationIdProperty 'ActMenu_Row'))) } else { @() } }
function MenuWords { @(MenuRowsIn 'ActMenu' | ForEach-Object { if ($_.Current.IsEnabled) { $_.Current.Name } else { "[$($_.Current.Name)]" } }) -join '; ' }
function MenuMoreWords { @(MenuRowsIn 'ActMenu_More' | ForEach-Object { $_.Current.Name }) -join '; ' }
function MenuIcons { @((All (Cond $AE::AutomationIdProperty 'ActMenu_Icon')) | ForEach-Object { $_.Current.Name }) -join '; ' }
function MenuUp { @(MenuActs).Count -gt 0 }
# The menu of an element, opened by a right click on its middle and waited for.
function MenuOf($el, [string]$what) {
  RightClickEl $el $what
  WaitUntil { if (MenuUp) { 'up' } } "the menu of $what" 3000 | Out-Null
  Start-Sleep -Milliseconds 150
}
# A row or an icon of the menu that is up, picked - without the mouse: the menu closes, and the act runs.
function MenuPick([string]$label) {
  InvokeEl (WaitUntil { MenuRow $label } "the menu's '$label'" 3000) "the menu's '$label'"
  Start-Sleep -Milliseconds 250
}
# A page's row in the list, by the page's whole name (the row's own, whatever the list shows of it squeezed).
# The list scrolls (beside a small grid it is as tall as the grid): a row or a lock scrolled out of
# it is brought into sight before it is handed back - else a click on it would land under the list, on whatever
# lies there. In sight is asked by where it lies against the list's own frame, not by IsOffscreen alone.
function InPagesList($el) {
  if (-not $el -or $el.Current.IsOffscreen) { return $false }
  $list = ById 'Pane_PagesScroll'
  if (-not $list) { return $true }
  $l = $list.Current.BoundingRectangle; $b = $el.Current.BoundingRectangle
  $mid = $b.Y + $b.Height / 2
  $mid -ge $l.Y + 2 -and $mid -le $l.Y + $l.Height - 2
}
function InSightOfPages([string]$id, [string]$name) {
  $find = { @((All (Cond $AE::AutomationIdProperty $id)) | Where-Object { $_.Current.Name -eq $name }) }
  $all = & $find
  $seen = @($all | Where-Object { InPagesList $_ })[0]
  if ($seen -or -not $all.Count) { return $seen }
  $list = ById 'Pane_PagesScroll'
  $scroll = $null
  if ($list -and $list.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$scroll) -and $scroll.Current.VerticallyScrollable) {
    foreach ($to in 0, 25, 50, 75, 100) {
      $scroll.SetScrollPercent(-1, $to); Start-Sleep -Milliseconds 150
      $seen = @((& $find) | Where-Object { InPagesList $_ })[0]
      if ($seen) { return $seen }
    }
  }
}
function PageRow([string]$name) { InSightOfPages 'PageRow' $name }
# A page's lock, in the column beside the rows.
function PageLock([string]$name) { InSightOfPages 'PageRow_Lock' $name }
# A pane's line over its grid - the first pane's, or the second's: where the pane is, and what it says it shows.
function PaneLine([int]$index) { ById $(if ($index) { 'Pane2_Title' } else { 'Pane_Title' }) }
function PaneCount { if (PaneLine 1) { 2 } else { 1 } }
# The page a pane shows: its line, less the pane's number it wears while there are two.
function PaneShows([int]$index) { $line = PaneLine $index; if ($line) { $line.Current.Name -replace '^I+ . ', '' } }
# The Items page is up: its first pane's line is there.
function ItemsPage { PaneLine 0 }
# The second pane opened - on the first page the first pane does not show - or, open, shut.
function ToggleSecondPane {
  if ((PaneCount) -gt 1) {
    CtrlClickEl (PageRow (PaneShows 1)) "the second pane's row: shut"
    WaitUntil { if ((PaneCount) -eq 1) { 'one' } } "one pane shown" 4000 | Out-Null
  } else {
    $shown = PaneShows 0
    $row = @((All (Cond $AE::AutomationIdProperty 'PageRow')) | Where-Object { $_.Current.Name -ne $shown -and -not $_.Current.IsOffscreen })[0]
    CtrlClickEl $row "a row: the second pane"
    WaitUntil { if ((PaneCount) -eq 2) { 'two' } } "two panes shown" 4000 | Out-Null
  }
}
# The container a pane shows: the page's row clicked for the first pane, clicked with Ctrl for the second - which
# opens it. The same page asked of the second pane again is left as it is: a Ctrl + click there would shut it.
function Pane([int]$index, [string]$choice) {
  $row = WaitUntil { PageRow $choice } "the row of '$choice'"
  if ((PaneShows $index) -ne $choice) {
    if ($index) { CtrlClickEl $row "'$choice', the second pane" } else { ClickEl $row "'$choice', the first pane" }
  }
  WaitUntil { if ((PaneShows $index) -eq $choice) { 'shown' } } "pane $index shows '$choice'" 4000 | Out-Null
}

# Every change of the Edit page is kept as it is made: there is no KEEP to click, and
# the autosave writes a moment after the last change. Kept waits for it: until some file of the pad - a save,
# the stash, the vault and its log - is newer than when the editor was launched or Kept last answered, and
# then until nothing has been written for most of a second, so a save still on its way is over too.
$script:keptAt = ''
function PadStamps {
  $files = Get-ChildItem "$script:pad\saves", "$script:pad\vtsmart" -File -Recurse -ErrorAction SilentlyContinue
  (@($files | Sort-Object FullName | ForEach-Object { "$($_.FullName)|$($_.LastWriteTimeUtc.Ticks)|$($_.Length)" }) -join "`n")
}
function Kept([string]$what = 'the change', [int]$ms = 10000) {
  WaitUntil { (PadStamps) -ne $script:keptAt } "kept: $what" $ms | Out-Null
  do { $now = PadStamps; Start-Sleep -Milliseconds 900 } while ((PadStamps) -ne $now)
  $script:keptAt = $now
}

# The way back from a change, now that nothing is staged: the session's undo, on the top bar - once the change
# has been written, which is when it becomes a step.
function UndoLast([string]$what = 'the change') {
  Kept $what
  ClickEl (WaitUntil { $u = ByTip 'Undo ' -Prefix; if ($u -and $u.Current.IsEnabled) { $u } } "Undo ($what)") "Undo ($what)"
}

function Close { if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill(); $script:proc.WaitForExit(5000) | Out-Null } }
