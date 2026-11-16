# REMOVE on the Experimental page asks first, as only a window shows it: it deletes files
# in the game folder, and no undo of the program's brings them back. Pressed only where the page's files are
# installed, and answered no with Esc: the install's marker must still be there afterwards. With nothing
# installed the button cannot be pressed, and that is what is looked at. Never writes the game folder.
#
# That the page asks before anything is removed, and that no leaves the game folder as it was, is proven
# without a window: tests/PD2SPCompanion.Shell.Tests/Experimental/RemoveAsksTests.
Add-Type -AssemblyName System.Windows.Forms

function Asked { @((All (Cond $AE::ControlTypeProperty $CT::Text)) | Where-Object { $_.Current.Name -eq 'Remove mod files' -and -not $_.Current.IsOffscreen }).Count -gt 0 }

Case 'remove-asks.remove-asks-and-esc-answers-no' -Tags remove-asks, experimental, keys {
  $gameRoot = $script:GameRoot
  $marker = "$gameRoot\ProjectD2\data\pd2spc-density.json"
  $installed = Test-Path $marker

  ClickEl (WaitUntil { ById 'SideBar_Experimental' } "the Experimental button") "Experimental"
  $remove = WaitUntil { ByName 'REMOVE' } "the REMOVE button"

  if (-not $installed) {
    Check (-not $remove.Current.IsEnabled) "nothing installed: REMOVE cannot be pressed (the question is not reached on this machine)"
  }
  else {
    ClickEl $remove "REMOVE"
    Check ($null -ne (WaitUntil { if (Asked) { 'asked' } } "the question" 4000)) "REMOVE asks: 'Remove mod files'"
    Check (Test-Path $marker) "nothing is removed while it asks"
    Front; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 500
    Check (-not (Asked)) "Esc answers no: the question gone"
    Check (Test-Path $marker) "and the install is as it was"
  }
}
