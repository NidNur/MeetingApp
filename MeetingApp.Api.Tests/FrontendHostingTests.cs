using System.Net;
using MeetingApp.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeetingApp.Api.Tests;

public class FrontendHostingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Hosting_is_disabled_unless_explicitly_enabled(bool? enabled)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await FrontendHost.StartAsync(enabled, cancellationToken);
        using var index = await host.Client.GetAsync("/", cancellationToken);
        using var page = await host.Client.GetAsync("/meetings/42/edit", cancellationToken);
        using var asset = await host.Client.GetAsync("/assets/app.js", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, index.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, asset.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/meetings/42/edit")]
    public async Task Enabled_host_serves_the_angular_shell_on_home_and_deep_links(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await FrontendHost.StartAsync(true, cancellationToken);
        using var response = await host.Client.GetAsync(path, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FrontendHost.Shell, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Enabled_host_serves_nested_build_assets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await FrontendHost.StartAsync(true, cancellationToken);
        using var response = await host.Client.GetAsync("/assets/app.js", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("window.meetingAppFixture = true;", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/missing")]
    [InlineData("/api/missing.json")]
    public async Task Unknown_api_routes_return_404_instead_of_the_angular_shell(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await FrontendHost.StartAsync(true, cancellationToken);
        using var response = await host.Client.GetAsync(path, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/Storage/ProfileImages/private.png")]
    [InlineData("/Storage/MeetingDocuments/private.pdf")]
    [InlineData("/Storage/no-extension")]
    public async Task Private_storage_is_not_exposed_by_static_files_or_fallback(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await FrontendHost.StartAsync(true, cancellationToken);
        using var response = await host.Client.GetAsync(path, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("private fixture", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Existing_api_and_health_routes_keep_their_own_responses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await FrontendHost.StartAsync(true, cancellationToken);
        using var api = await host.Client.GetAsync("/api/ping", cancellationToken);
        using var health = await host.Client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.Equal("application/json", api.Content.Headers.ContentType?.MediaType);
        Assert.Equal("\"api fixture\"", await api.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("health fixture", await health.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Enabled_host_without_a_built_index_reports_a_clear_configuration_error()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FrontendHost.StartAsync(true, TestContext.Current.CancellationToken, includeIndex: false));
        Assert.Contains("wwwroot/index.html", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FrontendHost : IAsyncDisposable
    {
        public const string Shell = "<!doctype html><html><body><app-root>MeetingApp fixture</app-root></body></html>";
        private readonly string _directory;
        private readonly WebApplication _app;
        public HttpClient Client { get; }

        private FrontendHost(string directory, WebApplication app)
        {
            _directory = directory;
            _app = app;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(5) };
        }

        public static async Task<FrontendHost> StartAsync(
            bool? enabled, CancellationToken cancellationToken, bool includeIndex = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), "MeetingAppFrontendTests", Guid.NewGuid().ToString("N"));
            var wwwroot = Path.Combine(directory, "wwwroot");
            Directory.CreateDirectory(Path.Combine(wwwroot, "assets"));
            Directory.CreateDirectory(Path.Combine(directory, "Storage", "ProfileImages"));
            Directory.CreateDirectory(Path.Combine(directory, "Storage", "MeetingDocuments"));
            if (includeIndex) await File.WriteAllTextAsync(Path.Combine(wwwroot, "index.html"), Shell, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(wwwroot, "assets", "app.js"), "window.meetingAppFixture = true;", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "Storage", "ProfileImages", "private.png"), "private fixture", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "Storage", "MeetingDocuments", "private.pdf"), "private fixture", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "Storage", "no-extension"), "private fixture", cancellationToken);

            WebApplication? app = null;
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    Args = [],
                    ApplicationName = typeof(FrontendHostingTests).Assembly.FullName,
                    ContentRootPath = directory,
                    WebRootPath = wwwroot,
                    EnvironmentName = Environments.Production
                });
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Logging.ClearProviders();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FrontendHosting:Enabled"] = enabled?.ToString()
                });
                app = builder.Build();
                app.UseFrontendHosting();
                app.MapGet("/api/ping", () => Results.Json("api fixture"));
                app.MapGet("/health/ready", () => "health fixture");
                await app.StartAsync(cancellationToken);
                return new FrontendHost(directory, app);
            }
            catch
            {
                if (app is not null) await app.DisposeAsync();
                DeleteFixture(directory);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            try
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _app.StopAsync(stopTimeout.Token);
            }
            finally
            {
                try
                {
                    await _app.DisposeAsync();
                }
                finally
                {
                    DeleteFixture(_directory);
                }
            }
        }

        private static void DeleteFixture(string directory)
        {
            var fixtureRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MeetingAppFrontendTests"))
                + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(directory);
            if (!fullPath.StartsWith(fixtureRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a path outside the owned frontend test directory.");
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
        }
    }
}
