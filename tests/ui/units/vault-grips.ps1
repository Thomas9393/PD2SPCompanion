# The grip of the vault page: the folder tree's edge carries a grip to take hold of.
# Dragged, it makes the tree wider or narrower - never under its least width; dragged left past half of that the
# tree shuts, and let go of there it is put away - its button brings it back as wide as it was. All of it is the
# pointer's and the window's measuring. (A folder's folders are tiles of the sheet,
# with no grip of their own.)
#
# What the page keeps of it - the widths and rows it will not go under or over, the part its edge alone while
# dragged shut, the size it comes back at - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Vault/VaultRoomTests.
#
# The window is maximized; nothing in the vault is changed. The tree stays as it was dragged while the program
# runs, so the case has the program closed after it.

function Any([string]$id) { Steady { @((All (Cond $AE::AutomationIdProperty $id)) | Where-Object { -not $_.Current.IsOffscreen })[0] } }
function Box([string]$id) { $e = Any $id; if ($e) { $e.Current.BoundingRectangle } }
# A drag by the left button from one screen point to another, slowly enough to be followed.
function Pull([double]$x, [double]$y, [double]$dx, [double]$dy) {
  Front
  [Fast]::SetCursorPos([int]$x, [int]$y) | Out-Null; Start-Sleep -Milliseconds 150
  [Fast]::mouse_event(2, 0, 0, 0, 0); Start-Sleep -Milliseconds 80
  for ($i = 1; $i -le 16; $i++) { [Fast]::SetCursorPos([int]($x + $dx * $i / 16), [int]($y + $dy * $i / 16)) | Out-Null; Start-Sleep -Milliseconds 15 }
  Start-Sleep -Milliseconds 150
  [Fast]::mouse_event(4, 0, 0, 0, 0); Start-Sleep -Milliseconds 400
}
# The tree's grip: its edge, at the tree's middle - found by itself, not from the rows' width: the column of counts
# and locks lies beside the rows.
function TreeGrip { $e = Box 'Vault_TreeEdge'; $t = Box 'Vault_Tree'; @(($e.X + $e.Width / 2), ($t.Y + $t.Height / 2)) }

# The vault on the left page, at its top.
function OpenVault {
  ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
  WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
  Start-Sleep -Milliseconds 600
  VaultGoTo 'Vault'
}

Case 'vault-grips.tree-grip-widens-narrows-and-shuts' -Tags vault, vault-grips, drag, layout -Changes {
  OpenVault
  # The pixels of an editor unit, asked (fast.ps1's Unit): it was guessed from the window's width, which is
  # right only while the window is as wide as its reference - not under a UI scale told.
  $scale = Unit
  "  an editor unit is $scale px"

  # ---- the tree: wider by its grip
  $was = (Box 'Vault_TreePane').Width
  $g = TreeGrip
  Pull $g[0] $g[1] 80 0
  $wide = (Box 'Vault_TreePane').Width
  Check ([math]::Abs($wide - $was - 80) -le 8) "the tree's grip dragged right by 80: the tree is wider by $([int]($wide - $was))"

  # ---- narrower, and never under its least width while the pointer stays near it
  $g = TreeGrip
  Pull $g[0] $g[1] (-($wide - 100 * $scale)) 0
  $least = (Box 'Vault_TreePane').Width
  Check ($null -ne (Any 'Vault_Tree') -and $least -ge 80 * $scale -and $least -le 110 * $scale) "dragged to about its least width: it stays, $([int]$least) px wide"

  # ---- wide again, then dragged to the page's edge: put away, and back by its button as wide as it was
  $g = TreeGrip
  Pull $g[0] $g[1] 120 0
  $before = (Box 'Vault_TreePane').Width
  $g = TreeGrip
  Pull $g[0] $g[1] (-($before + 40)) 0
  Check ($null -eq (Any 'Vault_Tree')) "dragged left to the end: the tree is put away"
  Check ($null -ne (Any 'Vault_Pages')) "and the sheet is there"
  ClickEl (Any 'Vault_TreeToggle') "the tree's button"
  $back = try { WaitUntil { Box 'Vault_Tree' } "the tree back" 3000 | Out-Null; (Box 'Vault_TreePane').Width } catch { 0 }
  Check ([math]::Abs($back - $before) -le 4) "its button brings it back as wide as it was ($([int]$back) against $([int]$before)), not at nothing"
}
