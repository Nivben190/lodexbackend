using Looxdex.Api.Configuration;
using Looxdex.Api.Data;
using Looxdex.Api.Services;
using Looxdex.Api.Services.Detection;
using Looxdex.Api.Services.Ingest;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicy = "LooxdexWebClient";

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Looxdex API",
        Version = "v1",
        Description = "Feed, closet, daily look and suitcase endpoints for the Looxdex app."
    });
});

// ---- configuration -------------------------------------------------------
builder.Services.Configure<PexelsOptions>(
    builder.Configuration.GetSection(PexelsOptions.SectionName));
builder.Services.Configure<OnnxDetectionOptions>(
    builder.Configuration.GetSection(OnnxDetectionOptions.SectionName));

// ---- persistence ---------------------------------------------------------
// SQLite for now. Moving to Supabase Postgres in phase 1 is a provider swap here;
// the entities, queries and controllers are untouched by it.
var connectionString = builder.Configuration.GetConnectionString("Looxdex")
                       ?? "Data Source=looxdex.db";

builder.Services.AddDbContext<LooxdexDbContext>(options =>
    options.UseSqlite(connectionString));

// ---- app services --------------------------------------------------------
builder.Services.AddHttpContextAccessor();

// Still the source of the demo closet/suitcase content and the weather stub.
builder.Services.AddSingleton<LooxdexSeedData>();

builder.Services.AddScoped<IOwnerContext, HeaderOwnerContext>();
builder.Services.AddScoped<FeedReadService>();
builder.Services.AddScoped<FeedIngestService>();
builder.Services.AddScoped<DatabaseSeeder>();

builder.Services.AddHttpClient<IImageSource, PexelsImageSource>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Both hold state that must outlive a request — the cached model path and the
// loaded InferenceSession — so they are singletons taking IHttpClientFactory
// rather than typed clients, which would be transient and reload the model.
builder.Services.AddHttpClient(ModelProvider.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});

builder.Services.AddHttpClient(OnnxFashionDetector.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddSingleton<ModelProvider>();
builder.Services.AddSingleton<IFashionDetector, OnnxFashionDetector>();

builder.Services.AddHostedService<IngestWorker>();
builder.Services.AddHostedService<DetectionWorker>();

// ---- CORS ----------------------------------------------------------------
var extraOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        policy.WithOrigins(new[]
            {
                "http://localhost:4200",
                "https://localhost:4200",
                "https://looxdex.netlify.app"
            }.Concat(extraOrigins).ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Apply migrations and seed the demo content on boot.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LooxdexDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Looxdex API v1");
    });
}

app.UseCors(CorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.Run();
