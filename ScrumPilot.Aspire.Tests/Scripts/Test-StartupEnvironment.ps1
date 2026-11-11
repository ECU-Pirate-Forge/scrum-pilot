$ErrorActionPreference = 'Stop'
$directory = Join-Path ([IO.Path]::GetTempPath()) ('scrumpilot-script-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$names = @('ArtifactsPath', 'UseArtifactsOutput', 'Infrastructure__SqlOnly',
    'Infrastructure__TestConnectionFile', 'SqlServer__BackupApplicationPath', 'SqlServer__BackupMountSource')
foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, 'caller-value', 'Process') }

# Mock CLI execution only in this environment-scope unit test. The separate
# BackupIntegration test always executes real SQL BACKUP and file verification.
function dotnet { $global:LASTEXITCODE = 0 }
function aspire {
    if ($args[0] -eq 'start') {
        @{
            SCRUMPILOT_TEST_SQL_CONNECTION = 'fixture-value'
            SCRUMPILOT_TEST_SQL_BACKUP_SERVER_PATH = '/sql/backups'
            SCRUMPILOT_TEST_SQL_BACKUP_APPLICATION_PATH = $directory
        } | ConvertTo-Json | Set-Content -LiteralPath $env:Infrastructure__TestConnectionFile
    }
    $global:LASTEXITCODE = 0
}
try {
    $helper = Join-Path $PSScriptRoot 'Start-TestSql.ps1'
    $state = Join-Path $directory 'state'
    & $helper -ArtifactsPath (Join-Path $directory 'artifacts') -StateDirectory $state -BackupDirectory (Join-Path $directory 'backups') | Out-Null
    foreach ($name in $names) {
        if ([Environment]::GetEnvironmentVariable($name, 'Process') -ne 'caller-value') {
            throw "Startup variable was not restored: $name"
        }
    }
    if ($env:SCRUMPILOT_TEST_SQL_CONNECTION -ne 'fixture-value') { throw 'Fixture exports were not retained.' }
    # Fail after startup-only variables have changed, then prove finally restored them.
    function aspire { throw 'intentional-start-failure' }
    $failed = $false
    try {
        & $helper -ArtifactsPath (Join-Path $directory 'artifacts') -StateDirectory $state -BackupDirectory (Join-Path $directory 'backups') | Out-Null
    } catch { $failed = $_.Exception.Message -eq 'intentional-start-failure' }
    if (!$failed) { throw 'Expected startup failure was not observed.' }
    foreach ($name in $names) {
        if ([Environment]::GetEnvironmentVariable($name, 'Process') -ne 'caller-value') {
            throw "Failure path did not restore startup variable: $name"
        }
    }
    Write-Output 'Startup environment success/failure restoration passed.'
} finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
