[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ArtifactsPath,
    [Parameter(Mandatory)][string] $StateDirectory,
    [string] $BackupDirectory,
    [string] $WslDistribution,
    [switch] $GitHubEnvironment
)
$ErrorActionPreference = 'Stop'
$startupVariables = @(
    'ArtifactsPath', 'UseArtifactsOutput', 'Infrastructure__SqlOnly',
    'Infrastructure__TestConnectionFile', 'SqlServer__BackupApplicationPath',
    'SqlServer__BackupMountSource'
)
$previous = @{}
foreach ($name in $startupVariables) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
$env:SqlServer__BackupMountSource = $null
$appHost = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../ScrumPilot.AppHost.csproj'))
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
$StateDirectory = [IO.Path]::GetFullPath($StateDirectory)
New-Item -ItemType Directory -Force $StateDirectory | Out-Null
if ($IsWindows) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls $StateDirectory /inheritance:r /grant:r "${identity}:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE) { throw 'Could not restrict SQL state directory.' }
} else {
    & chmod 700 $StateDirectory
    if ($LASTEXITCODE) { throw 'Could not restrict SQL state directory.' }
}
if ($IsWindows -and $WslDistribution) {
    # SQL BACKUP needs a native Linux filesystem; Docker Desktop Windows shares
    # can fail DiskChangeFileSize. WSL exposes the SAME ext4 files to the API host.
    $scope = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($appHost))).Substring(0, 16).ToLowerInvariant()
    $linuxDirectory = & wsl --distribution $WslDistribution --exec sh -c `
        'set -eu; umask 077; p="$HOME/.local/share/scrumpilot/aspire-test-sql/$1"; mkdir -p "$p"; setfacl -m "u:10001:rwx,d:u:10001:rwx,d:u:$(id -u):rwx,d:o::---" "$p"; printf "%s" "$p"' sh $scope
    if ($LASTEXITCODE) { throw 'Could not prepare restricted native WSL backup directory.' }
    $backups = "\\wsl.localhost\$WslDistribution$($linuxDirectory.Replace('/', '\'))"
    # Share only this owned ext4 directory with Docker Desktop, without changing
    # the user's Docker Desktop distro integration settings.
    & wsl --distribution $WslDistribution --user root --exec sh -c `
        'set -eu; p="/mnt/wsl/scrumpilot-$1"; mkdir -p "$p"; mountpoint -q "$p" || mount --bind "$2" "$p"' sh $scope $linuxDirectory
    if ($LASTEXITCODE) { throw 'Could not share the owned native backup directory with Docker Desktop.' }
    $env:SqlServer__BackupMountSource = "/run/desktop/mnt/host/wsl/scrumpilot-$scope"
} else {
    if ($IsWindows -and !$BackupDirectory) {
        throw 'On Windows, specify -WslDistribution or a SQL-compatible native Linux -BackupDirectory UNC path.'
    }
    $backups = if ($BackupDirectory) { [IO.Path]::GetFullPath($BackupDirectory) } else { Join-Path $StateDirectory 'backups' }
    New-Item -ItemType Directory -Force $backups | Out-Null
    if (!$IsWindows) {
        $uid = & id -u
        & chmod 700 $backups
        & setfacl -m "u:10001:rwx,d:u:10001:rwx,d:u:${uid}:rwx,d:o::---" $backups
        if ($LASTEXITCODE) { throw 'Could not grant SQL UID 10001 and API host restricted backup access (install acl).' }
    }
}
$connectionFile = Join-Path $StateDirectory 'connection-env.json'
dotnet build $appHost --artifacts-path $ArtifactsPath -p:BuildProjectReferences=false "-bl:$(Join-Path $ArtifactsPath ('sql-' + [guid]::NewGuid().ToString('N') + '.binlog'))" --nologo
if ($LASTEXITCODE) { throw 'AppHost build failed.' }
$env:ArtifactsPath = $ArtifactsPath
$env:UseArtifactsOutput = 'true'
$env:Infrastructure__SqlOnly = 'true'
$env:Infrastructure__TestConnectionFile = $connectionFile
$env:SqlServer__BackupApplicationPath = $backups
$startedAt = [DateTime]::UtcNow
aspire start --no-build --non-interactive --isolated --apphost $appHost
if ($LASTEXITCODE) { throw 'SQL-only AppHost start failed.' }
aspire wait sql --apphost $appHost --non-interactive --timeout 180
if ($LASTEXITCODE) { throw 'SQL2025 did not become healthy.' }
if (!(Test-Path -LiteralPath $connectionFile)) { throw 'AppHost did not export the required test connection file.' }
if ((Get-Item -LiteralPath $connectionFile).LastWriteTimeUtc -lt $startedAt) {
    throw 'The test connection export is stale; stop this exact AppHost before starting a new fixture.'
}
$values = Get-Content $connectionFile -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($values.SCRUMPILOT_TEST_SQL_CONNECTION)) {
    throw 'The SQL test connection export is empty.'
}
foreach ($property in $values.PSObject.Properties) {
    [Environment]::SetEnvironmentVariable($property.Name, $property.Value, 'Process')
    if ($GitHubEnvironment) {
        if (!$env:GITHUB_ENV) { throw 'GITHUB_ENV is not set.' }
        # GitHub consumes this control record; values are never logged unmasked.
        $mask = ([string]$property.Value).Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
        Write-Output "::add-mask::$mask"
        Add-Content -Path $env:GITHUB_ENV -Value "$($property.Name)=$($property.Value)"
    }
}
$tests = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ScrumPilot.Aspire.Tests/ScrumPilot.Aspire.Tests.csproj'))
# The dashboard can briefly report a reused persistent container's old health
# while remounting. Prove the current exported endpoint and real backup mapping,
# not just a TCP listener or a stale health snapshot.
dotnet test $tests --artifacts-path $ArtifactsPath -p:BuildProjectReferences=false `
    --filter Category=BackupIntegration --nologo `
    "-bl:$(Join-Path $ArtifactsPath ('fixture-' + [guid]::NewGuid().ToString('N') + '.binlog'))"
if ($LASTEXITCODE) { throw 'The SQL17 connection/real-backup fixture verification failed.' }
Write-Output "SQL2025 is ready. Restricted fixture input: $connectionFile"
} finally {
    foreach ($name in $startupVariables) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
# Keep this exact graph alive until consumers finish, then:
# aspire stop --non-interactive --apphost <this exact csproj>
# Never force-stop: persistent SQL data and backups need an explicit retention decision.
