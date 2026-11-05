$ErrorActionPreference = 'Stop'
# Doi moi gio he thong sinh ra sang gio Viet Nam (VnTime) - khong phu thuoc mui gio may tram.
$root = Split-Path -Parent $PSScriptRoot
$skip = @('VnTime.cs', 'DigiScaleDate.cs')
$files = Get-ChildItem (Join-Path $root 'src') -Recurse -Include *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $skip -notcontains $_.Name }
$total = 0
foreach ($f in $files) {
    $bytes = [IO.File]::ReadAllBytes($f.FullName)
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB
    $t = [Text.Encoding]::UTF8.GetString($bytes); if ($bom) { $t = $t.Substring(1) }
    $o = $t
    $t = $t.Replace('System.DateTime.Now', 'VnTime.Now')
    $t = [regex]::Replace($t, '\.ToLocalTime\(\)', '.ToVnTime()')
    $t = [regex]::Replace($t, '\.LocalDateTime\b', '.ToVnTime().DateTime')
    $t = [regex]::Replace($t, '(?<![\w.])DateTimeOffset\.Now\b', 'VnTime.NowOffset')
    $t = [regex]::Replace($t, '(?<![\w.])DateTime\.Now\b', 'VnTime.Now')
    $t = [regex]::Replace($t, '(?<![\w.])DateTime\.Today\b', 'VnTime.Today')
    $t = $t.Replace('TimeZoneInfo.Local.GetUtcOffset(', 'VnTime.OffsetAt(')
    if ($t -ne $o) {
        if ($t -notmatch 'using TTC\.Weighing\.Domain\.Common;' -and $t -notmatch 'namespace TTC\.Weighing\.Domain\.Common') {
            # them using sau dong using cuoi cung o dau file (hoac dau file neu khong co)
            $m = [regex]::Matches($t, '(?m)^using [^\r\n]+;\r?\n')
            if ($m.Count -gt 0) { $last = $m[$m.Count - 1]; $t = $t.Insert($last.Index + $last.Length, "using TTC.Weighing.Domain.Common;`r`n") }
            else { $t = "using TTC.Weighing.Domain.Common;`r`n" + $t }
        }
        [IO.File]::WriteAllText($f.FullName, $t, (New-Object Text.UTF8Encoding($bom)))
        $total++
        Write-Output ("changed: " + $f.FullName.Substring($root.Length + 1))
    }
}
Write-Output "files changed: $total"
