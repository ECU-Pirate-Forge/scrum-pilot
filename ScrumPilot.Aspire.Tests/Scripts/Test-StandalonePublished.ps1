[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PublishDirectory,
    [Parameter(Mandatory)][string] $FixtureFile,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{12,64}$')][string] $FixtureContainerId,
    [Parameter(Mandatory)][string] $StateDirectory
)
# Run in pwsh. NODE_PATH must resolve operator-installed Playwright; this does not
# modify either client's packages. Uses an existing Aspire-owned SQL test fixture.
$ErrorActionPreference = 'Stop'
$processes = [Collections.Generic.List[object]]::new()
$ownedCatalog = $false

function Start-PrivateProcess($file, $arguments, $environment = @{}, $directory = $PWD.Path) {
    $start = [Diagnostics.ProcessStartInfo]::new($file)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardInput = $true
    $start.WorkingDirectory = $directory
    foreach ($argument in $arguments) { $start.ArgumentList.Add([string]$argument) }
    foreach ($key in $environment.Keys) { $start.Environment[$key] = [string]$environment[$key] }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    [void]$process.Start()
    $entry = [pscustomobject]@{
        Process = $process
        Output = $process.StandardOutput.ReadToEndAsync()
        ErrorOutput = $process.StandardError.ReadToEndAsync()
    }
    $processes.Add($entry)
    return $entry
}

function Complete-PrivateProcess($entry, $label, $seconds = 60) {
    if (!$entry.Process.WaitForExit($seconds * 1000)) { throw "$label exceeded its bounded deadline." }
    if ($entry.Process.ExitCode) { throw "$label exited with code $($entry.Process.ExitCode); private output withheld." }
    return $entry.Output.GetAwaiter().GetResult()
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}

function Invoke-FixtureSql($query) {
    $entry = Start-PrivateProcess 'docker' @('exec', '-e', 'SQLCMDPASSWORD', $sqlContainer,
        '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', $sqlUser, '-C', '-b',
        '-l', '10', '-t', '30', '-h', '-1', '-W', '-Q', $query) @{ SQLCMDPASSWORD = $sqlPassword }
    return Complete-PrivateProcess $entry 'Scoped native SQL command' 45
}

try {
    $PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
    $StateDirectory = [IO.Path]::GetFullPath($StateDirectory)
    [void][IO.Directory]::CreateDirectory($StateDirectory)
    if ($IsWindows) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        & icacls $StateDirectory /inheritance:r /grant:r "${identity}:(OI)(CI)F" | Out-Null
        if ($LASTEXITCODE) { throw 'Private standalone state ACL setup failed.' }
    } else {
        & chmod 700 $StateDirectory
        if ($LASTEXITCODE) { throw 'Private standalone state permissions failed.' }
    }
    $apiDirectory = Join-Path $PublishDirectory 'api'
    $apiDll = Join-Path $apiDirectory 'ScrumPilot.API.dll'
    foreach ($path in @($apiDll, (Join-Path $PublishDirectory 'web/wwwroot/index.html'),
        (Join-Path $PublishDirectory 'fluentweb/wwwroot/index.html'))) {
        if (!(Test-Path -LiteralPath $path)) { throw 'A required final published artifact is missing.' }
    }
    $fixture = Get-Content -LiteralPath $FixtureFile -Raw | ConvertFrom-Json
    $connection = [Data.Common.DbConnectionStringBuilder]::new()
    # PowerShell's IDictionary adapter would create a "ConnectionString" key
    # rather than invoke this DbConnectionStringBuilder property setter.
    $connection.set_ConnectionString($fixture.SCRUMPILOT_TEST_SQL_CONNECTION)
    # Aspire's exported TCP port can be a DCP proxy, not the Docker published
    # port. Require the exact owned fixture container instead of guessing.
    $sqlContainer = $FixtureContainerId
    $sqlUser = $connection['User ID']
    $sqlPassword = $connection['Password']
    $catalog = 'scrumpilot_standalone_' + [guid]::NewGuid().ToString('N')
    $exists = Invoke-FixtureSql "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name=N'$catalog';"
    if ($exists.Trim() -ne '0') { throw 'Standalone catalog already exists; refusing ownership.' }
    $ownedCatalog = $true

    $apiPort = Get-FreePort
    $webPort = Get-FreePort
    $fluentPort = Get-FreePort
    if (@($apiPort, $webPort, $fluentPort | Select-Object -Unique).Count -ne 3) { throw 'Port allocation collided.' }
    $api = "https://localhost:$apiPort"
    $web = "http://127.0.0.1:$webPort"
    $fluent = "http://127.0.0.1:$fluentPort"
    $email = 'standalone-operator@example.invalid'
    $password = 'Standalone-' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) + 'a1!'
    $environment = @{
        ASPNETCORE_ENVIRONMENT = 'Development'; DOTNET_ENVIRONMENT = 'Development'
        ASPNETCORE_URLS = $api
        ConnectionStrings__control = $fixture.SCRUMPILOT_TEST_SQL_CONNECTION + ";Initial Catalog=$catalog"
        'ConnectionStrings__tenant-server' = $fixture.SCRUMPILOT_TEST_SQL_CONNECTION
        'ConnectionStrings__tenant-admin' = $fixture.SCRUMPILOT_TEST_SQL_CONNECTION
        ConnectionStrings__ollama = 'Endpoint=http://127.0.0.1:1;Model=gemma4:latest'
        Jwt__Key = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        Jwt__Issuer = 'StandaloneSmoke'; Jwt__Audience = 'StandaloneSmoke'
        BootstrapAdmin__Email = $email; BootstrapAdmin__Password = $password
        Cors__AllowedOrigins__0 = $web; Cors__AllowedOrigins__1 = $fluent
        SqlServer__BackupServerPath = $fixture.SCRUMPILOT_TEST_SQL_BACKUP_SERVER_PATH
        SqlServer__BackupApplicationPath = $fixture.SCRUMPILOT_TEST_SQL_BACKUP_APPLICATION_PATH
    }
    $initialize = Start-PrivateProcess 'dotnet' @($apiDll, '--initialize') $environment $apiDirectory
    [void](Complete-PrivateProcess $initialize 'Explicit standalone initialization')
    $apiProcess = Start-PrivateProcess 'dotnet' @($apiDll) $environment $apiDirectory
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($apiProcess.Process.HasExited) { throw 'Standalone API exited before readiness; private output withheld.' }
        try {
            $health = Invoke-WebRequest "$api/health" -SkipCertificateCheck -TimeoutSec 3
            if ($health.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$ready) { throw 'Standalone API readiness deadline exceeded.' }

    # Serve immutable publish files. Only public appsettings are overlaid in the
    # HTTP response; no source or published file is overwritten. No gateway exists.
    $staticServer = Join-Path $PSScriptRoot 'StandaloneStaticServer.cjs'
    foreach ($site in @(@('web', $webPort), @('fluentweb', $fluentPort))) {
        $siteDirectory = Join-Path $PublishDirectory $site[0]
        $assetManifests = @(Get-ChildItem -LiteralPath $siteDirectory -Filter '*.staticwebassets.endpoints.json')
        if ($assetManifests.Count -ne 1) { throw 'Expected one published static asset endpoint manifest per site.' }
        [void](Start-PrivateProcess 'node' @($staticServer,
            (Join-Path $siteDirectory 'wwwroot'), $site[1], $api, $assetManifests[0].FullName))
    }
    $credentialsFile = Join-Path $StateDirectory 'credentials.json'
    [IO.File]::WriteAllText($credentialsFile, (@{ Email = $email; Password = $password } | ConvertTo-Json -Compress))
    $manifestFile = Join-Path $StateDirectory 'standalone.json'
    [IO.File]::WriteAllText($manifestFile, (@{
        Api = $api; Web = $web; FluentWeb = $fluent; CredentialFile = $credentialsFile
    } | ConvertTo-Json -Compress))
    $browserScript = Join-Path $PSScriptRoot 'Test-StandaloneBrowser.cjs'
    $browser = Start-PrivateProcess 'node' @($browserScript, $manifestFile)
    try {
        $report = Complete-PrivateProcess $browser 'Actual standalone WASM/browser check' 180
    } catch {
        if ($browser.Process.HasExited) {
            # Report only fixed diagnostic enums, never browser errors, requests,
            # form inputs, or the captured API/process output.
            foreach ($line in ($browser.Output.GetAwaiter().GetResult() -split '\r?\n')) {
                try { $diagnostic = $line | ConvertFrom-Json } catch { continue }
                if ($diagnostic.site -in @('web', 'fluentweb') -and
                    $diagnostic.stage -in @('published-login-startup', 'public-fallback-configuration', 'actual-wasm-form-login')) {
                    Write-Output "Standalone $($diagnostic.site) failed at $($diagnostic.stage)."
                }
            }
        }
        throw
    }
    Write-Output $report
    Write-Output 'Standalone API HTTPS health:200; explicit initialization and normal published API startup verified.'
} finally {
    foreach ($entry in $processes) {
        if (!$entry.Process.HasExited) { Stop-Process -Id $entry.Process.Id -ErrorAction SilentlyContinue }
    }
    if ($ownedCatalog) {
        [void](Invoke-FixtureSql "IF DB_ID(N'$catalog') IS NOT NULL BEGIN ALTER DATABASE [$catalog] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$catalog]; END;")
        Write-Output 'Only the owned standalone control catalog was dropped; SQL helper and graph data were preserved.'
    }
    if ($credentialsFile -and (Test-Path -LiteralPath $credentialsFile)) { Remove-Item -LiteralPath $credentialsFile }
    Write-Output 'Owned standalone API/static/browser helpers stopped.'
}
