[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $WslDistribution,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string] $StorageName = 'local'
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'This helper requires Windows with WSL and Docker Desktop. On Linux, configure a restricted native backup directory directly.' }
$appHost = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\ScrumPilot.AppHost.csproj'))
$scope = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes($appHost))).Substring(0, 16).ToLowerInvariant()
$linuxDirectory = & wsl --distribution $WslDistribution --exec sh -c `
    'set -eu; umask 077; p="$HOME/.local/share/scrumpilot/$1/$2"; mkdir -p "$p"; setfacl -m "u:10001:rwx,d:u:10001:rwx,d:u:$(id -u):rwx,d:o::---" "$p"; printf "%s" "$p"' sh $StorageName $scope
if ($LASTEXITCODE) { throw 'Could not prepare restricted native WSL backup storage. Verify the distribution and install the acl package (setfacl).' }
$applicationPath = "\\wsl.localhost\$WslDistribution$($linuxDirectory.Replace('/', '\'))"
# Share only this worktree's ext4 directory, without changing Docker Desktop integration.
$share = "scrumpilot-$StorageName-$scope"
& wsl --distribution $WslDistribution --user root --exec sh -c `
    'set -eu; p="/mnt/wsl/$1"; mkdir -p "$p"; if mountpoint -q "$p"; then test "$(stat -c %d:%i "$p")" = "$(stat -c %d:%i "$2")"; else mount --bind "$2" "$p"; fi' sh $share $linuxDirectory
if ($LASTEXITCODE) { throw 'Could not share the owned native backup directory with Docker Desktop. An existing mount must refer to the same directory.' }
if (!(Test-Path -LiteralPath $applicationPath -PathType Container)) {
    throw 'The API host cannot access the prepared WSL backup directory.'
}
[pscustomobject]@{
    ApplicationPath = $applicationPath
    MountSource = "/run/desktop/mnt/host/wsl/$share"
}
