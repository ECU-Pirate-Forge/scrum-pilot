using ScrumPilot.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Data.Repositories;


var builder = WebApplication.CreateBuilder(args);

// Add Data services (EF Core, repositories)
builder.Services.AddScoped<IStoryRepository, StoryRepository>();
// Add services to the container.
builder.Services.AddScoped<IStoryService, StoryService>();
builder.Services.AddHttpClient();
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// Add CORS policy for Blazor WebAssembly
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor",
        policy => policy
            .WithOrigins("https://localhost:7280") // Blazor app's HTTPS port
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor", policy =>
        policy.WithOrigins(
                "http://localhost:5199",
                "https://localhost:7280"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
    );
});

var app = builder.Build();

// Apply pending migrations at startup (development only)
if (app.Environment.IsDevelopment())
{
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
