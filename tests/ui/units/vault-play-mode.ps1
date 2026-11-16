# The play mode a vault keeps, as only a window shows it: that the question comes up over the window when the play mode is changed under an open
# vault, that neither Escape nor Enter answers it, and that each of its two answers is there to click.
#
# Everything the keeping decides - the mode taken unasked, when it asks and in what words, what each answer does,
# custom modes under their names and the rules of before as "(OLD)" - is proven without a window:
# tests/PD2SPCompanion.Shell.Tests/Vault/VaultPlayModeTests.
#
# The keeping is on for this case alone (PD2SPC_VAULT_MODE; fast.ps1 has it off for every other), so it has a
# start of the program to itself.
Add-Type -AssemblyName System.Windows.Forms

# The middle dot of the Vault row's line, by its number: this file is read as the system's code page.
$dot = [char]0x00B7

function Says([string]$part) {
  Steady {
    foreach ($e in (All (Cond $AE::ControlTypeProperty $CT::Text))) {
      if ($e.Current.Name.Contains($part) -and -not $e.Current.IsOffscreen) { return $e }
    }
  }
}
function Keys([string]$keys) { Front; [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 300 }
# The question: its text, and an answer by its label.
function Asked([string]$part) { WaitUntil { Says $part } "the question: $part" 5000 }
function Answer([string]$label) { ClickEl (WaitUntil { ByName $label } "the answer $label" 3000) $label; WaitUntil { if (-not (Says ' vault. Play mode is ')) { 'answered' } } "the question gone" 3000 | Out-Null }
function Stored { Get-Content (SettingsFile) -Raw | ConvertFrom-Json }

Case 'vault-play-mode.question-takes-a-click-not-a-key' -Tags vault, vault-play-mode, settings -Start Home -Mode Legit -Before {
  # What this case is about: on, where the other cases have it off (fast.ps1).
  $env:PD2SPC_VAULT_MODE = ''
} {
  try {
    ClickEl (WaitUntil { ById 'SideBar_Settings' } "Settings") "Settings"

    # A vault that keeps none takes the mode it is opened under, unasked.
    Check ([bool](WaitUntil { Says "$dot Legit" } "the Vault row" 8000)) "opened under Legit: the vault is a Legit vault, unasked"
    Check (-not (Says ' vault. Play mode is ')) "no question"

    # The play mode changed while it is open: asked. A key does not answer; the vault's mode is one answer ...
    ClickEl (ByName 'FREE') "Free"
    Check ([bool](Asked "$(MainVault) is a Legit vault. Play mode is Free.")) "Free chosen: the question says both"
    Keys '{ESC}'; Keys '{ENTER}'
    Check ([bool](Says "$(MainVault) is a Legit vault.")) "Escape and Enter do not answer it"
    if ($env:PD2SPC_SHOT_DIR) { Shot (Join-Path $env:PD2SPC_SHOT_DIR 'vault-play-mode.png') }
    Answer 'USE LEGIT'
    Check ([bool](WaitUntil { if ((Stored).PlayMode -eq 'Legit') { 'legit' } } "Legit again" 3000)) "USE LEGIT: the play mode is Legit again"

    # ... and the vault changed to this one is the other.
    ClickEl (ByName 'FREE') "Free"
    Asked "$(MainVault) is a Legit vault." | Out-Null
    Answer 'CHANGE VAULT'
    Check ([bool](WaitUntil { Says "$dot Free" } "the Vault row" 3000)) "CHANGE VAULT: the vault is a Free vault"
    Check ((Stored).PlayMode -eq 'Free') "and the play mode stays Free"
  }
  finally { $env:PD2SPC_VAULT_MODE = 'off' }
}
