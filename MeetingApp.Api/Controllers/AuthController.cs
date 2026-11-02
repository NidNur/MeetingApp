using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using MeetingApp.Api.Dtos.Auth;
using MeetingApp.Api.Services;
using MeetingApp.Context;
using MeetingApp.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MeetingApp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly ProfileImageStorage _profileImageStorage;
    private readonly JwtTokenService _jwtTokenService;
    private readonly NotificationEmailService _notificationEmails;

    public AuthController(
        AppDbContext context,
        IPasswordHasher<AppUser> passwordHasher,
        ProfileImageStorage profileImageStorage,
        JwtTokenService jwtTokenService,
        NotificationEmailService notificationEmails)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _profileImageStorage = profileImageStorage;
        _jwtTokenService = jwtTokenService;
        _notificationEmails = notificationEmails;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [Consumes("application/json")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await _context.Users.SingleOrDefaultAsync(
            existingUser => existingUser.NormalizedEmail == normalizedEmail,
            cancellationToken);

        if (user is null)
        {
            return Unauthorized(new { message = "E-posta veya parola hatalı." });
        }

        var verification = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "E-posta veya parola hatalı." });
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        }

        if (!string.IsNullOrWhiteSpace(request.TimeZoneId))
        {
            user.TimeZoneId = request.TimeZoneId.Trim();
        }

        if (_context.ChangeTracker.HasChanges())
            await _context.SaveChangesAsync(cancellationToken);

        return Ok(_jwtTokenService.CreateToken(user));
    }

    [Authorize]
    [HttpGet("me")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users.AsNoTracking()
            .Where(existingUser => existingUser.Id == userId)
            .Select(existingUser => new
            {
                existingUser.Id,
                existingUser.FirstName,
                existingUser.LastName,
                existingUser.Email,
                existingUser.PhoneNumber,
                existingUser.TimeZoneId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(user);
    }

    [Authorize]
    [HttpPut("me/time-zone")]
    [Consumes("application/json")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateTimeZone(
        [FromBody] UpdateTimeZoneRequest request,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
            return Unauthorized();

        var user = await _context.Users.SingleOrDefaultAsync(
            existingUser => existingUser.Id == userId, cancellationToken);
        if (user is null)
            return Unauthorized();

        user.TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? null : request.TimeZoneId.Trim();
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.PhoneNumber,
            user.TimeZoneId
        });
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromForm] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();

        var emailExists = await _context.Users.AnyAsync(
            existingUser => existingUser.NormalizedEmail == normalizedEmail,
            cancellationToken);

        if (emailExists)
        {
            return Conflict(new
            {
                message = "Bu e-posta adresi zaten kayıtlı."
            });
        }

        var user = new AppUser
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            PhoneNumber = request.PhoneNumber.Trim(),
            TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId)
                ? null : request.TimeZoneId.Trim()
        };

        user.PasswordHash = _passwordHasher.HashPassword(
            user,
            request.Password);

        if (request.ProfileImage is not null)
        {
            try
            {
                user.ProfileImagePath = await _profileImageStorage.SaveAsync(
                    request.ProfileImage,
                    cancellationToken);
            }
            catch (ValidationException exception)
            {
                ModelState.AddModelError(nameof(request.ProfileImage), exception.Message);
                return ValidationProblem(ModelState);
            }
        }

        try
        {
            _context.Users.Add(user);
            _notificationEmails.QueueWelcome(user);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException
            {
                Number: 2601 or 2627
            })
        {
            _profileImageStorage.TryDelete(user.ProfileImagePath);

            return Conflict(new
            {
                message = "Bu e-posta adresi zaten kayıtlı."
            });
        }
        catch
        {
            _profileImageStorage.TryDelete(user.ProfileImagePath);
            throw;
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            message = "Kullanıcı kaydı oluşturuldu.",
            userId = user.Id
        });
    }
}
