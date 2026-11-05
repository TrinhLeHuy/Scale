# Runs elevated from the installer after files are copied. No questions asked.
# - Initialise bundled PostgreSQL 16 in <AppDir>\data\pgdata, register Windows service TTCWeighingDB.
# - Create role/database, load db/schema.sql + db/seed.sql when the database is empty.
# - Write appsettings.Local.json = bundle-settings.json + generated connection string.
# Existing data and an existing appsettings.Local.json are never overwritten.
# Messages are ASCII on purpose (Windows PowerShell 5.1, any code page).
param(
    [Parameter(Mandatory = $true)][string]$AppDir,
    [string]$BundleSettings = ''
)

$ErrorActionPreference = 'Stop'

$dataRoot = Join-Path $AppDir 'data'
# The very first build kept data in %ProgramData%\TTCWeighing. Keep using it there instead of creating
# a second, empty database next to the app.
$legacyRoot = Join-Path $env:ProgramData 'TTCWeighing'
if (-not (Test-Path (Join-Path $dataRoot 'pgdata\PG_VERSION')) -and (Test-Path (Join-Path $legacyRoot 'pgdata\PG_VERSION'))) {
    $dataRoot = $legacyRoot
}
$logDir = Join-Path $dataRoot 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Start-Transcript -Path (Join-Path $logDir ("install-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))) | Out-Null

function Write-Utf8File([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
}

function New-Secret([int]$length = 24) {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'.ToCharArray()
    $bytes = New-Object byte[] $length
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

function Restrict-ToAdmins([string]$path) {
    # Administrators + SYSTEM only (SIDs, so it works on any Windows language).
    & icacls.exe $path /inheritance:r /grant:r '*S-1-5-32-544:F' '*S-1-5-18:F' | Out-Null
}

function Test-PortFree([int]$port) {
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $port)
    try { $listener.Start(); return $true } catch { return $false } finally { try { $listener.Stop() } catch { } }
}

function Invoke-Native([string]$exe, [string[]]$arguments) {
    # PowerShell 5.1 turns native stderr into errors; judge success by exit code only.
    $ErrorActionPreference = 'Continue'
    $output = & $exe @arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "$exe failed (exit $LASTEXITCODE): $($output -join [Environment]::NewLine)" }
    return $output
}

try {
    $localConfig = Join-Path $AppDir 'appsettings.Local.json'
    $pgBin = Join-Path $AppDir 'pgsql\bin'
    $dataDir = Join-Path $dataRoot 'pgdata'
    $secretsFile = Join-Path $dataRoot 'db-secrets.json'
    $serviceName = 'TTCWeighingDB'
    $dbName = 'ttc_weighing'
    $appRole = 'ttc_app'

    if (-not (Test-Path (Join-Path $dataDir 'PG_VERSION'))) {
        Write-Host 'Initialising new PostgreSQL cluster...'
        $port = 5432..5450 | Where-Object { Test-PortFree $_ } | Select-Object -First 1
        if (-not $port) { throw 'No free TCP port between 5432 and 5450.' }

        $secrets = [ordered]@{ Port = $port; SuperUserPassword = (New-Secret); AppPassword = (New-Secret) }
        New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
        $pwFile = Join-Path $dataRoot 'initdb.pw'
        Write-Utf8File $pwFile $secrets.SuperUserPassword
        try {
            Invoke-Native (Join-Path $pgBin 'initdb.exe') @('-D', $dataDir, '-U', 'postgres', "--pwfile=$pwFile",
                '--auth=scram-sha-256', '--encoding=UTF8', '--no-locale') | Out-Null
        } finally { Remove-Item -Force $pwFile -ErrorAction SilentlyContinue }

        # Only local connections from this machine.
        Add-Content -Encoding ASCII -Path (Join-Path $dataDir 'postgresql.conf') -Value @(
            '', '# TTC Weighing installer', "port = $port", "listen_addresses = 'localhost'")

        Write-Utf8File $secretsFile ($secrets | ConvertTo-Json)
        Restrict-ToAdmins $secretsFile
    }
    else {
        Write-Host 'Existing PostgreSQL cluster found, keeping data.'
        if (-not (Test-Path $secretsFile)) { throw "Missing $secretsFile for existing cluster $dataDir." }
    }

    $secrets = Get-Content -Raw -Path $secretsFile | ConvertFrom-Json
    $port = [int]$secrets.Port

    # Service runs as NetworkService and needs write access to the data directory.
    & icacls.exe $dataDir /grant '*S-1-5-20:(OI)(CI)M' /T /Q | Out-Null

    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Host "Registering service $serviceName..."
        Invoke-Native (Join-Path $pgBin 'pg_ctl.exe') @('register', '-N', $serviceName, '-D', $dataDir,
            '-S', 'auto', '-U', 'NT AUTHORITY\NetworkService') | Out-Null
        & sc.exe description $serviceName 'TTC Weighing - local PostgreSQL database' | Out-Null
        & sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
    }
    Start-Service -Name $serviceName

    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        & (Join-Path $pgBin 'pg_isready.exe') -h localhost -p $port -q
        if ($LASTEXITCODE -eq 0) { $ready = $true } else { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw "PostgreSQL did not become ready on port $port." }

    $psql = Join-Path $pgBin 'psql.exe'
    $env:PGCLIENTENCODING = 'UTF8'
    $env:PGPASSWORD = $secrets.SuperUserPassword
    $super = @('-h', 'localhost', '-p', "$port", '-U', 'postgres', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-tAc')

    # Passwords are generated alphanumeric only, so inline quoting is safe.
    $roleExists = (Invoke-Native $psql ($super + "SELECT 1 FROM pg_roles WHERE rolname = '$appRole'")) -join ''
    if ($roleExists.Trim() -ne '1') {
        Invoke-Native $psql ($super + "CREATE ROLE $appRole LOGIN PASSWORD '$($secrets.AppPassword)'") | Out-Null
    }
    $dbExists = (Invoke-Native $psql ($super + "SELECT 1 FROM pg_database WHERE datname = '$dbName'")) -join ''
    if ($dbExists.Trim() -ne '1') {
        Invoke-Native $psql ($super + "CREATE DATABASE $dbName OWNER $appRole ENCODING 'UTF8' TEMPLATE template0") | Out-Null
    }

    # Load schema + seed as the app role so it owns all objects. Only on an empty database.
    $env:PGPASSWORD = $secrets.AppPassword
    $app = @('-h', 'localhost', '-p', "$port", '-U', $appRole, '-d', $dbName, '-v', 'ON_ERROR_STOP=1')
    $dbScripts = Join-Path $AppDir 'setup\db'
    $hasTables = (Invoke-Native $psql ($app + @('-tAc', "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'trips'"))) -join ''
    $freshDatabase = $hasTables.Trim() -eq '0'
    if ($freshDatabase) {
        Write-Host 'Loading schema.sql and seed.sql...'
        Invoke-Native $psql ($app + @('-q', '-1', '-f', (Join-Path $dbScripts 'schema.sql'), '-f', (Join-Path $dbScripts 'seed.sql'))) | Out-Null
    }
    else {
        Write-Host 'Database already has tables, schema/seed not loaded (upgrade keeps data).'
    }

    # Schema upgrades: setup\db\upgrades\*.sql, applied once each, in file-name order.
    # A fresh database already gets the latest schema.sql, so all scripts are only recorded there.
    Invoke-Native $psql ($app + @('-q', '-c',
        'CREATE TABLE IF NOT EXISTS installer_schema_upgrades (script text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())')) | Out-Null
    $applied = @(Invoke-Native $psql ($app + @('-tAc', 'SELECT script FROM installer_schema_upgrades')) | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $upgradeDir = Join-Path $dbScripts 'upgrades'
    $pending = @(Get-ChildItem -Path $upgradeDir -Filter '*.sql' -File -ErrorAction SilentlyContinue |
        Where-Object { $applied -notcontains $_.Name } | Sort-Object Name)
    foreach ($script in $pending) {
        if ($script.Name -notmatch '^[0-9A-Za-z_.-]+\.sql$') { throw "Invalid upgrade script name: $($script.Name)" }
    }

    if ($pending.Count -gt 0 -and $freshDatabase) {
        foreach ($script in $pending) {
            Invoke-Native $psql ($app + @('-q', '-c', "INSERT INTO installer_schema_upgrades (script) VALUES ('$($script.Name)')")) | Out-Null
        }
        Write-Host "Recorded $($pending.Count) upgrade script(s) as included in schema.sql."
    }
    elseif ($pending.Count -gt 0) {
        # Back up before changing the schema of a database that holds real data.
        $backupDir = Join-Path $dataRoot 'backups'
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
        $backup = Join-Path $backupDir ("before-upgrade-{0:yyyyMMdd-HHmmss}.dump" -f (Get-Date))
        Write-Host "Backing up database to $backup..."
        Invoke-Native (Join-Path $pgBin 'pg_dump.exe') @('-h', 'localhost', '-p', "$port", '-U', $appRole, '-d', $dbName, '-Fc', '-f', $backup) | Out-Null

        foreach ($script in $pending) {
            Write-Host "Applying upgrade $($script.Name)..."
            # Script + its record in one transaction: a failing script leaves the database unchanged.
            Invoke-Native $psql ($app + @('-q', '-1', '-f', $script.FullName, '-c',
                "INSERT INTO installer_schema_upgrades (script) VALUES ('$($script.Name)')")) | Out-Null
        }
    }
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue

    if (Test-Path $localConfig) {
        Write-Host "Keeping existing $localConfig."
    }
    else {
        $config = if ($BundleSettings -and (Test-Path $BundleSettings)) {
            Get-Content -Raw -Encoding UTF8 -Path $BundleSettings | ConvertFrom-Json
        } else { New-Object PSObject }
        $config.PSObject.Properties.Remove('_comment')
        $config.PSObject.Properties.Remove('ConnectionStrings')
        $config | Add-Member -NotePropertyName ConnectionStrings -NotePropertyValue ([pscustomobject]@{
            WeighingDb = "Host=localhost;Port=$port;Database=$dbName;Username=$appRole;Password=$($secrets.AppPassword)"
        })
        if (-not $config.PSObject.Properties['Site']) {
            $config | Add-Member -NotePropertyName Site -NotePropertyValue ([pscustomobject]@{ Code = 'TTC-01' })
        }
        Write-Utf8File $localConfig ($config | ConvertTo-Json -Depth 10)
        Write-Host "Wrote $localConfig."
    }

    Write-Host 'Configuration finished.'
    exit 0
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)"
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
