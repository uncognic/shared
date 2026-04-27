using shared.Services;

namespace shared.Services;

public class ExpiryService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ExpiryService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public ExpiryService(IServiceProvider services, ILogger<ExpiryService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, stoppingToken);

            var fs = _services.GetRequiredService<FileService>();
            var count = await fs.DeleteExpiredAsync();

            if (count > 0)
                _logger.LogInformation("Expiry sweep: deleted {Count} expired file(s).", count);
        }
    }
}