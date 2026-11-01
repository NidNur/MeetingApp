using MeetingApp.Api.Options;
using Microsoft.Data.SqlClient;

namespace MeetingApp.Api.Services;

public static class DatabaseStartup
{
    public static async Task ApplyMigrationsAsync(
        Func<CancellationToken, Task> migrate,
        DatabaseStartupOptions options,
        ILogger logger,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!options.ApplyMigrations) return;

        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await migrate(cancellationToken);
                logger.LogInformation("Veritabanı migration'ları uygulandı.");
                return;
            }
            catch (Exception exception) when (attempt < options.MaxAttempts && IsTransient(exception))
            {
                // Keep connection strings and secret configuration out of retry logs.
                logger.LogWarning(
                    "Veritabanı henüz hazır değil. Deneme {Attempt}/{MaxAttempts}; {DelaySeconds} saniye sonra yeniden denenecek. Hata türü: {ErrorType}",
                    attempt, options.MaxAttempts, options.RetryDelaySeconds, exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(options.RetryDelaySeconds), timeProvider, cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        TimeoutException => true,
        SqlException { Number: -2 or 2 or 53 or 64 or 233 or 4060 or 927 or 928
            or 17142 or 18401 or 10053 or 10054 or 10060 or 11001 } => true,
        // EF Core can wrap transient SQL failures in InvalidOperationException.
        InvalidOperationException { InnerException: { } inner } => IsTransient(inner),
        _ => false
    };
}
