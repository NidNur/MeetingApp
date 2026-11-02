using MeetingApp.Api.Options;
using MeetingApp.Api.Services;
using Microsoft.Extensions.Options;

namespace MeetingApp.Api.Workers;

public class DocumentDeletionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MeetingCleanupOptions _options;
    private readonly ILogger<DocumentDeletionWorker> _logger;

    public DocumentDeletionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MeetingCleanupOptions> options,
        ILogger<DocumentDeletionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Document retries also run when automatic cancellation cleanup is disabled.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        try
        {
            do
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<DocumentDeletionService>();
                    var completed = await service.ProcessPendingAsync(stoppingToken);
                    if (completed > 0)
                        _logger.LogInformation("Kuyruktaki {Count} doküman silme işi tamamlandı.", completed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception,
                        "Doküman silme kuyruğu işlenemedi. Bir sonraki periyotta yeniden denenecek.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
