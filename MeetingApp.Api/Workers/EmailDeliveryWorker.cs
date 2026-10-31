using MeetingApp.Api.Options;
using MeetingApp.Api.Services;
using Microsoft.Extensions.Options;

namespace MeetingApp.Api.Workers;

public class EmailDeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailDeliveryOptions _options;
    private readonly ILogger<EmailDeliveryWorker> _logger;

    public EmailDeliveryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<EmailDeliveryOptions> options,
        ILogger<EmailDeliveryWorker> logger)
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
                    var processor = scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>();
                    var sentCount = await processor.ProcessAsync(stoppingToken);
                    if (sentCount > 0)
                        _logger.LogInformation("Kuyruktaki {Count} e-posta SMTP sunucusuna teslim edildi.", sentCount);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        "E-posta kuyruğu işlenemedi. Hata türü: {ErrorType}. Sonraki turda yeniden denenecek.",
                        exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
