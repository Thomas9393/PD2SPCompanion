# The Forge's "Paste from game", as only a window shows it:
# that the window's own clipboard reaches the page, that the window come back to the front - the way back from
# the game - makes the page look at it while an item copied with the window in front lights nothing, and that a
# click on the button makes the item, the boxes drawing what it is.
#
# What the page decides - greyed or lit and what the button says, that the Forge shown again looks again, what a
# rare, a runeword and a map pasted come to, the strip's words, the clipboard taken as it is now - is proven
# without a window: tests/PD2SPCompanion.Shell.Tests/Forge/ForgePasteTests.
#
# The texts are real copies out of the game (TestCorpus\clipboard). Each case puts the clipboard's own text back.

function Said([string]$text) { [bool](WaitUntil { ByName $text } "the strip: $text" 5000) }
function Box([string]$name) { @((All (Cond $AE::ControlTypeProperty $CT::ComboBox)) | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen })[0] }
function Shown([string]$box) { @((Box $box).FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })[0] }
function Tip { ((ById 'Forge_Paste').Current.HelpText -replace '\s+', ' ') }
# Away and back, as from the game: the window minimized, then in front again.
function AwayAndBack { [Fast]::ShowWindow($script:proc.MainWindowHandle, 6) | Out-Null; Start-Sleep -Milliseconds 500; Front; Start-Sleep -Milliseconds 300 }

# Each copied item of the corpus as one text, by what it is.
function CopiedTexts {
  $from = $script:Corpus
  $texts = @{}
  foreach ($file in 'round1.txt', 'round2.txt') {
    $all = [IO.File]::ReadAllText("$from\clipboard\$file", [Text.Encoding]::UTF8)
    foreach ($one in ($all -split '(?m)^\}\r?\n?' | Where-Object { $_.Trim() })) {
      $text = "$one}"
      if ($text -match '"name": "Blood Call"') { $texts.Rare = $text }
      if ($text -match '"runeword": "Spirit"') { $texts.Word = $text }
      if ($text -match '"type": "Demon Road Map"') { $texts.Map = $text }
    }
  }
  if ($texts.Count -ne 3) { throw "the corpus texts were not found: $($texts.Keys -join ', ')" }
  $texts
}

Case 'forge-paste.coming-back-looks-at-the-clipboard' -Tags forge, clipboard {
  $texts = CopiedTexts
  $mine = Get-Clipboard -Raw
  try {
    Set-Clipboard -Value "Harlequin Crest"
    ClickEl (WaitUntil { ById 'Bookmark_Forge' } "the Forge bookmark") "Forge"
    $paste = WaitUntil { ById 'Forge_Paste' } "the paste button"
    Start-Sleep -Milliseconds 500
    Check (-not $paste.Current.IsEnabled -and (Tip) -eq 'Paste from game') "nothing of the game's copied: greyed, '$(Tip)'"

    # Copied while the program stays in front: nothing looks (no timer).
    Set-Clipboard -Value $texts.Rare
    Start-Sleep -Milliseconds 1500
    Check (-not (ById 'Forge_Paste').Current.IsEnabled) "an item copied while the window stays in front lights nothing"

    AwayAndBack
    $paste = WaitUntil { $p = ById 'Forge_Paste'; if ($p -and $p.Current.IsEnabled) { $p } } "the button lit"
    Check ((Tip) -eq 'Paste Blood Call') "back in front: lit, '$(Tip)'"

    # Gone from the clipboard: greyed again once the window is come back to.
    Set-Clipboard -Value "nothing of the game's"
    AwayAndBack
    Check ($null -ne (WaitUntil { $p = ById 'Forge_Paste'; if ($p -and -not $p.Current.IsEnabled) { $p } } "the button greyed again")) "the clipboard emptied: greyed again"
  }
  finally {
    if ($null -ne $mine) { Set-Clipboard -Value $mine } else { Set-Clipboard -Value ' ' }
  }
}

Case 'forge-paste.click-makes-the-copied-item' -Tags forge, clipboard {
  $texts = CopiedTexts
  $mine = Get-Clipboard -Raw
  try {
    # The Forge shown looks at the clipboard: the item is on it before the bookmark is taken.
    Set-Clipboard -Value $texts.Word
    Start-Sleep -Milliseconds 300
    ClickEl (WaitUntil { ById 'Bookmark_Forge' } "the Forge bookmark") "Forge"
    $paste = WaitUntil { $p = ById 'Forge_Paste'; if ($p -and $p.Current.HelpText -match 'Spirit') { $p } } "the button names Spirit"
    Check ((Tip) -eq 'Paste Spirit') "the Forge shown looks at the clipboard: '$(Tip)'"
    ClickEl $paste "paste Spirit"
    Check ($null -ne (WaitUntil { OnEdit 'Spirit' } "Spirit in the holder")) "a click makes the runeword in the holder"
    Check ((Shown 'Base') -eq 'Crystal Sword' -and (Shown 'Rarity') -eq 'Runeword' -and (Shown 'Item') -eq 'Spirit') "the boxes: $(Shown 'Base') / $(Shown 'Rarity') / $(Shown 'Item')"
    Check (Said 'Spirit pasted.') "and the right strip says so"
  }
  finally {
    if ($null -ne $mine) { Set-Clipboard -Value $mine } else { Set-Clipboard -Value ' ' }
  }
}
