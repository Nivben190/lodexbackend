using Looxdex.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Looxdex.Api.Services.Ingest;

/// <summary>Tops the feed library up on a timer, well inside the provider's rate limit.</summary>
public class IngestWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PexelsOptions _options;
    private readonly ILogger<IngestWorker> _logger;

    public IngestWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PexelsOptions> options,
        ILogger<IngestWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || _options.Interval <= TimeSpan.Zero)
        {
            _logger.LogInformation("Ingest worker idle: Pexels is not configured.");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        // Page advances each run so repeat queries return fresh photos rather
        // than the same first page over and over.
        var page = 1;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ingest = scope.ServiceProvider.GetRequiredService<FeedIngestService>();

                var result = await ingest.IngestAsync(null, page, null, stoppingToken);
                _logger.LogInformation(
                    "Scheduled ingest added {Added} posts from page {Page}.", result.Added, page);

                // Pexels stops returning results deep into the result set; wrap around.
                page = result.Added == 0 ? 1 : page + 1;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled ingest failed.");
            }

            try
            {
                await Task.Delay(_options.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
