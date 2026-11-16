using ScrumPilot.API.Hubs;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Extensions;
using ScrumPilot.Data.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ScrumPilot.Data.Repositories;
using System.Text;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Configuration;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.API.Services.AI;
using Microsoft.Extensions.Http.Resilience;
using ScrumPilot.Shared.Models;


// Operator commands have positional arguments, not ASP.NET configuration switches.
// Their configuration (including credentials) comes from environment/configuration files.
var builder = WebApplication.CreateBuilder(TenancyCommands.IsOperatorCommand(args) ? [] : args);

builder.AddServiceDefaults();

builder.Services.AddHealthChecks()
                .AddCheck<TenancyReadinessCheck>("tenancy", tags: ["ready"]);

// Add Data services (EF Core, Identity, repositories)
builder.Services.AddDataServices(builder.Configuration);

builder.EnrichSqlServerDbContext<ControlPlaneContext>(settings =>
{ 
    settings.DisableHealthChecks = true; 
    settings.DisableRetry = true; 
});

builder.EnrichSqlServerDbContext<OrganizationContext>(settings =>
{ 
    settings.DisableHealthChecks = true; 
    settings.DisableRetry = true; 
});

var aiOptions = AiOptions.FromConfiguration(builder.Configuration);

// The integration consumes Endpoint=...;Model=...; direct-startup URI values are normalized here.
builder.Configuration["ConnectionStrings:ollama"] = aiOptions.ConnectionString;

builder.Services.AddSingleton(aiOptions);

builder.AddOllamaApiClient("ollama", settings =>
{
    settings.Endpoint = aiOptions.Endpoint;
    settings.SelectedModel = aiOptions.Model;
    settings.DisableHealthChecks = true;
}).AddChatClient();

builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(5));

#pragma warning disable EXTEXP0001
    http.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

});

// Add JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
        // SignalR passes the token via query string for WebSocket connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// Require authentication on every endpoint by default
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(
        new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build()
    );

// Add services to the container.
builder.Services.AddSignalR();

builder.Services.AddScoped<ScrumPilot.API.Controllers.PlanningErrorFilter>();
builder.Services.AddSingleton<ProjectWorkSubscriptions>();
builder.Services.AddScoped<ProjectWorkNotifier>();
builder.Services.AddScoped<ProjectGuidelinesService>();
builder.Services.AddScoped<ProjectStoryGenerationService>();
builder.Services.AddScoped<ProjectWorkConnectionEvictor>();
builder.Services.AddScoped<ProjectDependencyService>();
builder.Services.AddScoped<ScrumPilot.API.Controllers.GitHubFeatureFilter>();
builder.Services.AddScoped<RepositoryContextService>();
builder.Services.AddScoped<TaskSpecificationService>();
builder.Services.AddScoped<TaskGenerationService>();
builder.Services.AddScoped<TaskCreationService>();
builder.Services.AddScoped<PullRequestScopeService>();
builder.Services.AddScoped<IPlanningMutationHandler>(services => services.GetRequiredService<ProjectDependencyService>());

builder.Services.Configure<GitHubAppOptions>(builder.Configuration.GetSection("GitHubApp"));
builder.Services.Configure<FeatureOptions>(builder.Configuration.GetSection("Features"));
builder.Services.AddHttpClient<GitHubAppClient>();

builder.Services.AddScoped<LocalPlanningAi>();
builder.Services.AddScoped<PlanningInferenceSession>();
builder.Services.AddScoped<GitHubRepositoryRegistry>();
builder.Services.AddScoped<GitHubCheckRecovery>();
builder.Services.AddScoped<PlanningIntegrationLifecycle>();
builder.Services.AddScoped<ITenantLifecycleObserver>(services => services.GetRequiredService<PlanningIntegrationLifecycle>());
builder.Services.AddScoped<IProjectPlanningRetirement>(services => services.GetRequiredService<PlanningIntegrationLifecycle>());
builder.Services.AddSingleton<PlanningPokerSessionService>();
builder.Services.AddSingleton<IPlanningPokerConnectionEvictor, PlanningPokerConnectionEvictor>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISprintService, SprintService>();
builder.Services.AddScoped<IEpicService, EpicService>();
builder.Services.AddScoped<IMetricsDashboardService, MetricsDashboardService>();
builder.Services.AddScoped<IDashboardPreferenceService, DashboardPreferenceService>();

builder.Services.AddScoped<IUserSettingsService>(p => 
    new UserSettingsService(p.GetRequiredService<UserManager<ApplicationUser>>(), p.GetRequiredService<ControlPlaneContext>())
    { 
        Storage = p.GetRequiredService<TenantStorage>(), 
        Services = p 
    }
);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IOrganizationAccessService, TenantOrganizationAccessService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IInvitationAcceptanceUserLookup>(p => new InvitationAcceptanceUserLookup(p.GetRequiredService<ControlPlaneContext>()));

builder.Services.Configure<SendGridOptions>(
    builder.Configuration.GetSection(SendGridOptions.SectionName)
);

builder.Services.AddSingleton<IValidateOptions<SendGridOptions>, SendGridOptionsValidator>();

builder.Services.AddHttpClient<ISendGridTransport, SendGridTransport>();
builder.Services.AddScoped<IInvitationEmailSender, SendGridInvitationEmailSender>();
builder.Services.AddScoped<IOrganizationInvitationService, OrganizationInvitationService>();
builder.Services.AddScoped<IStoryGenerationService, StoryGenerationService>();
builder.Services.AddScoped<IPbiService, PbiService>();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<OrganizationBodyFilter>();
    options.Filters.AddService<ScrumPilot.API.Controllers.PlanningErrorFilter>();
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// Add CORS policy for Blazor WebAssembly
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor", policy =>
        policy
            .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()
    );
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (await TenancyCommands.ExecuteAsync(args, app.Services, builder.Configuration)) return;

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Use CORS policy
app.UseCors("AllowBlazor");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<GitHubIntegrationMiddleware>();
app.UseMiddleware<OrganizationResolverMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.MapHub<PlanningPokerHub>("/hubs/organizations/{organizationId:int}/planning-poker")
    .WithMetadata(new OrganizationWorkAttribute());

app.MapHub<ProjectWorkHub>("/hubs/organizations/{organizationId:int}/project-work")
    .WithMetadata(new OrganizationWorkAttribute());
    
app.Run();

public partial class Program { }
