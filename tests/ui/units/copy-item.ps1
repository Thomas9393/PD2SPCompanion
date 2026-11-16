# An item out, as only a window shows it:
# that Ctrl + C, the game's own key over an item, reaches the inventory page and the vault page, and that what
# the page hands over is on the window's clipboard after - left alone where nothing is selected.
#
# What the pages decide - the text for one item and for several, what the strips say, the Forge making the one
# copied again whole - is proven without a window: tests/PD2SPCompanion.Shell.Tests/Forge/CopyItemTests.
#
# Nothing is changed in any file. Each case puts the clipboard's own text back, and the pane it turned to the
# personal stash back to the inventory. The key is sent through the keyboard: not run under another input language.

Add-Type -AssemblyName System.Windows.Forms
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 500 }
function Strip([string]$text, [int]$ms = 4000) { try { [bool](WaitUntil { ByName $text } "the strip: $text" $ms) } catch { $false } }
function Copied { try { [string](Get-Clipboard -Raw) } catch { '' } }
function Objects([string]$text) { ([regex]::Matches($text, '"pd2spc"')).Count }

Case 'copy-item.ctrl-c-copies-the-selected-item' -Tags clipboard, items, keys -Typing {
  $mine = try { Get-Clipboard -Raw } catch { $null }
  try {
    Set-Clipboard -Value "nothing of an item"
    Pane 0 'Personal Stash'
    Start-Sleep -Milliseconds 400

    # ---- nothing selected: said, and the clipboard is left alone
    $ring = 'Steel Ring of Skill'
    $first = WaitUntil { Tile $ring } "the ring"
    # The page pressed in first, so the keys are its: the ring selected, and let go of.
    ClickEl $first $ring
    Keys '{ESC}'
    Keys '^c'
    Check ((Strip 'No item selected.') -and (Copied) -eq 'nothing of an item') "Ctrl + C with nothing selected: 'No item selected.', the clipboard untouched"

    # ---- one item of the inventory
    ClickEl $first $ring
    Keys '^c'
    $text = Copied
    Check (Strip "$ring copied.") "Ctrl + C on the ring: '$ring copied.'"
    Check ((Objects $text) -eq 1 -and $text -match '"quality":\s*"Magic"' -and $text -match '"type":\s*"Ring"' -and $text -match '"stat_id"') "the clipboard holds it in the game's shape, with the program's key ($($text.Length) characters)"
    Keys '{ESC}'
    Pane 0 'Inventory'
  }
  finally {
    if ($null -ne $mine) { Set-Clipboard -Value $mine } else { Set-Clipboard -Value ' ' }
  }
}

Case 'copy-item.ctrl-c-reaches-the-vault-page' -Tags clipboard, vault, keys -Typing {
  $mine = try { Get-Clipboard -Raw } catch { $null }
  try {
    Set-Clipboard -Value "nothing of an item"
    ClickEl (ById 'Bookmark_Vault_Left') "vault (left)"
    WaitUntil { ById 'Vault_Pages' } "the vault's sheet" | Out-Null
    VaultGoTo 'Small'
    $charm = 'Small Charm of Fortune'
    ClickEl (WaitUntil { Tile $charm } "the charm") $charm
    Keys '^c'
    Check (Strip "$charm copied.") "Ctrl + C on a vault item: '$charm copied.'"
    Check ((Copied) -match '"type":\s*"Small Charm"') "and the clipboard holds the charm"
    Keys '{ESC}'
    # The vault's top again, where the next case looks for it.
    VaultGoTo 'Vault'
  }
  finally {
    if ($null -ne $mine) { Set-Clipboard -Value $mine } else { Set-Clipboard -Value ' ' }
  }
}
