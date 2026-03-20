using ScrumPilot.API.Services;
using ScrumPilot.Data.Extensions;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;


var builder = WebApplication.CreateBuilder(args);

// Add Data services (EF Core, repositories)
builder.Services.AddDataServices(builder.Configuration);

// Add services to the container.
builder.Services.AddScoped<IStoryService, StoryService>();
builder.Services.AddHttpClient<StoryService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// Add CORS policy for Blazor WebAssembly
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor", policy =>
        policy.WithOrigins(
                "http://localhost:5199",
                "http://127.0.0.1:5199",
                "https://localhost:7280",
                "https://127.0.0.1:7280"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
    );
});

var app = builder.Build();

// Apply pending migrations at startup (development only)
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ScrumPilotContext>();
        context.Database.Migrate();

        // Seed database with initial data
        DatabaseSeeder.SeedDatabase(context);
    }
}

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
app.UseAuthorization();
app.MapControllers();
app.Run();
