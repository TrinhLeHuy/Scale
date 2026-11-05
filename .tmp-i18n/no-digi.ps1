$ErrorActionPreference = 'Stop'
# Bo ten trung gian DigiScale khoi chuoi hien thi:
#  - "DigiScale Portal" -> "Portal"
#  - khoa digi.* / weigh.* (dung chung mia + vat tu) -> token [SYS] (FRM / ERP theo luong, xem Loc.UseSystem)
#  - khoa khac (reg., ccs., quality., ...) chi dung cho mia -> "FRM"
#  - bo qua apilog.* (man nhat ky ky thuat, giu ten he thong) va approval.api_not_configured (ten khoa cau hinh)
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skip = @('approval.api_not_configured')
foreach ($lang in @('vi','en','lo','km')) {
    $path = Join-Path $root "src\TTC.Weighing.WinForms\Localization\Resources\strings.$lang.json"
    $lines = [System.IO.File]::ReadAllLines($path, $utf8)
    $changed = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $m = [regex]::Match($lines[$i], '^(\s*"([^"]+)"\s*:\s*")(.*)("\s*,?\s*)$')
        if (-not $m.Success) { continue }
        $key = $m.Groups[2].Value; $val = $m.Groups[3].Value
        if ($val -notmatch '(?i)digi') { continue }
        if ($skip -contains $key) { continue }
        if ($key.StartsWith('apilog.')) {
            $new = $val -replace 'DigiScale,\s*', ''
        } else {
            $sys = if ($key.StartsWith('digi.') -or $key.StartsWith('weigh.')) { '[SYS]' } else { 'FRM' }
            $new = $val -replace 'DigiScale Portal', 'Portal'
            $new = $new -creplace 'DIGISCALE', $sys
            $new = $new -creplace 'DigiScale', $sys
            $new = $new -creplace '\bDIGI\b', $sys
            $new = $new -creplace '\bDigi\b', $sys
        }
        if ($new -ne $val) { $lines[$i] = $m.Groups[1].Value + $new + $m.Groups[4].Value; $changed++ }
    }
    [System.IO.File]::WriteAllLines($path, $lines, $utf8)
    $null = [System.IO.File]::ReadAllText($path, $utf8) | ConvertFrom-Json
    $left = ($lines | Select-String -Pattern '(?i)":\s*".*digi').Count
    Write-Output "$lang changed=$changed remaining-with-digi=$left"
}
