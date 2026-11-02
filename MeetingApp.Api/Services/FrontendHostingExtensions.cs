namespace MeetingApp.Api.Services;

public static class FrontendHostingExtensions
{
    public static WebApplication UseFrontendHosting(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("FrontendHosting:Enabled")) return app;

        var webRoot = app.Environment.WebRootPath
            ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        if (!File.Exists(Path.Combine(webRoot, "index.html")))
        {
            throw new InvalidOperationException(
                "FrontendHosting açık ancak wwwroot/index.html bulunamadı. "
                + "Yayın paketini PublishFrontend=true ile oluşturun.");
        }

        app.UseWhen(context =>
            !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !context.Request.Path.StartsWithSegments("/Storage", StringComparison.OrdinalIgnoreCase),
            frontend =>
            {
                frontend.UseDefaultFiles();
                frontend.UseStaticFiles();
            });

        app.MapFallback("/api/{**path}", () => Results.NotFound()).AllowAnonymous();
        app.MapFallback("/Storage/{**path}", () => Results.NotFound()).AllowAnonymous();
        app.MapFallbackToFile("index.html").AllowAnonymous();
        return app;
    }
}
