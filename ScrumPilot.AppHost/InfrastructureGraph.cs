using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ScrumPilot.AppHost;

public static class InfrastructureGraph
{
    public const string BackupServerPath = "/var/opt/mssql/backups";
    public const string CatalogPrefix = "scrumpilot_org_";
    public const string ProductionSqlImage = "2025-CU9-GDR1-ubuntu-24.04";

    public static void Configure(IDistributedApplicationBuilder builder)
    {
        var sqlOnly = builder.Configuration.GetValue<bool>("Infrastructure:SqlOnly");
        var local = sqlOnly || string.Equals(builder.Configuration["SqlServer:Mode"] ?? "Local",
            "Local", StringComparison.OrdinalIgnoreCase);
        IResourceBuilder<SqlServerServerResource>? sql = null;
        IResourceBuilder<IResourceWithConnectionString> control;
        IResourceBuilder<IResourceWithConnectionString> runtime;
        IResourceBuilder<IResourceWithConnectionString> admin;
        var backupDirectory = builder.Configuration["SqlServer:BackupApplicationPath"];
        var backupServerPath = builder.Configuration["SqlServer:BackupServerPath"] ?? BackupServerPath;
        var ephemeral = builder.Configuration.GetValue<bool>("Infrastructure:EphemeralTests");
        if (ephemeral && (builder.ExecutionContext.IsPublishMode ||
            !(builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))))
            throw new InvalidOperationException("Ephemeral storage is permitted only for local development/testing.");
        if (!sqlOnly && !ephemeral && !builder.ExecutionContext.IsPublishMode &&
            string.IsNullOrWhiteSpace(backupDirectory))
            throw new InvalidOperationException("Set SqlServer:BackupApplicationPath to restricted native backup storage visible to the API host.");
        var mountSource = builder.Configuration["SqlServer:BackupMountSource"];
        var backupSource = string.IsNullOrWhiteSpace(mountSource) ? backupDirectory : mountSource;
        if (local && !builder.ExecutionContext.IsPublishMode && backupSource is not null &&
            System.Text.RegularExpressions.Regex.IsMatch(backupSource,
                @"\A(?:[a-zA-Z]:[\\/]|/run/desktop/mnt/host/[a-zA-Z]/|/host_mnt/[a-zA-Z]/)"))
            throw new InvalidOperationException(
                "SQL Server native backups cannot use a Windows drive bind mount (SQL error 3634, OS error 31 during DiskChangeFileSize). " +
                "Run scripts\\Initialize-WslBackupStorage.ps1 and configure SqlServer:BackupApplicationPath and SqlServer:BackupMountSource for the same native Linux directory.");

        if (local)
        {
            var imageTag = builder.Configuration["SqlServer:ImageTag"] ??
                (builder.ExecutionContext.IsPublishMode ? ProductionSqlImage : "2025-latest");
            if (!imageTag.StartsWith("2025-", StringComparison.Ordinal))
                throw new InvalidOperationException("Only SQL Server 2025 (major 17) images are supported.");
            sql = builder.AddSqlServer("sql")
                .WithImageTag(imageTag)
                .WithLifetime(ephemeral ? ContainerLifetime.Session : ContainerLifetime.Persistent);
            if (!ephemeral)
                sql.WithDataVolume();
            if (!builder.ExecutionContext.IsPublishMode && !string.IsNullOrWhiteSpace(backupDirectory))
            {
                if (string.IsNullOrWhiteSpace(mountSource))
                    sql.WithBindMount(backupDirectory, backupServerPath);
                else
                    sql.WithAnnotation(new ContainerMountAnnotation(mountSource, backupServerPath,
                        ContainerMountType.BindMount, isReadOnly: false));
            }
            if (builder.ExecutionContext.IsPublishMode)
                sql.PublishAsDockerComposeService((_, service) =>
                {
                    service.Volumes ??= [];
                    service.Volumes.Add(new Aspire.Hosting.Docker.Resources.ServiceNodes.Volume
                    {
                        Name = "scrumpilot-backups",
                        Type = "bind",
                        Source = "${SCRUMPILOT_BACKUP_HOST_PATH:?Set durable restricted native backup directory}",
                        Target = backupServerPath
                    });
                });
            // Catalog creation belongs exclusively to API --initialize. AddDatabase
            // would create an empty catalog first and defeat the lost-registry guard.
            var catalog = builder.Configuration["SqlServer:ControlCatalog"] ?? "scrumpilot_control";
            if (!System.Text.RegularExpressions.Regex.IsMatch(catalog, @"\A[a-zA-Z0-9_]{1,128}\z"))
                throw new InvalidOperationException("The control catalog must contain only letters, digits and underscores.");
            control = builder.Environment.IsDevelopment() && !builder.ExecutionContext.IsPublishMode
                ? builder.AddResource(new ConnectionResource("control",
                    ReferenceExpression.Create($"{sql};Initial Catalog={catalog}")))
                : builder.AddConnectionString("control");
            runtime = sql;
            admin = sql;
        }
        else
        {
            control = builder.AddConnectionString("control");
            runtime = builder.AddConnectionString("tenant-server");
            admin = builder.AddConnectionString("tenant-admin");
        }

        if (sqlOnly)
        {
            ExportTestConnection(builder, sql!, backupDirectory, backupServerPath);
            return;
        }

        var compose = builder.AddDockerComposeEnvironment("compose");
        IResourceBuilder<ParameterResource>? dashboardKey = null;
        if (builder.ExecutionContext.IsPublishMode)
        {
            dashboardKey = Parameter(builder, "dashboard-otlp-key", "Dashboard:OtlpApiKey", secret: true);
            compose.WithDashboard(dashboard => dashboard
                .WithEnvironment("Dashboard__Otlp__AuthMode", "ApiKey")
                .WithEnvironment("Dashboard__Otlp__PrimaryApiKey", dashboardKey));
        }
        // Local SA is a developer convenience, never a production runtime identity.
        if (local && (!builder.Environment.IsDevelopment() || builder.ExecutionContext.IsPublishMode))
        {
            runtime = builder.AddConnectionString("tenant-server");
            admin = builder.AddConnectionString("tenant-admin");
        }

        var endpoint = Parameter(builder, "ollama-endpoint", "AI:Endpoint", "http://localhost:11434");
        var model = Parameter(builder, "ollama-model-name", "AI:Model", "gemma4:latest");
        builder.Services.AddHealthChecks().AddCheck("ollama-model-available",
            new OllamaModelHealthCheck(ct => endpoint.Resource.GetValueAsync(ct),
                ct => model.Resource.GetValueAsync(ct)));
        var ollama = builder.AddExternalService("ollama-model", endpoint)
            .WithHealthCheck("ollama-model-available");
        var ollamaConnection = builder.AddResource(new ConnectionResource("ollama",
            ReferenceExpression.Create($"Endpoint={endpoint};Model={model}")));

        var jwtKey = Parameter(builder, "jwt-key", "Jwt:Key", secret: true);
        var jwtIssuer = Parameter(builder, "jwt-issuer", "Jwt:Issuer", "ScrumPilot");
        var jwtAudience = Parameter(builder, "jwt-audience", "Jwt:Audience", "ScrumPilot");
        var jwtHours = Parameter(builder, "jwt-hours", "Jwt:ExpiresInHours", "8");
        var sendGridKey = Parameter(builder, "sendgrid-key", "SendGrid:ApiKey", "", secret: true);
        var sendGridEmail = Parameter(builder, "sendgrid-from", "SendGrid:FromEmail", "");
        var sendGridName = Parameter(builder, "sendgrid-name", "SendGrid:FromName", "ScrumPilot");
        var initialize = builder.Configuration.GetValue<bool>("Infrastructure:Initialize");
        var gitHubIntegration = builder.Configuration.GetValue<bool>("Features:GitHubIntegration");
        var gitHubIntegrationValue = gitHubIntegration.ToString().ToLowerInvariant();
        // Command-only execution must not reserve the API launch profile's listening ports.
        var migration = builder.AddProject<Projects.ScrumPilot_API>("database-init", launchProfileName: null)
            .WithArgs(initialize ? "--initialize" : "--migrate")
            .WithEnvironment("Features__GitHubIntegration", gitHubIntegrationValue)
            .WithReference(control, "control")
            .WithReference(runtime, "tenant-server")
            .WithReference(admin, "tenant-admin");
        if (!builder.ExecutionContext.IsPublishMode)
            migration.WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);
        if (sql is not null)
            migration.WaitFor(sql);
        if (initialize)
        {
            migration.WithEnvironment("BootstrapAdmin__Email",
                    Parameter(builder, "bootstrap-email", "BootstrapAdmin:Email", secret: true))
                .WithEnvironment("BootstrapAdmin__Password",
                    Parameter(builder, "bootstrap-password", "BootstrapAdmin:Password", secret: true));
        }

        var api = builder.AddProject<Projects.ScrumPilot_API>("webapi")
            .WithEnvironment("Features__GitHubIntegration", gitHubIntegrationValue)
            .WithReference(control, "control")
            .WithReference(runtime, "tenant-server")
            .WithReference(admin, "tenant-admin")
            .WithReference(ollamaConnection)
            .WithEnvironment("Jwt__Key", jwtKey)
            .WithEnvironment("Jwt__Issuer", jwtIssuer)
            .WithEnvironment("Jwt__Audience", jwtAudience)
            .WithEnvironment("Jwt__ExpiresInHours", jwtHours)
            .WithEnvironment("SendGrid__ApiKey", sendGridKey)
            .WithEnvironment("SendGrid__FromEmail", sendGridEmail)
            .WithEnvironment("SendGrid__FromName", sendGridName)
            .WithEnvironment("AI__Model", model)
            .WithEnvironment("SqlServer__BackupServerPath",
                backupServerPath)
            .WithEnvironment("SqlServer__BackupApplicationPath", backupDirectory ?? "")
            // Locally the API 307-redirects HTTP to its HTTPS origin (see apiScheme
            // below), so probe the HTTPS endpoint directly; published containers
            // drop HTTPS endpoints and keep the HTTP probe.
            .WithHttpHealthCheck("/health",
                endpointName: builder.ExecutionContext.IsPublishMode ? "http" : "https")
            .WaitForCompletion(migration);
        migration.WithEnvironment("SqlServer__BackupServerPath", backupServerPath)
            .WithEnvironment("SqlServer__BackupApplicationPath", backupDirectory ?? "");
        if (builder.ExecutionContext.IsPublishMode)
        {
            foreach (var process in new[] { api, migration })
            {
                process.WithEnvironment("SqlServer__BackupApplicationPath", "/app/backups")
                    .PublishAsDockerComposeService((_, service) =>
                    {
                        service.Volumes ??= [];
                        service.Volumes.Add(new Aspire.Hosting.Docker.Resources.ServiceNodes.Volume
                        {
                            Name = "scrumpilot-backups",
                            Type = "bind",
                            Source = "${SCRUMPILOT_BACKUP_HOST_PATH:?Set durable restricted native backup directory}",
                            Target = "/app/backups"
                        });
                    });
            }
        }
        // Model AI as a visible optional dependency, NOT a readiness wait.
        api.WithRelationship(ollama, "Uses");

#pragma warning disable ASPIREBLAZOR001
        // A local HTTP upstream redirects outside the gateway to the API's HTTPS
        // origin, causing browsers to discard Authorization on the redirected hop.
        var apiScheme = builder.ExecutionContext.IsPublishMode ? "http" : "https";
        var web = builder.AddBlazorWasmProject<Projects.ScrumPilot_Web>("web").WithReference(api.GetEndpoint(apiScheme));
        var fluent = builder.AddBlazorWasmProject<Projects.ScrumPilot_FluentWeb>("fluentweb").WithReference(api.GetEndpoint(apiScheme));
        web.WithEnvironment("Features__GitHubIntegration", gitHubIntegrationValue);
        fluent.WithEnvironment("Features__GitHubIntegration", gitHubIntegrationValue);
        var webGateway = builder.AddBlazorGateway("web-gateway")
            .WithBlazorClientApp(web, proxyTelemetry: true).WithExternalHttpEndpoints()
            .WithEnvironment("Logging__LogLevel__Microsoft.AspNetCore", "Warning")
            .WithEnvironment("Logging__LogLevel__Yarp.ReverseProxy", "Warning")
            .WithHttpHealthCheck("/web/_blazor/_configuration");
        var fluentGateway = builder.AddBlazorGateway("fluentweb-gateway")
            .WithBlazorClientApp(fluent, proxyTelemetry: true).WithExternalHttpEndpoints()
            .WithEnvironment("Logging__LogLevel__Microsoft.AspNetCore", "Warning")
            .WithEnvironment("Logging__LogLevel__Yarp.ReverseProxy", "Warning")
            .WithHttpHealthCheck("/fluentweb/_blazor/_configuration");
#pragma warning restore ASPIREBLAZOR001
        if (!builder.ExecutionContext.IsPublishMode)
        {
            foreach (var (scheme, key) in new[]
            {
                ("http", "Gateways:FluentWeb:HttpPort"),
                ("https", "Gateways:FluentWeb:HttpsPort")
            })
            {
                var port = builder.Configuration.GetValue<int>(key);
                if (port is < 1 or > 65535)
                    throw new InvalidOperationException($"Set {key} to a port between 1 and 65535.");
                fluentGateway.WithEndpoint(scheme, endpoint => endpoint.Port = port);
            }
            if (fluentGateway.GetEndpoint("http").EndpointAnnotation.Port ==
                fluentGateway.GetEndpoint("https").EndpointAnnotation.Port)
                throw new InvalidOperationException("Gateways:FluentWeb:HttpPort and Gateways:FluentWeb:HttpsPort must be different.");

            if (OperatingSystem.IsWindows())
            {
                // Preview gateway builds wait for output EOF; reused MSBuild nodes retain those handles.
                System.Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1",
                    EnvironmentVariableTarget.Process);
                System.Environment.SetEnvironmentVariable("DOTNET_CLI_USE_MSBUILD_SERVER", "false",
                    EnvironmentVariableTarget.Process);
            }
            // Gateway environment callbacks build clients sharing the same obj outputs.
            fluentGateway.WaitFor(webGateway);
            // Both supported gateways execute the same file-based helper. The
            // SDK's shared runfile cache can rewrite a DLL already loaded by the
            // other gateway on Windows. Give each helper its own temp/build cache.
            foreach (var gateway in new[] { webGateway, fluentGateway })
            {
                var temporary = Directory.CreateDirectory(Path.Combine(
                    builder.Configuration["Aspire:Store:Path"] ?? builder.AppHostDirectory,
                    ".aspire", "gateway-temp", gateway.Resource.Name)).FullName;
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                gateway.WithEnvironment("TMP", temporary)
                    .WithEnvironment("TEMP", temporary)
                    .WithEnvironment("TMPDIR", temporary);
            }
            ConfigureDevTunnel(builder, api, webGateway, fluentGateway);
        }
        if (builder.ExecutionContext.IsPublishMode)
        {
            // TLS terminates at the operator's ingress, not at an unconfigured container.
            foreach (var resource in new[] { api.Resource, webGateway.Resource, fluentGateway.Resource })
            {
                foreach (var annotation in resource.Annotations.OfType<EndpointAnnotation>()
                    .Where(e => e.UriScheme == "https").ToArray())
                    resource.Annotations.Remove(annotation);
            }
            var webOrigin = Parameter(builder, "web-public-origin", "Public:WebOrigin");
            var fluentOrigin = Parameter(builder, "fluentweb-public-origin", "Public:FluentWebOrigin");
            api.WithEnvironment("Cors__AllowedOrigins__0", webOrigin)
                .WithEnvironment("Cors__AllowedOrigins__1", fluentOrigin)
                .WithEnvironment("SendGrid__InvitationBaseUrl", Parameter(builder, "invitation-url", "SendGrid:InvitationBaseUrl"));
            ConfigurePublishedTelemetry(webGateway, "web", dashboardKey!, webOrigin, gitHubIntegration);
            ConfigurePublishedTelemetry(fluentGateway, "fluentweb", dashboardKey!, fluentOrigin, gitHubIntegration);
        }
        else
        {
            api.WithEnvironment("Cors__AllowedOrigins__0", webGateway.GetEndpoint("https"))
                .WithEnvironment("Cors__AllowedOrigins__1", fluentGateway.GetEndpoint("https"))
                .WithEnvironment("Cors__AllowedOrigins__2", webGateway.GetEndpoint("http"))
                .WithEnvironment("Cors__AllowedOrigins__3", fluentGateway.GetEndpoint("http"))
                .WithEnvironment("SendGrid__InvitationBaseUrl",
                    ReferenceExpression.Create($"{webGateway.GetEndpoint("https")}/web/accept-invitation"));
        }
    }

    // Local-only public URLs. Browsers reach the API through each gateway's
    // same-origin /{client}/_api/webapi proxy, so CORS needs no tunnel origins.
    private static void ConfigureDevTunnel(IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> api, IResourceBuilder<ProjectResource> webGateway,
        IResourceBuilder<ProjectResource> fluentGateway)
    {
        var settings = builder.Configuration.GetSection("DevTunnels");
        if (!settings.GetValue<bool>("Enabled"))
            return;
        var targets = new[] { ("WebApi", api), ("Web", webGateway), ("FluentWeb", fluentGateway) }
            .Where(target => settings.GetValue(target.Item1, true))
            .Select(target => target.Item2)
            .ToArray();
        if (targets.Length == 0)
            return;
        var tunnelId = settings["TunnelId"];
        var tunnel = builder.AddDevTunnel("devtunnel",
            string.IsNullOrWhiteSpace(tunnelId) ? null : tunnelId,
            new Aspire.Hosting.DevTunnels.DevTunnelOptions
            {
                AllowAnonymous = settings.GetValue<bool>("AllowAnonymous"),
                Description = "ScrumPilot local development"
            });
        // HTTPS targets avoid the API's HTTP-to-localhost redirect.
        foreach (var target in targets)
            tunnel.WithReference(target.GetEndpoint("https"));
    }

    // The preview Blazor publisher omits its HTTP-OTLP argument in publish mode.
    // Complete its generated YARP/configuration model without exposing dashboard headers.
    private static void ConfigurePublishedTelemetry(IResourceBuilder<ProjectResource> gateway, string client,
        IResourceBuilder<ParameterResource> dashboardKey, IResourceBuilder<ParameterResource> publicOrigin,
        bool gitHubIntegration)
    {
        gateway.WithEnvironment(context =>
        {
            var key = $"ClientApps__{client}__ConfigResponse";
            context.EnvironmentVariables[key] = PublicBrowserConfiguration(client, publicOrigin, gitHubIntegration);
            var route = $"ReverseProxy__Routes__route-otlp-{client}";
            context.EnvironmentVariables[$"{route}__ClusterId"] = "cluster-otlp-dashboard";
            context.EnvironmentVariables[$"{route}__Match__Path"] = $"/{client}/_otlp/{{**catch-all}}";
            context.EnvironmentVariables[$"{route}__Transforms__0__PathRemovePrefix"] = $"/{client}/_otlp";
            context.EnvironmentVariables["ReverseProxy__Clusters__cluster-otlp-dashboard__Destinations__d1__Address"]
                = "http://compose-dashboard:18890";
        }).WithEnvironment($"ReverseProxy__Routes__route-otlp-{client}__Transforms__1__RequestHeader", "x-otlp-api-key")
            .WithEnvironment($"ReverseProxy__Routes__route-otlp-{client}__Transforms__1__Set", dashboardKey);
    }

    internal static ReferenceExpression PublicBrowserConfiguration(string client,
        IResourceBuilder<ParameterResource> publicOrigin, bool gitHubIntegration = false)
    {
        var config = System.Text.Json.JsonSerializer.Serialize(new
        {
            webAssembly = new
            {
                environment = new Dictionary<string, string>
                {
                    ["Features__GitHubIntegration"] = gitHubIntegration.ToString().ToLowerInvariant(),
                    ["services__webapi__http__0"] = $"__PUBLIC_ORIGIN__/{client}/_api/webapi",
                    ["OTEL_SERVICE_NAME"] = client,
                    ["OTEL_EXPORTER_OTLP_ENDPOINT"] = $"/{client}/_otlp",
                    ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf"
                }
            }
        }).Split("__PUBLIC_ORIGIN__", StringSplitOptions.None);
        var expression = new ReferenceExpressionBuilder();
        // ReferenceExpression uses composite formatting: escape JSON braces.
        expression.AppendLiteral(config[0].Replace("{", "{{").Replace("}", "}}"));
        expression.AppendFormatted(publicOrigin.Resource);
        expression.AppendLiteral(config[1].Replace("{", "{{").Replace("}", "}}"));
        return expression.Build();
    }

    // Explicit Parameters values win; direct-launch application keys remain valid fallbacks.
    public static IResourceBuilder<ParameterResource> Parameter(IDistributedApplicationBuilder builder,
        string name, string applicationKey, string? fallback = null, bool secret = false)
    {
        var value = builder.Configuration[$"Parameters:{name}"] ??
            builder.Configuration[$"Parameters:{name.Replace('-', '_')}"] ??
            builder.Configuration[applicationKey] ?? fallback;
        return value is null ? builder.AddParameter(name, secret: secret)
            : builder.AddParameter(name, value, publishValueAsDefault: !secret, secret: secret);
    }

    private static void ExportTestConnection(IDistributedApplicationBuilder builder,
        IResourceBuilder<SqlServerServerResource> sql, string? backupDirectory, string backupServerPath)
    {
        var file = builder.Configuration["Infrastructure:TestConnectionFile"];
        if (string.IsNullOrWhiteSpace(file))
            return;
        builder.Eventing.Subscribe<AfterResourcesCreatedEvent>(async (_, cancellationToken) =>
        {
            var connection = await sql.Resource.GetConnectionStringAsync(cancellationToken);
            await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(new
            {
                SCRUMPILOT_TEST_SQL_CONNECTION = connection,
                SCRUMPILOT_TEST_SQL_BACKUP_SERVER_PATH = backupServerPath,
                SCRUMPILOT_TEST_SQL_BACKUP_APPLICATION_PATH = backupDirectory,
                SCRUMPILOT_TEST_SQL_BACKUP_MOUNT_SOURCE =
                    builder.Configuration["SqlServer:BackupMountSource"] ?? backupDirectory
            }), cancellationToken);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        });
    }
}

public sealed class ConnectionResource(string name, ReferenceExpression expression)
    : Resource(name), IResourceWithConnectionString
{
    public ReferenceExpression ConnectionStringExpression => expression;
}
