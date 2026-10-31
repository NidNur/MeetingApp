using MeetingApp.Api.Options;
using MeetingApp.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingApp.Api.Services;

public class MeetingCleanupService
{
    private readonly AppDbContext _context;
    private readonly MeetingDocumentStorage _documentStorage;
    private readonly MeetingCleanupOptions _options;
    private readonly TimeProvider _timeProvider;

    public MeetingCleanupService(
        AppDbContext context,
        MeetingDocumentStorage documentStorage,
        IOptions<MeetingCleanupOptions> options,
        TimeProvider timeProvider)
    {
        _context = context;
        _documentStorage = documentStorage;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-_options.RetentionHours);
        var candidates = await _context.Meetings.AsNoTracking()
            .Where(meeting => meeting.IsCancelled
                && meeting.CancelledAtUtc.HasValue
                && meeting.CancelledAtUtc.Value <= cutoff)
            .OrderBy(meeting => meeting.CancelledAtUtc)
            .ThenBy(meeting => meeting.Id)
            .Select(meeting => new { meeting.Id, meeting.DocumentPath })
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        var deletedCount = 0;
        foreach (var candidate in candidates)
        {
            var affectedRows = await _context.Meetings
                .Where(meeting => meeting.Id == candidate.Id
                    && meeting.IsCancelled
                    && meeting.CancelledAtUtc.HasValue
                    && meeting.CancelledAtUtc.Value <= cutoff
                    && meeting.DocumentPath == candidate.DocumentPath)
                .ExecuteDeleteAsync(cancellationToken);

            if (affectedRows == 0) continue;

            deletedCount += affectedRows;
            _documentStorage.TryDelete(candidate.DocumentPath);
        }

        return deletedCount;
    }
}
