$needle = '3776e80f-67d3-f111-b816-005056b47d38'
$cfg = Get-Content src\TTC.Weighing.WinForms\appsettings.Development.json -Raw | ConvertFrom-Json
$f = $cfg.FrmPlanLookup
$auth = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($f.Username):$($f.Password)"))
foreach ($e in @('LenhDon','the-xe','xe-van-chuyen','hop-dong-van-chuyen')) {
  for ($p = 1; $p -le 50; $p++) {
    $body = @{ entities = $e; busineesUnit = 'KSK'; Date = '1900-01-01 00:00:00'; page = $p; pageSize = 200 } | ConvertTo-Json
    $r = Invoke-WebRequest -Uri "$($f.BaseUrl)/api/v1/integration/frm" -Method Post -Body $body -ContentType 'application/json; charset=utf-8' -Headers @{ Authorization = "Basic $auth" } -UseBasicParsing -TimeoutSec 120
    $c = [Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray())
    $i = $c.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase)
    if ($i -ge 0) {
      $j = $c | ConvertFrom-Json
      foreach ($it in $j.results[0].items) {
        $raw = $it | ConvertTo-Json -Depth 6 -Compress
        if ($raw.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
          Write-Output "== FOUND in $e page $p : MaLD=$($it.fields.MaLD)"
          $it.fields.PSObject.Properties | Where-Object { "$($_.Value)" -ieq $needle } | ForEach-Object { "  field: $($_.Name)" }
          if ($it.sourceKey -ieq $needle) { '  field: sourceKey' }
        }
      }
    }
    $j2 = $c | ConvertFrom-Json
    if (-not $j2.results[0].hasNext) { break }
  }
}
# FRM direct retrieve lenh don
$p2 = $cfg.FrmPolicyApi
$auth2 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($p2.Username):$($p2.Password)"))
$b = '{"logicalName":"new_lenhdon","busineesUnit":"KSK","status":"Retrieve","entityRequest":[{"Date":"10/01/2026 00:00:00"}]}'
$r = Invoke-WebRequest -Uri ("{0}/api/crm/retrieve" -f $p2.BaseUrl) -Method Post -Body ([Text.Encoding]::UTF8.GetBytes($b)) -ContentType 'application/json; charset=utf-8' -Headers @{ Authorization = "Basic $auth2"; Accept = 'application/vnd.bms+json; version=1.0' } -UseBasicParsing -TimeoutSec 300
$c = [Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray())
if ($c.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
  $j = $c | ConvertFrom-Json
  foreach ($ld in $j.entityRequest[0].LenhDon) {
    $hit = $ld.PSObject.Properties | Where-Object { "$($_.Value)" -ieq $needle }
    if ($hit) { Write-Output "== FOUND in FRM retrieve new_lenhdon MaLD=$($ld.MaLD) field(s): $(($hit | % Name) -join ',')" }
  }
} else { Write-Output "FRM retrieve new_lenhdon: not found (Count=$((($c | ConvertFrom-Json).Count)))" }
Write-Output "done"
