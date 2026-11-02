using System.Text;
using MeetingApp.Api.OpenApi;
using MeetingApp.Api.Options;
using MeetingApp.Api.Services;
using MeetingApp.Api.Workers;
using MeetingApp.Context;
using MeetingApp.Model.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection bağlantı ayarı bulunamadı.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<ProfileImageStorage>();
builder.Services.AddScoped<MeetingDocumentStorage>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<DatabaseStartupOptions>()
    .BindConfiguration("DatabaseStartup")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<MeetingCleanupService>();
builder.Services.AddOptions<MeetingCleanupOptions>()
    .BindConfiguration("MeetingCleanup")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<MeetingCleanupWorker>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<NotificationEmailService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<EmailOutboxProcessor>();
builder.Services.AddOptions<SmtpOptions>()
    .BindConfiguration("Smtp")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<EmailDeliveryOptions>()
    .BindConfiguration("EmailDelivery")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<EmailDeliveryWorker>();

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration("Jwt")
    .ValidateDataAnnotations()
    .Validate(options => !string.IsNullOrWhiteSpace(options.SigningKey)
        && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
        "JWT imzalama anahtarı en az 32 bayt olmalıdır.")
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, settings) =>
    {
        var jwt = settings.Value;
        bearer.MapInboundClaims = false;
        bearer.IncludeErrorDetails = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            NameClaimType = JwtRegisteredClaimNames.Sub,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecurityTransformer>();
    options.AddOperationTransformer<BearerSecurityTransformer>();
});

var app = builder.Build();

await DatabaseStartup.ApplyMigrationsAsync(async cancellationToken =>
{
    await using var scope = app.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.MigrateAsync(cancellationToken);
}, app.Services.GetRequiredService<IOptions<DatabaseStartupOptions>>().Value,
    app.Logger, app.Services.GetRequiredService<TimeProvider>(), app.Lifetime.ApplicationStopping);

if (app.Configuration.GetValue<bool?>("Swagger:Enabled") ?? app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "MeetingApp API v1"));
}

if (app.Configuration.GetValue("HttpsRedirection:Enabled", true))
    app.UseHttpsRedirection();
app.UseFrontendHosting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health/ready", async (AppDbContext context, CancellationToken cancellationToken) =>
{
    var ready = await context.Database.CanConnectAsync(cancellationToken);
    return ready ? (IResult)Results.Ok(new { status = "ready" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();
app.Run();
