using Looxdex.Api.Data;

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
        Description = "Mock backend for the Looxdex fashion magazine app — feed, closet, daily look and suitcase endpoints."
    });
});

builder.Services.AddSingleton<LooxdexSeedData>();

var extraOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        policy.WithOrigins(new[] { "http://localhost:4200", "https://localhost:4200" }.Concat(extraOrigins).ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

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

app.Run();
