using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MeetingApp.Api.Controllers;
using MeetingApp.Api.Dtos.Auth;
using MeetingApp.Api.Options;
using MeetingApp.Api.Services;
using MeetingApp.Context;
using MeetingApp.Model.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingApp.Api.Tests;

public sealed class LoginTimeZoneSqlFactAttribute : FactAttribute
{
    public LoginTimeZoneSqlFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MEETINGAPP_LOGIN_TIMEZONE_TEST_CONNECTION")))
            Skip = "Set MEETINGAPP_LOGIN_TIMEZONE_TEST_CONNECTION to an isolated local TimeZoneLoginCheck_ database.";
    }
}

public class LoginTimeZoneTests
{
    private const string Password = "TimeZoneLogin2026!";
    private static readonly DateTime StartUtc = new(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc);

    [LoginTimeZoneSqlFact]
    public async Task Each_successful_login_updates_the_zone_used_by_new_meeting_emails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await LoginFixture.CreateAsync("Europe/Istanbul", cancellationToken);
        var meeting = new Meeting
        {
            Name = "Automatic browser time zone", Description = "Isolated login fixture",
            StartsAtUtc = StartUtc, EndsAtUtc = StartUtc.AddHours(1), CreatedById = fixture.User.Id
        };
        fixture.Context.Meetings.Add(meeting);
        await fixture.Context.SaveChangesAsync(cancellationToken);

        Assert.IsType<OkObjectResult>(await fixture.Login("Europe/Rome", Password, cancellationToken));
        Assert.Equal("Europe/Rome", await fixture.StoredZone(cancellationToken));
        await fixture.Notifications.QueueMeetingAsync(meeting, MeetingNotificationType.Created, cancellationToken);
        await fixture.Context.SaveChangesAsync(cancellationToken);
        Assert.Contains("Başlangıç: 01.11.2026 12:00 (Europe/Rome, UTC+01:00)",
            (await fixture.Context.EmailOutboxMessages.AsNoTracking()
                .SingleAsync(message => message.RecipientEmail == fixture.User.Email, cancellationToken)).TextBody);

        Assert.IsType<OkObjectResult>(await fixture.Login("Europe/Istanbul", Password, cancellationToken));
        Assert.Equal("Europe/Istanbul", await fixture.StoredZone(cancellationToken));
        await fixture.Notifications.QueueMeetingAsync(meeting, MeetingNotificationType.Updated, cancellationToken);
        await fixture.Context.SaveChangesAsync(cancellationToken);
        Assert.Contains("Başlangıç: 01.11.2026 14:00 (Europe/Istanbul, UTC+03:00)",
            (await fixture.Context.EmailOutboxMessages.AsNoTracking().SingleAsync(message =>
                message.RecipientEmail == fixture.User.Email && message.Subject == "Toplantınız güncellendi",
                cancellationToken)).TextBody);
        var savedMeeting = await fixture.Context.Meetings.AsNoTracking()
            .SingleAsync(current => current.Id == meeting.Id, cancellationToken);
        Assert.Equal(StartUtc.Ticks, savedMeeting.StartsAtUtc.Ticks);
        Assert.Equal(StartUtc.AddHours(1).Ticks, savedMeeting.EndsAtUtc.Ticks);
        Assert.All(await fixture.Context.EmailOutboxMessages.AsNoTracking()
            .Where(message => message.RecipientEmail == fixture.User.Email).ToListAsync(cancellationToken),
            message => Assert.Null(message.SentAtUtc));
    }

    [LoginTimeZoneSqlFact]
    public async Task Missing_or_blank_browser_zone_does_not_erase_the_last_known_zone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await LoginFixture.CreateAsync("Europe/Rome", cancellationToken);
        foreach (var zone in new string?[] { null, "", "   " })
        {
            Assert.IsType<OkObjectResult>(await fixture.Login(zone, Password, cancellationToken));
            Assert.Equal("Europe/Rome", await fixture.StoredZone(cancellationToken));
        }
    }

    [LoginTimeZoneSqlFact]
    public async Task Wrong_password_never_changes_the_stored_zone_or_password_hash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await LoginFixture.CreateAsync("Europe/Istanbul", cancellationToken);
        var originalHash = fixture.User.PasswordHash;

        Assert.IsType<UnauthorizedObjectResult>(await fixture.Login("Europe/Rome", "WrongPassword2026!", cancellationToken));

        Assert.Equal("Europe/Istanbul", await fixture.StoredZone(cancellationToken));
        Assert.Equal(originalHash, await fixture.Context.Users.AsNoTracking()
            .Where(user => user.Id == fixture.User.Id).Select(user => user.PasswordHash).SingleAsync(cancellationToken));
    }

    [LoginTimeZoneSqlFact]
    public async Task A_legacy_user_gets_the_trimmed_browser_zone_and_password_rehash_together()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await LoginFixture.CreateAsync(null, cancellationToken, legacyHash: true);
        var originalHash = fixture.User.PasswordHash;

        Assert.IsType<OkObjectResult>(await fixture.Login(" Europe/Rome ", Password, cancellationToken));

        Assert.Equal("Europe/Rome", await fixture.StoredZone(cancellationToken));
        var savedHash = await fixture.Context.Users.AsNoTracking().Where(user => user.Id == fixture.User.Id)
            .Select(user => user.PasswordHash).SingleAsync(cancellationToken);
        Assert.NotEqual(originalHash, savedHash);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<AppUser>().VerifyHashedPassword(fixture.User, savedHash, Password));
    }

    private sealed class LoginFixture : IAsyncDisposable
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly AuthController _controller;
        public AppDbContext Context { get; }
        public AppUser User { get; }
        public NotificationEmailService Notifications { get; }

        private LoginFixture(DbContextOptions<AppDbContext> options, AppDbContext context, AppUser user)
        {
            _options = options;
            Context = context;
            User = user;
            Notifications = new NotificationEmailService(context, TimeProvider.System);
            _controller = new AuthController(context, new PasswordHasher<AppUser>(),
                new ProfileImageStorage(new TestEnvironment(), NullLogger<ProfileImageStorage>.Instance),
                new JwtTokenService(Microsoft.Extensions.Options.Options.Create(new JwtOptions
                {
                    Issuer = "TimeZoneLoginTests", Audience = "TimeZoneLoginTests",
                    SigningKey = "isolated-test-signing-key-with-at-least-32-bytes", ExpirationMinutes = 5
                })), Notifications);
        }

        public static async Task<LoginFixture> CreateAsync(string? zone, CancellationToken cancellationToken, bool legacyHash = false)
        {
            AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);
            var connection = new SqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("MEETINGAPP_LOGIN_TIMEZONE_TEST_CONNECTION"));
            Assert.Matches(new Regex(@"^(?:tcp:)?(?:localhost|127\.0\.0\.1)(?:,[0-9]{1,5})?$", RegexOptions.IgnoreCase),
                connection.DataSource);
            Assert.Matches(@"^TimeZoneLoginCheck_[A-Za-z0-9_]{6,100}$", connection.InitialCatalog);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection.ConnectionString).Options;
            var context = new AppDbContext(options);
            await context.Database.MigrateAsync(cancellationToken);
            var email = $"timezone-login-{Guid.NewGuid():N}@example.test";
            var user = new AppUser
            {
                FirstName = "TimeZone", LastName = "LoginFixture", Email = email,
                NormalizedEmail = email.ToUpperInvariant(), PhoneNumber = "+905550000001", TimeZoneId = zone
            };
            var hasher = legacyHash
                ? new PasswordHasher<AppUser>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions
                    { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2 }))
                : new PasswordHasher<AppUser>();
            user.PasswordHash = hasher.HashPassword(user, Password);
            context.Users.Add(user);
            await context.SaveChangesAsync(cancellationToken);
            // Keep the isolated database for inspection; no database or upload-folder cleanup is performed.
            return new LoginFixture(options, context, user);
        }

        public Task<IActionResult> Login(string? zone, string password, CancellationToken cancellationToken) =>
            _controller.Login(new LoginRequest { Email = User.Email, Password = password, TimeZoneId = zone }, cancellationToken);

        public async Task<string?> StoredZone(CancellationToken cancellationToken)
        {
            await using var freshContext = new AppDbContext(_options);
            return await freshContext.Users.AsNoTracking().Where(user => user.Id == User.Id)
                .Select(user => user.TimeZoneId).SingleAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MeetingApp.LoginTimeZone.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
