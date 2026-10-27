$path = 'd:\Works\Scale\src\TTC.Weighing.WinForms\Forms\QualityForm.cs'
$bytes = [IO.File]::ReadAllBytes($path)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$text = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)

function Cut([string]$t, [string]$startMarker, [string]$endMarker) {
    $s = $t.IndexOf($startMarker)
    $e = $t.IndexOf($endMarker, $s + 1)
    if ($s -lt 0 -or $e -lt 0) { throw "marker not found: $startMarker" }
    return $t.Remove($s, $e - $s)
}

# Old 2-number manual popup and its submit handler (replaced by one-number-per-weighing entry).
$text = Cut $text '    private void ToggleManualEntry()' '    private void UpdateManualPreview()'
$text = Cut $text '    private async Task RecordImpurityManualAsync()' '    private async void HandleImpurityResult('
# Old auto-capture state machine.
$text = Cut $text '    private void ProcessAutoWeighStep(SampleScaleView view)' '    private void FinalizeAutoWeigh()'

[IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $hasBom))
'cut ok'
