using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MeetingApp.Api.Options;
using MeetingApp.Api.Services;
using MeetingApp.Context;
using MeetingApp.Model.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MeetingApp.Api.Tests;

public sealed class DocumentQueueSqlFactAttribute : FactAttribute
{
    public DocumentQueueSqlFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MEETINGAPP_DOCUMENT_QUEUE_TEST_CONNECTION")))
            Skip = "Set MEETINGAPP_DOCUMENT_QUEUE_TEST_CONNECTION to an isolated local DocumentQueueCheck_ database.";
    }
}

public sealed class WindowsDocumentQueueSqlFactAttribute : FactAttribute
{
    public WindowsDocumentQueueSqlFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows())
            Skip = "This regression deliberately uses Windows exclusive file sharing to reproduce a delete failure.";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MEETINGAPP_DOCUMENT_QUEUE_TEST_CONNECTION")))
            Skip = "Set MEETINGAPP_DOCUMENT_QUEUE_TEST_CONNECTION to an isolated local DocumentQueueCheck_ database.";
    }
}

public class DocumentDeletionTests
{
    [WindowsDocumentQueueSqlFact]
    public async Task Locked_document_remains_queued_and_is_deleted_after_the_lock_is_released()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await QueueFixture.CreateAsync(cancellationToken);
        await using var context = fixture.Context();
        var service = fixture.Service(context);
        var fileName = fixture.WriteDocument();
        var job = service.Enqueue(fileName)!;
        await context.SaveChangesAsync(cancellationToken);

        using (var exclusiveHandle = new FileStream(fixture.DocumentPath(fileName), FileMode.Open,
            FileAccess.Read, FileShare.None))
        {
            await service.ProcessAsync(job.Id, cancellationToken);

            var pending = await context.DocumentDeletionJobs.AsNoTracking()
                .SingleAsync(current => current.Id == job.Id, cancellationToken);
            Assert.True(File.Exists(fixture.DocumentPath(fileName)));
            Assert.Equal(1, pending.AttemptCount);
            Assert.True(pending.NextAttemptAtUtc > fixture.Clock.GetUtcNow().UtcDateTime);
        }

        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        await service.ProcessPendingAsync(cancellationToken);

        Assert.False(File.Exists(fixture.DocumentPath(fileName)));
        Assert.False(await context.DocumentDeletionJobs.AnyAsync(current => current.Id == job.Id, cancellationToken));
    }

    [DocumentQueueSqlFact]
    public async Task Already_missing_document_completes_the_job_without_failure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await QueueFixture.CreateAsync(cancellationToken);
        await using var context = fixture.Context();
        var service = fixture.Service(context);
        var fileName = $"{Guid.NewGuid():N}.pdf";
        var job = service.Enqueue(fileName)!;
        await context.SaveChangesAsync(cancellationToken);

        await service.ProcessAsync(job.Id, cancellationToken);

        Assert.False(File.Exists(fixture.DocumentPath(fileName)));
        Assert.False(await context.DocumentDeletionJobs.AnyAsync(current => current.Id == job.Id, cancellationToken));
    }

    [DocumentQueueSqlFact]
    public async Task A_document_still_referenced_by_a_meeting_is_preserved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await QueueFixture.CreateAsync(cancellationToken);
        await using var context = fixture.Context();
        var fileName = fixture.WriteDocument();
        var user = new AppUser
        {
            FirstName = "Queue", LastName = "Fixture", Email = $"queue-{Guid.NewGuid():N}@example.test",
            NormalizedEmail = $"QUEUE-{Guid.NewGuid():N}@EXAMPLE.TEST", PhoneNumber = "+905550000001",
            PasswordHash = "test-fixture-only"
        };
        var meeting = new Meeting
        {
            Name = "Protect a referenced document", Description = "Isolated queue regression fixture",
            StartsAtUtc = fixture.Clock.GetUtcNow().UtcDateTime,
            EndsAtUtc = fixture.Clock.GetUtcNow().UtcDateTime.AddHours(1),
            DocumentPath = fileName, DocumentOriginalName = "fixture.pdf", CreatedBy = user
        };
        context.Meetings.Add(meeting);
        var service = fixture.Service(context);
        var job = service.Enqueue(fileName)!;
        await context.SaveChangesAsync(cancellationToken);

        await service.ProcessAsync(job.Id, cancellationToken);

        Assert.True(File.Exists(fixture.DocumentPath(fileName)));
        Assert.True(await context.Meetings.AnyAsync(current => current.Id == meeting.Id
            && current.DocumentPath == fileName, cancellationToken));
        Assert.True(await context.DocumentDeletionJobs.AnyAsync(current => current.Id == job.Id, cancellationToken));
    }

    [DocumentQueueSqlFact]
    public async Task Pending_document_job_survives_disposal_and_is_processed_by_a_fresh_context()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await QueueFixture.CreateAsync(cancellationToken);
        var fileName = fixture.WriteDocument();
        Guid jobId;
        await using (var original = fixture.Context())
        {
            var job = fixture.Service(original).Enqueue(fileName)!;
            jobId = job.Id;
            await original.SaveChangesAsync(cancellationToken);
        }

        await using var restarted = fixture.Context();
        Assert.True(await restarted.DocumentDeletionJobs.AnyAsync(current => current.Id == jobId, cancellationToken));
        Assert.True(File.Exists(fixture.DocumentPath(fileName)));

        await fixture.Service(restarted).ProcessPendingAsync(cancellationToken);

        Assert.False(await restarted.DocumentDeletionJobs.AnyAsync(current => current.Id == jobId, cancellationToken));
        Assert.False(File.Exists(fixture.DocumentPath(fileName)));
    }

    private sealed class QueueFixture
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly TestWebEnvironment _environment;
        public MutableClock Clock { get; } = new();

        private QueueFixture(DbContextOptions<AppDbContext> options, string contentRoot)
        {
            _options = options;
            _environment = new TestWebEnvironment { ContentRootPath = contentRoot };
            Directory.CreateDirectory(Path.Combine(contentRoot, "Storage", "MeetingDocuments"));
        }

        public static async Task<QueueFixture> CreateAsync(CancellationToken cancellationToken)
        {
            AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);
            var configured = Environment.GetEnvironmentVariable("MEETINGAPP_DOCUMENT_QUEUE_TEST_CONNECTION");
            Assert.False(string.IsNullOrWhiteSpace(configured));
            var connection = new SqlConnectionStringBuilder(configured);
            Assert.Matches(new Regex(@"^(?:tcp:)?(?:localhost|127\.0\.0\.1)(?:,[0-9]{1,5})?$", RegexOptions.IgnoreCase),
                connection.DataSource);
            Assert.Matches(@"^DocumentQueueCheck_[A-Za-z0-9_]{6,100}$", connection.InitialCatalog);

            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection.ConnectionString).Options;
            var root = Path.Combine(Path.GetTempPath(), "MeetingAppDocumentDeletionTests", Guid.NewGuid().ToString("N"));
            var fixture = new QueueFixture(options, root);
            await using var context = fixture.Context();
            await context.Database.MigrateAsync(cancellationToken);
            // Keep the owned database and scratch directories for inspection; never drop or recursively delete them.
            return fixture;
        }

        public AppDbContext Context() => new(_options);

        public DocumentDeletionService Service(AppDbContext context) => new(context,
            new MeetingDocumentStorage(_environment, NullLogger<MeetingDocumentStorage>.Instance),
            Clock, Microsoft.Extensions.Options.Options.Create(new MeetingCleanupOptions { PollIntervalSeconds = 60 }),
            NullLogger<DocumentDeletionService>.Instance);

        public string DocumentPath(string fileName) => Path.Combine(_environment.ContentRootPath,
            "Storage", "MeetingDocuments", fileName);

        public string WriteDocument()
        {
            var fileName = $"{Guid.NewGuid():N}.pdf";
            File.WriteAllText(DocumentPath(fileName), "%PDF-1.4\nIsolated deletion regression fixture\n%%EOF");
            return fileName;
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow = _utcNow.Add(elapsed);
    }

    private sealed class TestWebEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MeetingApp.DocumentDeletion.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
