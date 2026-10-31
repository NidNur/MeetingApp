using MeetingApp.Api.Options;
using MeetingApp.Context;
using MeetingApp.Model.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingApp.Api.Services;

public class EmailOutboxProcessor
{
    private readonly AppDbContext _context;
    private readonly IEmailSender _sender;
    private readonly EmailDeliveryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmailOutboxProcessor> _logger;

    public EmailOutboxProcessor(
        AppDbContext context,
        IEmailSender sender,
        IOptions<EmailDeliveryOptions> options,
        TimeProvider timeProvider,
        ILogger<EmailOutboxProcessor> logger)
    {
        _context = context;
        _sender = sender;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> ProcessAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var messages = _context.Set<EmailOutboxMessage>();
        var candidates = await messages.AsNoTracking()
            .Where(message => message.SentAtUtc == null && message.NextAttemptAtUtc <= now
                && (message.LockedUntilUtc == null || message.LockedUntilUtc <= now))
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .Select(message => message.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        var sentCount = 0;
        foreach (var id in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            now = _timeProvider.GetUtcNow().UtcDateTime;
            var lockId = Guid.NewGuid();
            var lockedUntil = now.AddMinutes(2);
            var claimed = await messages
                .Where(message => message.Id == id && message.SentAtUtc == null
                    && message.NextAttemptAtUtc <= now
                    && (message.LockedUntilUtc == null || message.LockedUntilUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.LockId, (Guid?)lockId)
                    .SetProperty(message => message.LockedUntilUtc, (DateTime?)lockedUntil)
                    .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1),
                    cancellationToken);

            if (claimed == 0) continue;

            var email = await messages.AsNoTracking().SingleAsync(
                message => message.Id == id && message.LockId == lockId, cancellationToken);

            try
            {
                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                await _sender.SendAsync(email, sendTimeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var errorType = exception.GetType().Name;
                if (errorType.Length > 500) errorType = errorType[..500];
                var nextAttempt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.RetryDelaySeconds);
                await messages.Where(message => message.Id == id && message.LockId == lockId
                        && message.SentAtUtc == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(message => message.LastError, errorType)
                        .SetProperty(message => message.NextAttemptAtUtc, nextAttempt)
                        .SetProperty(message => message.LockId, (Guid?)null)
                        .SetProperty(message => message.LockedUntilUtc, (DateTime?)null), cancellationToken);

                _logger.LogWarning(
                    "E-posta gönderilemedi. Kayıt: {EmailId}, hata türü: {ErrorType}. Yeniden denenecek.",
                    id, errorType);
                continue;
            }

            var sentAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var updated = await messages.Where(message => message.Id == id && message.LockId == lockId
                    && message.SentAtUtc == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.SentAtUtc, (DateTime?)sentAtUtc)
                    .SetProperty(message => message.LastError, (string?)null)
                    .SetProperty(message => message.LockId, (Guid?)null)
                    .SetProperty(message => message.LockedUntilUtc, (DateTime?)null), cancellationToken);

            if (updated > 0) sentCount++;
        }

        return sentCount;
    }
}
