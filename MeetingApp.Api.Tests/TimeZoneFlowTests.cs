using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MeetingApp.Context;
using MeetingApp.Model.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

namespace MeetingApp.Api.Tests;

public sealed class LocalSqlFactAttribute : FactAttribute
{
    public LocalSqlFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (Environment.GetEnvironmentVariable("MEETINGAPP_RUN_SQL_TESTS") != "1")
            Skip = "Set MEETINGAPP_RUN_SQL_TESTS=1 to run the disposable local SQL/API test.";
    }
}

public class TimeZoneFlowTests
{
    private const string Password = "TimeZoneTest2026!";
    private static readonly DateTime StartUtc = new(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc);

    [LocalSqlFact]
    public async Task Migration_auth_profile_and_all_meeting_emails_preserve_UTC_and_use_the_recipient_zone()
    {
        AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);
        var repo = FindRepository();
        var configured = Environment.GetEnvironmentVariable("MEETINGAPP_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            var secretId = XDocument.Load(Path.Combine(repo, "MeetingApp.Api", "MeetingApp.Api.csproj"))
                .Descendants("UserSecretsId").Single().Value;
            configured = new ConfigurationBuilder().AddUserSecrets(secretId).Build()
                .GetConnectionString("DefaultConnection");
        }
        Assert.False(string.IsNullOrWhiteSpace(configured), "Configure a local SQL connection before enabling this test.");
        var masterBuilder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = "master", Pooling = false, ConnectTimeout = 5
        };
        Assert.True(masterBuilder.DataSource is "127.0.0.1,1433" or "localhost,1433",
            "The test only permits SQL on 127.0.0.1,1433 or localhost,1433.");
        var databaseName = "TimeZoneCheck_" + Guid.NewGuid().ToString("N");
        var testBuilder = new SqlConnectionStringBuilder(masterBuilder.ConnectionString)
        {
            InitialCatalog = databaseName, Pooling = true
        };
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(testBuilder.ConnectionString).Options;
        var scratch = Path.Combine(Path.GetTempPath(), "MeetingAppTimeZoneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var cancellationToken = deadline.Token;
        var databaseCreated = false;
        Process? api = null;
        Task? stdout = null;
        Task? stderr = null;

        try
        {
            await ExecuteSql(masterBuilder.ConnectionString, $"CREATE DATABASE [{databaseName}]", cancellationToken);
            databaseCreated = true;

            await using (var context = new AppDbContext(dbOptions))
            {
                await context.GetService<IMigrator>().MigrateAsync("20261031181404_AddEmailOutbox", cancellationToken);
            }
            var legacy = new AppUser { FirstName = "Legacy", LastName = "Fixture", Email = "legacy@example.test" };
            var hash = new PasswordHasher<AppUser>().HashPassword(legacy, Password);
            var legacyUserId = await ScalarInt(testBuilder.ConnectionString, """
                INSERT INTO dbo.Users (FirstName, LastName, Email, NormalizedEmail, PhoneNumber, PasswordHash, CreatedAtUtc)
                VALUES ('Legacy', 'Fixture', 'legacy@example.test', 'LEGACY@EXAMPLE.TEST', '+905321234567', @hash, SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, cancellationToken, new SqlParameter("@hash", hash));
            var legacyMeetingId = await ScalarInt(testBuilder.ConnectionString, """
                INSERT INTO dbo.Meetings (Name, Description, StartsAtUtc, EndsAtUtc, IsCancelled, CreatedAtUtc, CreatedById)
                VALUES ('Pre-migration meeting', 'Public disposable fixture', @start, @end, 0, SYSUTCDATETIME(), @owner);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, cancellationToken, new SqlParameter("@start", StartUtc),
                new SqlParameter("@end", StartUtc.AddHours(1)), new SqlParameter("@owner", legacyUserId));

            await using (var context = new AppDbContext(dbOptions))
            {
                await context.Database.MigrateAsync(cancellationToken);
                var afterMigration = await context.Meetings.AsNoTracking()
                    .SingleAsync(meeting => meeting.Id == legacyMeetingId, cancellationToken);
                Assert.Equal(StartUtc.Ticks, afterMigration.StartsAtUtc.Ticks);
                Assert.Equal(StartUtc.AddHours(1).Ticks, afterMigration.EndsAtUtc.Ticks);
                Assert.Null((await context.Users.AsNoTracking().SingleAsync(user => user.Id == legacyUserId, cancellationToken)).TimeZoneId);
            }

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var address = $"http://127.0.0.1:{port}";
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
            var apiDll = Path.Combine(repo, "MeetingApp.Api", "bin", configuration, "net10.0", "MeetingApp.Api.dll");
            Assert.True(File.Exists(apiDll), "Build the API in the same configuration before running this test.");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                WorkingDirectory = scratch, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(apiDll);
            start.ArgumentList.Add("--urls"); start.ArgumentList.Add(address);
            start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(scratch);
            foreach (var pair in new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development", ["DOTNET_ENVIRONMENT"] = "Development",
                ["ConnectionStrings__DefaultConnection"] = testBuilder.ConnectionString,
                ["Jwt__Issuer"] = "TimeZoneTestIssuer", ["Jwt__Audience"] = "TimeZoneTestClient",
                ["Jwt__SigningKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), ["Jwt__ExpirationMinutes"] = "30",
                ["MeetingCleanup__Enabled"] = "false", ["EmailDelivery__Enabled"] = "false",
                ["Smtp__Host"] = "127.0.0.1", ["Smtp__Port"] = "1025", ["Smtp__Security"] = "None",
                ["Smtp__FromAddress"] = "noreply@example.test", ["Smtp__FromName"] = "MeetingApp Test",
                ["Smtp__Username"] = "", ["Smtp__Password"] = "", ["Logging__LogLevel__Default"] = "None",
                ["ASPNETCORE_HTTPS_PORT"] = "", ["ASPNETCORE_HTTPS_PORTS"] = "", ["DOTNET_HTTPS_PORTS"] = ""
            }) start.Environment[pair.Key] = pair.Value;
            api = Process.Start(start);
            Assert.NotNull(api);
            stdout = Drain(api.StandardOutput);
            stderr = Drain(api.StandardError);
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5)
            };
            await WaitUntilReady(http, api, cancellationToken);

            using (var response = await http.PutAsJsonAsync("/api/auth/me/time-zone", new { timeZoneId = "Europe/Rome" }, cancellationToken))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            foreach (var invalid in new[] { "Invalid/Zone", "Turkey Standard Time" })
            {
                using var invalidForm = Registration("invalid-" + Guid.NewGuid().ToString("N") + "@example.test", invalid);
                using var response = await http.PostAsync("/api/auth/register", invalidForm, cancellationToken);
                await AssertTimeZoneError(response, cancellationToken);
            }
            await using (var context = new AppDbContext(dbOptions))
                Assert.Equal(1, await context.Users.CountAsync(cancellationToken));

            const string recipient = "rome@example.test";
            using (var form = Registration(recipient, "Europe/Rome"))
            using (var response = await http.PostAsync("/api/auth/register", form, cancellationToken))
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            await Authenticate(http, recipient, "Europe/Istanbul", cancellationToken);
            Assert.Equal("Europe/Rome", await StoredZone(dbOptions, recipient, cancellationToken));
            using (var me = await http.GetAsync("/api/auth/me", cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, me.StatusCode);
                var body = await Json(me, cancellationToken);
                Assert.Equal("Europe/Rome", body.GetProperty("timeZoneId").GetString());
            }

            var meetingId = await CreateMeeting(http, "Rome meeting", cancellationToken);
            await using (var context = new AppDbContext(dbOptions))
            {
                var meeting = await context.Meetings.AsNoTracking().SingleAsync(m => m.Id == meetingId, cancellationToken);
                Assert.Equal(StartUtc.Ticks, meeting.StartsAtUtc.Ticks);
                Assert.Equal(StartUtc.AddHours(1).Ticks, meeting.EndsAtUtc.Ticks);
            }
            var createdMail = await LatestEmail(dbOptions, recipient, "Toplantınız oluşturuldu", cancellationToken);
            Assert.Contains("Başlangıç: 01.11.2026 12:00 (Europe/Rome, UTC+01:00)", createdMail.TextBody);
            Assert.Contains("Bitiş: 01.11.2026 13:00 (Europe/Rome, UTC+01:00)", createdMail.TextBody);
            Assert.Null(createdMail.SentAtUtc);
            Assert.Equal(createdMail.CreatedAtUtc, createdMail.NextAttemptAtUtc);
            await using (var context = new AppDbContext(dbOptions))
            {
                var meetingMails = await context.EmailOutboxMessages.AsNoTracking()
                    .Where(message => message.Subject.StartsWith("Toplantınız")).ToListAsync(cancellationToken);
                Assert.Single(meetingMails);
                Assert.Equal(recipient, meetingMails[0].RecipientEmail);
            }

            foreach (var invalid in new[] { "Invalid/Zone", "Turkey Standard Time" })
            {
                using (var response = await http.PutAsJsonAsync("/api/auth/me/time-zone", new { timeZoneId = invalid }, cancellationToken))
                    await AssertTimeZoneError(response, cancellationToken);
                using (var response = await http.PostAsJsonAsync("/api/auth/login", new { email = recipient, password = Password, timeZoneId = invalid }, cancellationToken))
                    await AssertTimeZoneError(response, cancellationToken);
                Assert.Equal("Europe/Rome", await StoredZone(dbOptions, recipient, cancellationToken));
            }

            using (var response = await http.PutAsJsonAsync("/api/auth/me/time-zone", new { timeZoneId = "Europe/Istanbul" }, cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await Json(response, cancellationToken);
                Assert.Equal("Europe/Istanbul", body.GetProperty("timeZoneId").GetString());
            }
            using (var form = MeetingForm("Istanbul update"))
            using (var response = await http.PutAsync($"/api/meetings/{meetingId}", form, cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("2026-11-01T11:00:00Z", (await Json(response, cancellationToken)).GetProperty("startsAtUtc").GetString());
            }
            var updatedMail = await LatestEmail(dbOptions, recipient, "Toplantınız güncellendi", cancellationToken);
            Assert.Contains("Başlangıç: 01.11.2026 14:00 (Europe/Istanbul, UTC+03:00)", updatedMail.TextBody);
            Assert.Contains("Bitiş: 01.11.2026 15:00 (Europe/Istanbul, UTC+03:00)", updatedMail.TextBody);
            using (var response = await http.PostAsync($"/api/meetings/{meetingId}/cancel", null, cancellationToken))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var cancelledMail = await LatestEmail(dbOptions, recipient, "Toplantınız iptal edildi", cancellationToken);
            Assert.Contains("Başlangıç: 01.11.2026 14:00 (Europe/Istanbul, UTC+03:00)", cancelledMail.TextBody);

            using (var response = await http.PutAsJsonAsync("/api/auth/me/time-zone", new { timeZoneId = (string?)null }, cancellationToken))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(await StoredZone(dbOptions, recipient, cancellationToken));
            await CreateMeeting(http, "UTC fallback meeting", cancellationToken);
            var fallbackMail = await LatestEmail(dbOptions, recipient, "Toplantınız oluşturuldu", cancellationToken);
            Assert.Contains("Başlangıç: 01.11.2026 11:00 UTC", fallbackMail.TextBody);

            http.DefaultRequestHeaders.Authorization = null;
            using (var response = await http.PostAsJsonAsync("/api/auth/login", new { email = legacy.Email, password = "WrongPassword2026!", timeZoneId = "Europe/Rome" }, cancellationToken))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(await StoredZone(dbOptions, legacy.Email, cancellationToken));
            await Authenticate(http, legacy.Email, "Europe/Rome", cancellationToken);
            Assert.Equal("Europe/Rome", await StoredZone(dbOptions, legacy.Email, cancellationToken));
            await Authenticate(http, legacy.Email, "Europe/Istanbul", cancellationToken);
            Assert.Equal("Europe/Rome", await StoredZone(dbOptions, legacy.Email, cancellationToken));
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException($"Disposable local SQL test failed with SQL error {exception.Number}; private connection omitted.");
        }
        finally
        {
            if (api is not null)
            {
                if (!api.HasExited) api.Kill(entireProcessTree: true);
                await api.WaitForExitAsync();
                if (stdout is not null && stderr is not null) await Task.WhenAll(stdout, stderr);
                api.Dispose();
            }
            if (databaseCreated)
            {
                if (!Regex.IsMatch(databaseName, "^TimeZoneCheck_[a-f0-9]{32}$"))
                    throw new InvalidOperationException("Unsafe test database name; cleanup refused.");
                SqlConnection.ClearAllPools();
                try
                {
                    await ExecuteSql(masterBuilder.ConnectionString,
                        $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]", CancellationToken.None);
                }
                catch (SqlException exception)
                {
                    throw new InvalidOperationException($"Owned test database {databaseName} needs cleanup; SQL error {exception.Number}; private connection omitted.");
                }
            }
            var resolvedScratch = Path.GetFullPath(scratch);
            var scratchParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MeetingAppTimeZoneTests")) + Path.DirectorySeparatorChar;
            if (resolvedScratch.StartsWith(scratchParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolvedScratch))
                Directory.Delete(resolvedScratch, recursive: true);
        }
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "MeetingApp.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Run the test project inside the MeetingApp repository.");
    }

    private static async Task Drain(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is not null) { }
    }

    private static async Task WaitUntilReady(HttpClient http, Process api, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            Assert.False(api.HasExited, "The disposable API exited before becoming ready.");
            try
            {
                using var response = await http.GetAsync("/openapi/v1.json", cancellationToken);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100, cancellationToken);
        }
        Assert.Fail("The disposable API did not become ready.");
    }

    private static MultipartFormDataContent Registration(string email, string? timeZoneId)
    {
        var form = new MultipartFormDataContent();
        foreach (var pair in new Dictionary<string, string>
        {
            ["FirstName"] = "TimeZone", ["LastName"] = "Fixture", ["Email"] = email,
            ["PhoneNumber"] = "+905321234567", ["Password"] = Password
        }) form.Add(new StringContent(pair.Value), pair.Key);
        if (timeZoneId is not null) form.Add(new StringContent(timeZoneId), "TimeZoneId");
        return form;
    }

    private static MultipartFormDataContent MeetingForm(string name)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "Name");
        form.Add(new StringContent("Public disposable timezone fixture."), "Description");
        form.Add(new StringContent("2026-11-01T14:00:00+03:00"), "StartsAtUtc");
        form.Add(new StringContent("2026-11-01T15:00:00+03:00"), "EndsAtUtc");
        return form;
    }

    private static async Task Authenticate(HttpClient http, string email, string timeZoneId, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/api/auth/login", new { email, password = Password, timeZoneId }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response, cancellationToken);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
    }

    private static async Task<int> CreateMeeting(HttpClient http, string name, CancellationToken cancellationToken)
    {
        using var form = MeetingForm(name);
        using var response = await http.PostAsync("/api/meetings", form, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response, cancellationToken);
        Assert.Equal("2026-11-01T11:00:00Z", body.GetProperty("startsAtUtc").GetString());
        Assert.Equal("2026-11-01T12:00:00Z", body.GetProperty("endsAtUtc").GetString());
        return body.GetProperty("id").GetInt32();
    }

    private static async Task AssertTimeZoneError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await Json(response, cancellationToken);
        Assert.True(body.GetProperty("errors").TryGetProperty("TimeZoneId", out _));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return body.RootElement.Clone();
    }

    private static async Task<string?> StoredZone(DbContextOptions<AppDbContext> options, string email, CancellationToken cancellationToken)
    {
        await using var context = new AppDbContext(options);
        return await context.Users.AsNoTracking().Where(user => user.Email == email)
            .Select(user => user.TimeZoneId).SingleAsync(cancellationToken);
    }

    private static async Task<EmailOutboxMessage> LatestEmail(DbContextOptions<AppDbContext> options, string recipient, string subject, CancellationToken cancellationToken)
    {
        await using var context = new AppDbContext(options);
        return await context.EmailOutboxMessages.AsNoTracking()
            .Where(message => message.RecipientEmail == recipient && message.Subject == subject)
            .OrderByDescending(message => message.CreatedAtUtc).FirstAsync(cancellationToken);
    }

    private static async Task ExecuteSql(string connectionString, string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ScalarInt(string connectionString, string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }
}
