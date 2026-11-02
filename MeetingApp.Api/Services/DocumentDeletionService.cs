using MeetingApp.Api.Options;
using MeetingApp.Context;
using MeetingApp.Model.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingApp.Api.Services;

public class DocumentDeletionService
{
    private readonly AppDbContext _context;
    private readonly MeetingDocumentStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly MeetingCleanupOptions _options;
    private readonly ILogger<DocumentDeletionService> _logger;

    public DocumentDeletionService(
        AppDbContext context,
        MeetingDocumentStorage storage,
        TimeProvider timeProvider,
        IOptions<MeetingCleanupOptions> options,
        ILogger<DocumentDeletionService> logger)
    {
        _context = context;
        _storage = storage;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    // The caller saves this job in the same transaction that unlinks its document.
    public DocumentDeletionJob? Enqueue(string? documentPath)
    {
        if (documentPath is null) return null;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var job = new DocumentDeletionJob
        {
            DocumentPath = documentPath,
            CreatedAtUtc = now,
            NextAttemptAtUtc = now
        };
        _context.DocumentDeletionJobs.Add(job);
        return job;
    }

    public async Task ProcessAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id.HasValue) await TryProcessAsync(id.Value, cancellationToken);
    }

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var candidates = await _context.DocumentDeletionJobs.AsNoTracking()
            .Where(job => job.NextAttemptAtUtc <= now)
            .OrderBy(job => job.NextAttemptAtUtc)
            .ThenBy(job => job.Id)
            .Select(job => job.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        var completed = 0;
        foreach (var id in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryProcessAsync(id, cancellationToken)) completed++;
        }
        return completed;
    }

    private async Task<bool> TryProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var job = await _context.DocumentDeletionJobs.AsNoTracking()
                .SingleOrDefaultAsync(job => job.Id == id && job.NextAttemptAtUtc <= now,
                    cancellationToken);
            if (job is null) return false;

            // A queued path must never remove a document still attached to a meeting.
            if (await _context.Meetings.AnyAsync(meeting => meeting.DocumentPath == job.DocumentPath,
                    cancellationToken))
            {
                _logger.LogWarning("Doküman hâlâ kullanımda; silme ertelendi. Kuyruk kaydı: {JobId}", id);
                await ScheduleRetryAsync(id, cancellationToken);
                return false;
            }

            if (!_storage.TryDelete(job.DocumentPath))
            {
                await ScheduleRetryAsync(id, cancellationToken);
                return false;
            }

            // File.Delete also succeeds for an absent file, so retrying after a crash is safe.
            return await _context.DocumentDeletionJobs.Where(job => job.Id == id)
                .ExecuteDeleteAsync(cancellationToken) > 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Doküman silme işi tamamlanamadı. Kuyruk kaydı: {JobId}. Yeniden denenecek.", id);
            try
            {
                await ScheduleRetryAsync(id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception retryException)
            {
                _logger.LogError(retryException,
                    "Doküman yeniden deneme zamanı kaydedilemedi. Kuyruk kaydı korunuyor: {JobId}", id);
            }
            return false;
        }
    }

    private Task<int> ScheduleRetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var nextAttempt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.PollIntervalSeconds);
        return _context.DocumentDeletionJobs.Where(job => job.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.AttemptCount, job => job.AttemptCount + 1)
                .SetProperty(job => job.NextAttemptAtUtc, nextAttempt), cancellationToken);
    }
}
