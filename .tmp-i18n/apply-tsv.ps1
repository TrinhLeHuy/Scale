param([string]$Prefix)
$res = 'd:\Works\Scale\src\TTC.Weighing.WinForms\Localization\Resources'
$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($lang in 'vi','en','lo','km') {
  $tsv = "d:\Works\Scale\.tmp-i18n\$Prefix.$lang.tsv"
  $path = "$res\strings.$lang.json"
  $hadBom = ([IO.File]::ReadAllBytes($path)[0] -eq 0xEF)
  $lines = [System.Collections.Generic.List[string]]([IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8))
  $pairs = [IO.File]::ReadAllLines($tsv, [Text.Encoding]::UTF8) | Where-Object { $_ -match "`t" }
  $updated = 0; $added = 0
  $anchor = -1
  for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^\s*"common\.error"\s*:') { $anchor = $i; break } }
  if ($anchor -lt 0) { throw "anchor missing in $lang" }
  foreach ($p in $pairs) {
    $k, $v = $p -split "`t", 2
    $json = ($v | ConvertTo-Json)
    $newLine = '  "' + $k + '": ' + $json + ','
    $found = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
      if ($lines[$i] -match ('^\s*"' + [regex]::Escape($k) + '"\s*:')) {
        $trail = $lines[$i].TrimEnd().EndsWith(',')
        $lines[$i] = if ($trail) { $newLine } else { $newLine.TrimEnd(',') }
        $found = $true; $updated++
      }
    }
    if (-not $found) { $anchor++; $lines.Insert($anchor, $newLine); $added++ }
  }
  $enc = if ($hadBom) { New-Object System.Text.UTF8Encoding($true) } else { $utf8 }
  [IO.File]::WriteAllLines($path, $lines, $enc)
  $null = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) | ConvertFrom-Json
  "$lang updated=$updated added=$added"
}
