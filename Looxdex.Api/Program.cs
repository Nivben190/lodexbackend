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
// Postgres (Supabase). The connection string comes from configuration:
// user-secrets locally, ConnectionStrings__Looxdex in the environment on deploy.
var connectionString = builder.Configuration.GetConnectionString("Looxdex")
                       ?? BuildSupabaseConnectionString(builder.Configuration);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No database connection string. Set ConnectionStrings__Looxdex, or Supabase:Host " +
        "and Supabase:DbPassword.");
}

builder.Services.AddDbContext<LooxdexDbContext>(options =>
    options
        .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3))
        // Postgres folds unquoted identifiers to lowercase, so EF's default
        // PascalCase would force quoting in every hand-written query and RLS
        // policy. snake_case keeps the schema usable outside EF.
        .UseSnakeCaseNamingConvention());

/// <summary>
/// Assembles a Supavisor connection string from the project's pieces, so only
/// the password has to be a secret.
/// </summary>
static string? BuildSupabaseConnectionString(IConfiguration config)
{
    var host = config["Supabase:Host"];
    var password = config["Supabase:DbPassword"];
    var projectRef = config["Supabase:ProjectRef"];

    if (string.IsNullOrWhiteSpace(host) ||
        string.IsNullOrWhiteSpace(password) ||
        string.IsNullOrWhiteSpace(projectRef))
    {
        return null;
    }

    // Session mode (5432) rather than transaction mode (6543): this is a
    // long-lived server with its own connection pool, and session mode keeps
    // prepared statements working. Transaction-mode pooling shares connections
    // between requests, which breaks them.
    var port = config["Supabase:Port"] ?? "5432";

    return new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = host,
        Port = int.Parse(port),
        Database = config["Supabase:Database"] ?? "postgres",
        Username = $"postgres.{projectRef}",
        Password = password,
        SslMode = Npgsql.SslMode.Require,
        Pooling = true,
        MinPoolSize = 0,
        MaxPoolSize = 10,
        Timeout = 20,
        CommandTimeout = 60
    }.ConnectionString;
}

// ---- app services --------------------------------------------------------
builder.Services.AddHttpContextAccessor();

// Still the source of the demo closet/suitcase content and the weather stub.
builder.Services.AddSingleton<LooxdexSeedData>();

builder.Services.AddScoped<IOwnerContext, HeaderOwnerContext>();
builder.Services.AddScoped<FeedReadService>();
builder.Services.AddScoped<FeedIngestService>();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<StarterClosetService>();

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
