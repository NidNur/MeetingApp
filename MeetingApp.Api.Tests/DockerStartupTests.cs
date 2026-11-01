using System.ComponentModel.DataAnnotations;
using MailKit.Security;
using MeetingApp.Api.Options;
using MeetingApp.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingApp.Api.Tests;

public class DockerStartupTests
{
    [Fact]
    public async Task Local_startup_does_not_apply_migrations_without_opt_in()
    {
        var called = false;
        await Run(_ => { called = true; return Task.CompletedTask; }, new DatabaseStartupOptions(), TestContext.Current.CancellationToken);
        Assert.False(called);
    }

    [Fact]
    public async Task Startup_retries_a_temporary_database_timeout_and_then_continues()
    {
        var calls = 0;
        await Run(_ => ++calls == 1 ? Task.FromException(new TimeoutException()) : Task.CompletedTask,
            new DatabaseStartupOptions { ApplyMigrations = true, MaxAttempts = 2, RetryDelaySeconds = 1 }, TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Startup_retries_a_transient_failure_wrapped_by_EF_Core()
    {
        var calls = 0;
        await Run(_ => ++calls == 1
            ? Task.FromException(new InvalidOperationException("EF wrapper", new TimeoutException()))
            : Task.CompletedTask,
            new DatabaseStartupOptions { ApplyMigrations = true, MaxAttempts = 2, RetryDelaySeconds = 1 },
            TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Startup_fails_after_the_last_attempt_instead_of_serving_an_unmigrated_database()
    {
        var calls = 0;
        await Assert.ThrowsAsync<TimeoutException>(() => Run(_ =>
        {
            calls++;
            return Task.FromException(new TimeoutException());
        }, new DatabaseStartupOptions { ApplyMigrations = true, MaxAttempts = 2, RetryDelaySeconds = 1 }, TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Startup_does_not_retry_a_programming_or_migration_definition_error()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(_ =>
        {
            calls++;
            return Task.FromException(new InvalidOperationException());
        }, new DatabaseStartupOptions { ApplyMigrations = true }, TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancelled_startup_does_not_attempt_a_database_change()
    {
        var called = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, new DatabaseStartupOptions { ApplyMigrations = true }, new CancellationToken(canceled: true)));
        Assert.False(called);
    }

    [Fact]
    public async Task Cancellation_during_a_retry_does_not_start_another_attempt()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(_ =>
        {
            calls++;
            cancellation.Cancel();
            return Task.FromException(new TimeoutException());
        }, new DatabaseStartupOptions { ApplyMigrations = true }, cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(21, 5)]
    [InlineData(6, 0)]
    [InlineData(6, 61)]
    public void Startup_retry_configuration_rejects_unbounded_or_invalid_values(int attempts, int delay)
    {
        Assert.NotEmpty(Validate(new DatabaseStartupOptions { MaxAttempts = attempts, RetryDelaySeconds = delay }));
    }

    [Fact]
    public void Plaintext_Docker_SMTP_requires_an_explicit_opt_in()
    {
        var smtp = new SmtpOptions { Host = "mailpit", Security = SecureSocketOptions.None };
        Assert.Contains(Validate(smtp), error => error.MemberNames.Contains(nameof(SmtpOptions.Security)));
        smtp.AllowUnencryptedRelay = true;
        Assert.Empty(Validate(smtp));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Existing_local_Mailpit_settings_continue_to_work(string host)
    {
        Assert.Empty(Validate(new SmtpOptions { Host = host, Security = SecureSocketOptions.None }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Relay_opt_in_never_allows_credentials_over_plaintext(bool optIn)
    {
        var smtp = new SmtpOptions
        {
            Host = "mailpit", Security = SecureSocketOptions.None, AllowUnencryptedRelay = optIn,
            Username = "test-user", Password = "public-test-password"
        };
        Assert.Contains(Validate(smtp), error => error.ErrorMessage?.Contains("TLS") == true);
    }

    [Fact]
    public void Normal_remote_SMTP_with_TLS_remains_supported()
    {
        Assert.Empty(Validate(new SmtpOptions
        {
            Host = "smtp.example.test", Security = SecureSocketOptions.StartTls,
            Username = "test-user", Password = "public-test-password"
        }));
    }

    private static Task Run(Func<CancellationToken, Task> migrate, DatabaseStartupOptions options,
        CancellationToken cancellationToken = default) => DatabaseStartup.ApplyMigrationsAsync(
            migrate, options, NullLogger.Instance, TimeProvider.System, cancellationToken);

    private static List<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return results;
    }
}
