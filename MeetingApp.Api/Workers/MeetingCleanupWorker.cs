using MeetingApp.Api.Options;
using MeetingApp.Api.Services;
using Microsoft.Extensions.Options;

namespace MeetingApp.Api.Workers;

public class MeetingCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MeetingCleanupOptions _options;
    private readonly ILogger<MeetingCleanupWorker> _logger;

    public MeetingCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MeetingCleanupOptions> options,
        ILogger<MeetingCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        try
        {
            do
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var cleanupService = scope.ServiceProvider
                        .GetRequiredService<MeetingCleanupService>();
                    var deletedCount = await cleanupService.CleanupAsync(stoppingToken);

                    if (deletedCount > 0)
                        _logger.LogInformation(
                            "İptal süresi dolan {Count} toplantı kalıcı olarak silindi.", deletedCount);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception,
                        "İptal edilen toplantılar temizlenemedi. Bir sonraki periyotta yeniden denenecek.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
