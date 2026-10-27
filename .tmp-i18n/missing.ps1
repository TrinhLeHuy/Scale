$root = 'd:\Works\Scale\src'
$keys = New-Object System.Collections.Generic.HashSet[string]
Get-ChildItem $root -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object {
  $t = [IO.File]::ReadAllText($_.FullName)
  foreach ($m in [regex]::Matches($t, 'Loc\.T\(\s*"([^"]+)"')) { [void]$keys.Add($m.Groups[1].Value) }
}
foreach ($lang in 'vi','en','lo','km') {
  $p = "$root\TTC.Weighing.WinForms\Localization\Resources\strings.$lang.json"
  $json = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8) | ConvertFrom-Json
  $names = New-Object System.Collections.Generic.HashSet[string]
  $json.PSObject.Properties | ForEach-Object { [void]$names.Add($_.Name) }
  $miss = $keys | Where-Object { -not $names.Contains($_) } | Sort-Object
  Write-Output "== $lang missing: $($miss.Count)"
  $miss | ForEach-Object { Write-Output "  $_" }
}
