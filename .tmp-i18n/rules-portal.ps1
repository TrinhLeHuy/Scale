$ErrorActionPreference = 'Stop'
# Nguong tap chat / he so xo lay tu Portal (Quy tac can), khong phai FRM.
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)
$keys = @('quality.rules_stale_msg','quality.rules_changed_before_record','quality.rules_changed',
          'ccs.ux_rules_stale','ccs.ux_rules_stale_never','ccs.ux_fibre_changed',
          'ccs.ux_fibre_changed_before_record','ccs.ux_stale_confirm_msg')
foreach ($lang in @('vi','en','lo','km')) {
    $path = Join-Path $root "src\TTC.Weighing.WinForms\Localization\Resources\strings.$lang.json"
    $lines = [System.IO.File]::ReadAllLines($path, $utf8)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        foreach ($k in $keys) {
            if ($lines[$i].TrimStart().StartsWith('"' + $k + '":')) {
                $idx = $lines[$i].IndexOf(':')
                $lines[$i] = $lines[$i].Substring(0, $idx + 1) + ($lines[$i].Substring($idx + 1) -creplace '\bFRM\b', 'Portal')
            }
        }
    }
    [System.IO.File]::WriteAllLines($path, $lines, $utf8)
    $null = [System.IO.File]::ReadAllText($path, $utf8) | ConvertFrom-Json
    Write-Output "$lang ok"
}
