# Runs on uninstall: stop and remove the local database service.
# Data in %ProgramData%\TTCWeighing (pgdata, db-secrets.json, logs) is kept on purpose.
$serviceName = 'TTCWeighingDB'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }
    & sc.exe delete $serviceName | Out-Null
}
exit 0
