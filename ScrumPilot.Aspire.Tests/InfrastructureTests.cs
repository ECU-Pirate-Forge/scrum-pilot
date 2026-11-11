using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using ScrumPilot.AppHost;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;

#pragma warning disable ASPIREBLAZOR001
namespace ScrumPilot.Aspire.Tests;

public sealed class InfrastructureTests(ITestOutputHelper output)
{
    private static Task<IDistributedApplicationTestingBuilder> Graph(params string[] extra) =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.ScrumPilot_AppHost>(
        new DistributedApplicationTestingBuilderOptions { EnableDashboard = true },
        [
            "--environment=Development",
            // The preview Blazor publisher and runner otherwise share generated
            // manifests beneath AppHostDirectory, even with isolated build outputs.
            "--Aspire:Store:Path=" + Directory.CreateTempSubdirectory("scrumpilot-aspire-graph-state-").FullName,
            "--Infrastructure:EphemeralTests=true",
            "--Parameters:jwt-key=integration-only-not-a-production-signing-key",
            "--Parameters:sendgrid-key=integration-private-sendgrid",
            .. extra
        ]);

    [Fact]
    public async Task ModelsTwoGatewaysAndOneSqlInstanceNotPerOrganization()
    {
        await using var builder = await Graph();
        Assert.Single(builder.Resources.OfType<SqlServerServerResource>());
        Assert.Empty(builder.Resources.OfType<SqlServerDatabaseResource>());
        Assert.Equal(2, builder.Resources.OfType<BlazorWasmAppResource>().Count());
        Assert.Contains(builder.Resources, r => r.Name == "web-gateway");
        Assert.Contains(builder.Resources, r => r.Name == "fluentweb-gateway");
        var image = Assert.Single(builder.Resources.OfType<SqlServerServerResource>())
            .Annotations.OfType<ContainerImageAnnotation>().Single();
        Assert.StartsWith("2025-", image.Tag);
        Assert.DoesNotContain(builder.Resources, r => r.Name.StartsWith(InfrastructureGraph.CatalogPrefix));
    }

    [Fact]
    public async Task PrivateConfigurationNeverFlowsToWasm()
    {
        await using var builder = await Graph();
        foreach (var browser in builder.Resources.OfType<BlazorWasmAppResource>())
        {
            var values = await Environment(builder, browser);
            Assert.DoesNotContain(values.Keys, key =>
                key.Contains("ConnectionStrings", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Jwt", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("SendGrid", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Bootstrap", StringComparison.OrdinalIgnoreCase));
            Assert.All(browser.Annotations.OfType<EndpointReferenceAnnotation>(),
                reference => Assert.Equal("webapi", reference.Resource.Name));
        }
        var api = builder.Resources.Single(r => r.Name == "webapi");
        var environment = await Environment(builder, api);
        Assert.Contains("ConnectionStrings__control", environment.Keys);
        Assert.Contains("ConnectionStrings__tenant-server", environment.Keys);
        Assert.Contains("ConnectionStrings__tenant-admin", environment.Keys);
        Assert.Contains("ConnectionStrings__ollama", environment.Keys);
        Assert.Contains("Cors__AllowedOrigins__0", environment.Keys);
        Assert.Contains("Cors__AllowedOrigins__1", environment.Keys);
    }

    [Fact]
    public async Task PublishedPublicConfigurationIsValidJsonAfterExpressionExpansion()
    {
        await using var builder = await Graph();
        var origin = InfrastructureGraph.Parameter(builder, "test-public-origin",
            "Public:WebOrigin", "https://example.invalid");
        foreach (var client in new[] { "web", "fluentweb" })
        {
            var expression = InfrastructureGraph.PublicBrowserConfiguration(client, origin);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var json = await expression.GetValueAsync(timeout.Token);
            using var config = JsonDocument.Parse(json!);
            var environment = config.RootElement.GetProperty("webAssembly").GetProperty("environment");
            Assert.Equal(4, environment.EnumerateObject().Count());
            Assert.Equal($"https://example.invalid/{client}/_api/webapi",
                environment.GetProperty("services__webapi__http__0").GetString());
            Assert.Equal("http/protobuf", environment.GetProperty("OTEL_EXPORTER_OTLP_PROTOCOL").GetString());
            Assert.DoesNotContain("signing-key", json);
            Assert.DoesNotContain("integration-private-sendgrid", json);
        }
    }

    [Fact]
    public async Task ExplicitParameterOverridesApplicationFallback()
    {
        await using var builder = await Graph("--AI:Model=fallback-model", "--Parameters:ollama-model-name=chosen-model");
        Assert.Equal("chosen-model",
            await builder.Resources.OfType<ParameterResource>().Single(r => r.Name == "ollama-model-name").GetValueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ShellSafeParameterOverridesApplicationFallback()
    {
        await using var builder = await Graph("--AI:Model=fallback-model", "--Parameters:ollama_model_name=shell-model");
        Assert.Equal("shell-model",
            await builder.Resources.OfType<ParameterResource>().Single(r => r.Name == "ollama-model-name").GetValueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExternalSqlModeCreatesNoDatabaseContainer()
    {
        await using var builder = await Graph("--SqlServer:Mode=External");
        Assert.Empty(builder.Resources.OfType<SqlServerServerResource>());
        Assert.Contains(builder.Resources, r => r.Name == "tenant-server");
        Assert.Contains(builder.Resources, r => r.Name == "tenant-admin");
    }

    [Fact]
    public async Task SqlOnlyModeRequiresNoApplicationSecretsOrGateways()
    {
        await using var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.ScrumPilot_AppHost>(["--Infrastructure:SqlOnly=true"]);
        Assert.Single(builder.Resources.OfType<SqlServerServerResource>());
        Assert.DoesNotContain(builder.Resources, resource => resource is ProjectResource);
    }

    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task ActualSqlEngineIsMajor17AndNewCatalogUsesCompatibility170()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.ScrumPilot_AppHost>(
                ["--Infrastructure:SqlOnly=true", "--Infrastructure:EphemeralTests=true"], timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("sql", timeout.Token);
        var connectionString = await app.GetConnectionStringAsync("sql", timeout.Token);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(timeout.Token);
        await using var version = new SqlCommand("SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion'))", connection);
        Assert.Equal(17, Convert.ToInt32(await version.ExecuteScalarAsync(timeout.Token)));
        var catalog = "scrumpilot_test_" + Guid.NewGuid().ToString("N");
        try
        {
            await using var create = new SqlCommand($"CREATE DATABASE [{catalog}]; ALTER DATABASE [{catalog}] SET COMPATIBILITY_LEVEL = 170;", connection);
            await create.ExecuteNonQueryAsync(timeout.Token);
            await using var compatibility = new SqlCommand(
                "SELECT compatibility_level FROM sys.databases WHERE name=@name", connection);
            compatibility.Parameters.AddWithValue("@name", catalog);
            Assert.Equal(170, Convert.ToInt32(await compatibility.ExecuteScalarAsync(timeout.Token)));
        }
        finally
        {
            await using var drop = new SqlCommand($"DROP DATABASE IF EXISTS [{catalog}]", connection);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    [Trait("Category", "GatewayIntegration")]
    public async Task FreshSqlGraphServesBothPrefixedSitesAndPublicRuntimeConfiguration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var nativeBackupSource = System.Environment.GetEnvironmentVariable("SCRUMPILOT_TEST_SQL_BACKUP_MOUNT_SOURCE");
        var fixtureBackupDirectory = System.Environment.GetEnvironmentVariable("SCRUMPILOT_TEST_SQL_BACKUP_APPLICATION_PATH");
        var suffix = "graph-" + Guid.NewGuid().ToString("N");
        var backupDirectory = !string.IsNullOrWhiteSpace(nativeBackupSource) && !string.IsNullOrWhiteSpace(fixtureBackupDirectory)
            ? Directory.CreateDirectory(Path.Combine(fixtureBackupDirectory, suffix))
            : Directory.CreateTempSubdirectory("scrumpilot-gateway-backups-");
        var bootstrapEmail = "operator@example.invalid";
        var bootstrapPassword = "Test-Only-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)) + "a1!";
        await using var builder = await Graph(
            "--Infrastructure:Initialize=true", "--Infrastructure:EphemeralTests=true",
            "--Parameters:ollama-endpoint=http://127.0.0.1:1",
            "--SqlServer:BackupApplicationPath=" + backupDirectory.FullName,
            "--SqlServer:BackupMountSource=" + (string.IsNullOrWhiteSpace(nativeBackupSource) ? "" :
                nativeBackupSource.TrimEnd('/') + "/" + suffix),
            "--BootstrapAdmin:Email=" + bootstrapEmail,
            "--BootstrapAdmin:Password=" + bootstrapPassword);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        try
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync("webapi", timeout.Token);
            await app.ResourceNotifications.WaitForResourceHealthyAsync("web-gateway", timeout.Token);
            await app.ResourceNotifications.WaitForResourceHealthyAsync("fluentweb-gateway", timeout.Token);
        }
        catch
        {
            var logs = app.Services.GetRequiredService<ResourceLoggerService>();
            using var logTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                foreach (var name in new[] { "database-init", "webapi", "web-gateway", "fluentweb-gateway" })
                {
                    var resource = builder.Resources.Single(r => r.Name == name);
                    await foreach (var batch in logs.GetAllAsync(resource).WithCancellation(logTimeout.Token))
                        foreach (var line in batch.TakeLast(25))
                            output.WriteLine($"{name}: {line.Content}");
                }
            }
            catch (OperationCanceledException) when (logTimeout.IsCancellationRequested) { }
            throw;
        }

        int? organizationId = null;
        foreach (var (gateway, site) in new[]
        {
                ("web-gateway", "web"), ("fluentweb-gateway", "fluentweb")
            })
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(gateway, timeout.Token);
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = app.GetEndpoint(gateway, "http")
            };
            using var configResponse = await client.GetAsync($"/{site}/_blazor/_configuration", timeout.Token);
            Assert.True(configResponse.IsSuccessStatusCode, $"{gateway}: configuration returned {(int)configResponse.StatusCode}.");
            var configuration = await configResponse.Content.ReadAsStringAsync(timeout.Token);
            using var config = JsonDocument.Parse(configuration);
            var environment = config.RootElement.GetProperty("webAssembly").GetProperty("environment");
            Assert.All(environment.EnumerateObject(), property =>
                Assert.True(property.Name.StartsWith("services__webapi__") ||
                    property.Name is "OTEL_SERVICE_NAME" or "OTEL_EXPORTER_OTLP_ENDPOINT" or "OTEL_EXPORTER_OTLP_PROTOCOL"));
            Assert.Equal("http/protobuf", environment.GetProperty("OTEL_EXPORTER_OTLP_PROTOCOL").GetString());
            Assert.DoesNotContain("integration-private-sendgrid", configuration);
            Assert.DoesNotContain("signing-key", configuration);
            using var deepLink = await client.GetAsync($"/{site}/organizations/{Guid.NewGuid():D}/backlog", timeout.Token);
            Assert.True(deepLink.IsSuccessStatusCode, $"{gateway}: deep link returned {(int)deepLink.StatusCode}.");
            var html = await deepLink.Content.ReadAsStringAsync(timeout.Token);
            Assert.Contains($"location.pathname.startsWith(\"/{site}/\")", html);
            Assert.Contains($"document.querySelector(\"base\").setAttribute(\"href\", \"/{site}/\");", html);
            Assert.True(html.IndexOf("location.pathname", StringComparison.Ordinal) <
                html.IndexOf("<link", StringComparison.Ordinal));
            using var apiHealth = await client.GetAsync($"/{site}/_api/webapi/health", timeout.Token);
            Assert.True(apiHealth.IsSuccessStatusCode, $"{gateway}: proxied API health returned {(int)apiHealth.StatusCode}.");
            using var login = await client.PostAsJsonAsync($"/{site}/_api/webapi/api/auth/login",
                new { UserName = bootstrapEmail, Password = bootstrapPassword }, timeout.Token);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Null(login.Headers.Location);
            using var identity = JsonDocument.Parse(await login.Content.ReadAsStringAsync(timeout.Token));
            var token = identity.RootElement.GetProperty("token").GetString()!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            using var organizations = await client.GetAsync($"/{site}/_api/webapi/api/organizations", timeout.Token);
            Assert.Equal(HttpStatusCode.OK, organizations.StatusCode);
            Assert.Null(organizations.Headers.Location);
            if (organizationId is null)
            {
                var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
                using var claims = JsonDocument.Parse(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
                var owner = claims.RootElement.EnumerateObject()
                    .Single(property => property.Name.EndsWith("/nameidentifier", StringComparison.Ordinal)).Value.GetString();
                using var created = await client.PostAsJsonAsync($"/{site}/_api/webapi/api/organizations",
                    new { Name = "Gateway regression", InitialOwnerUserId = owner }, timeout.Token);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                using var organization = JsonDocument.Parse(await created.Content.ReadAsStringAsync(timeout.Token));
                organizationId = organization.RootElement.GetProperty("organizationId").GetInt32();
            }
            var hubPath = $"/{site}/_api/webapi/hubs/organizations/{organizationId}/planning-poker";
            using var negotiate = await client.PostAsync(hubPath + "/negotiate?negotiateVersion=1", null, timeout.Token);
            Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
            Assert.Null(negotiate.Headers.Location);
            using var negotiation = JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync(timeout.Token));
            var connectionToken = negotiation.RootElement.GetProperty("connectionToken").GetString()!;
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            var socketAddress = new UriBuilder(new Uri(client.BaseAddress, hubPath))
            {
                Scheme = "ws",
                Query = "id=" + Uri.EscapeDataString(connectionToken)
            }.Uri;
            await socket.ConnectAsync(socketAddress, timeout.Token);
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
                WebSocketMessageType.Text, true, timeout.Token);
            var handshake = new byte[4096];
            var result = await socket.ReceiveAsync(handshake, timeout.Token);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            Assert.StartsWith("{}\u001e", Encoding.UTF8.GetString(handshake, 0, result.Count));
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token);
            using var telemetry = await client.PostAsync($"/{site}/_otlp/v1/traces",
                new ByteArrayContent([]) { Headers = { ContentType = new("application/x-protobuf") } }, timeout.Token);
            Assert.True(telemetry.IsSuccessStatusCode, $"{gateway}: HTTP/protobuf proxy returned {(int)telemetry.StatusCode}.");
        }
        // Opt-in, operator-owned live inspection. CI never sets this option.
        // Removing the sentinel releases this test and disposes only its graph.
        var browserFile = System.Environment.GetEnvironmentVariable("SCRUMPILOT_ASPIRE_BROWSER_SESSION_FILE");
        if (!string.IsNullOrWhiteSpace(browserFile))
        {
            if (!Path.IsPathFullyQualified(browserFile) || !Directory.Exists(Path.GetDirectoryName(browserFile)))
                throw new InvalidOperationException("Browser manifest must use an existing restricted, absolute directory.");
            var expires = DateTimeOffset.UtcNow.AddMinutes(30);
            var requestedExpiry = System.Environment.GetEnvironmentVariable("SCRUMPILOT_ASPIRE_BROWSER_LEASE_UNTIL");
            if (requestedExpiry is not null &&
                (!DateTimeOffset.TryParse(requestedExpiry, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out expires) ||
                 expires <= DateTimeOffset.UtcNow || expires > DateTimeOffset.UtcNow.AddHours(4)))
                throw new InvalidOperationException("Browser lease expiry must be an explicit timestamp within the next four hours.");
            expires = expires.ToUniversalTime();
            var renewalFile = browserFile + ".hold";
            var credentialFile = browserFile + ".credentials.json";
            await File.WriteAllTextAsync(credentialFile, JsonSerializer.Serialize(new
            {
                Email = bootstrapEmail,
                Password = bootstrapPassword
            }));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(credentialFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Task WriteManifestAsync() => File.WriteAllTextAsync(browserFile, JsonSerializer.Serialize(new
            {
                Api = app.GetEndpoint("webapi", "https").ToString(),
                Web = new Uri(app.GetEndpoint("web-gateway", "https"), "web/").ToString(),
                FluentWeb = new Uri(app.GetEndpoint("fluentweb-gateway", "https"), "fluentweb/").ToString(),
                WebHttp = new Uri(app.GetEndpoint("web-gateway", "http"), "web/").ToString(),
                FluentWebHttp = new Uri(app.GetEndpoint("fluentweb-gateway", "http"), "fluentweb/").ToString(),
                CredentialFile = credentialFile,
                LeaseExpiresAtUtc = expires,
                LeaseRenewalFile = renewalFile
            }));
            await WriteManifestAsync();
            await File.WriteAllTextAsync(renewalFile, expires.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            output.WriteLine("Browser graph ready. Endpoint manifest: " + browserFile);
            try
            {
                while (File.Exists(renewalFile))
                {
                    string renewal;
                    try { renewal = await File.ReadAllTextAsync(renewalFile); }
                    catch (FileNotFoundException) { break; }
                    catch (IOException) when (DateTimeOffset.UtcNow < expires)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        continue;
                    }
                    if (DateTimeOffset.TryParse(renewal, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var renewed) &&
                        renewed > expires && renewed <= DateTimeOffset.UtcNow.AddHours(4))
                    {
                        expires = renewed.ToUniversalTime();
                        await WriteManifestAsync();
                    }
                    if (DateTimeOffset.UtcNow >= expires)
                        break;
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
            finally
            {
                File.Delete(credentialFile);
                File.Delete(renewalFile);
            }
        }
    }
    private static async Task<Dictionary<string, object>> Environment(
        IDistributedApplicationTestingBuilder builder, IResource resource)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await annotation.Callback(context);
        return context.EnvironmentVariables;
    }

    [Fact]
    [Trait("Category", "BackupIntegration")]
    public async Task SuppliedSqlFixtureSupportsRealBackupAndApiHostReadAccess()
    {
        var connectionString = System.Environment.GetEnvironmentVariable("SCRUMPILOT_TEST_SQL_CONNECTION");
        var serverPath = System.Environment.GetEnvironmentVariable("SCRUMPILOT_TEST_SQL_BACKUP_SERVER_PATH");
        var applicationPath = System.Environment.GetEnvironmentVariable("SCRUMPILOT_TEST_SQL_BACKUP_APPLICATION_PATH");
        Assert.False(string.IsNullOrWhiteSpace(connectionString), "Run AppHost/scripts/Start-TestSql.ps1 and load its private fixture environment first.");
        Assert.False(string.IsNullOrWhiteSpace(serverPath), "A SQL-visible backup path is required.");
        Assert.False(string.IsNullOrWhiteSpace(applicationPath), "An API-host-visible backup path is required.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(timeout.Token);
        await using var version = new SqlCommand("SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion'))", connection);
        Assert.Equal(17, Convert.ToInt32(await version.ExecuteScalarAsync(timeout.Token)));
        var catalog = "scrumpilot_test_" + Guid.NewGuid().ToString("N");
        var fileName = catalog + ".bak";
        var hostFile = Path.Combine(applicationPath, fileName);
        var created = false;
        try
        {
            await using var create = new SqlCommand($"CREATE DATABASE [{catalog}]; ALTER DATABASE [{catalog}] SET COMPATIBILITY_LEVEL = 170;", connection);
            await create.ExecuteNonQueryAsync(timeout.Token);
            created = true;
            await using var backup = new SqlCommand($"BACKUP DATABASE [{catalog}] TO DISK=@path WITH COPY_ONLY,CHECKSUM,INIT;", connection);
            backup.Parameters.AddWithValue("@path", serverPath.TrimEnd('/') + "/" + fileName);
            await backup.ExecuteNonQueryAsync(timeout.Token);
            Assert.True(File.Exists(hostFile), "The API host must see the actual SQL backup through the explicit path mapping.");
            await using var file = File.OpenRead(hostFile);
            Assert.True(file.Length > 0);
            Assert.Equal(32, (await System.Security.Cryptography.SHA256.HashDataAsync(file, timeout.Token)).Length);
        }
        finally
        {
            if (created)
            {
                await using var drop = new SqlCommand($"DROP DATABASE IF EXISTS [{catalog}]", connection);
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }
            if (File.Exists(hostFile))
                File.Delete(hostFile);
        }
    }
}
