$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)
$data = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'synclog.json'), $utf8) | ConvertFrom-Json
foreach ($lang in @('vi','en','lo','km')) {
    $path = Join-Path $root "src\TTC.Weighing.WinForms\Localization\Resources\strings.$lang.json"
    $lines = [System.Collections.Generic.List[string]]::new([System.IO.File]::ReadAllLines($path, $utf8))
    $added = 0
    foreach ($p in $data.$lang.PSObject.Properties) {
        $val = ($p.Value | ConvertTo-Json -Compress)
        $entry = '  "' + $p.Name + '": ' + $val
        $idx = -1
        for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].TrimStart().StartsWith('"' + $p.Name + '":')) { $idx = $i; break } }
        if ($idx -ge 0) {
            $comma = if ($lines[$idx].TrimEnd().EndsWith(',')) { ',' } else { '' }
            $lines[$idx] = $entry + $comma
        } else {
            # insert right after the opening brace
            $lines.Insert(1, $entry + ',')
            $added++
        }
    }
    [System.IO.File]::WriteAllLines($path, $lines, $utf8)
    $null = [System.IO.File]::ReadAllText($path, $utf8) | ConvertFrom-Json
    Write-Output "$lang ok (+$added)"
}
