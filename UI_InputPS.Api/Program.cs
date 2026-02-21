using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

var store = new JsonStore("data/requests.json");

app.MapPost("/api/generate", async (GenerateRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ProblemStatement))
        return Results.BadRequest(new ErrorResponse("Problem statement is required."));

    var text = req.ProblemStatement.Trim();

    if (text.Length < 10)
        return Results.BadRequest(new ErrorResponse("Please enter at least 10 characters."));

    var responseText =
        "Summary\n" +
        "- Problem: inconsistent sprint velocity and unclear acceptance criteria\n" +
        "- Impact: unpredictable planning and rework\n\n" +
        "Recommended actions\n" +
        "1) Add Definition of Ready\n" +
        "2) Standardize acceptance criteria\n" +
        "3) Run weekly backlog refinement\n\n" +
        "Input received:\n" +
        text;

    var record = new GenerateRecord(
        Guid.NewGuid().ToString("N"),
        DateTimeOffset.UtcNow,
        text,
        responseText
    );

    await store.AppendAsync(record);

    return Results.Ok(record);
});

app.MapGet("/api/records", async () =>
{
    var all = await store.ReadAllAsync();
    return Results.Ok(all.OrderByDescending(r => r.CreatedUtc));
});

app.MapGet("/api/records/{id}", async (string id) =>
{
    var all = await store.ReadAllAsync();
    var match = all.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    return match is null
        ? Results.NotFound(new ErrorResponse("Record not found."))
        : Results.Ok(match);
});

app.Run();

record GenerateRequest(string ProblemStatement);

record GenerateRecord(
    string Id,
    DateTimeOffset CreatedUtc,
    string ProblemStatement,
    string ResponseText
);

record ErrorResponse(string Message);

sealed class JsonStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public JsonStore(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(_path))
            File.WriteAllText(_path, "[]");
    }

    public async Task<List<GenerateRecord>> ReadAllAsync()
    {
        await using var fs = File.OpenRead(_path);
        return (await JsonSerializer.DeserializeAsync<List<GenerateRecord>>(fs)) ?? new();
    }

    public async Task AppendAsync(GenerateRecord record)
    {
        var all = await ReadAllAsync();
        all.Add(record);
        await using var fs = File.Create(_path);
        await JsonSerializer.SerializeAsync(fs, all, _jsonOptions);
    }
}
