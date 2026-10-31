using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using MeetingApp.Api.Dtos.Meetings;
using MeetingApp.Api.Services;
using MeetingApp.Context;
using MeetingApp.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MeetingApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/meetings")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class MeetingsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly MeetingDocumentStorage _documentStorage;

    public MeetingsController(AppDbContext context, MeetingDocumentStorage documentStorage)
    {
        _context = context;
        _documentStorage = documentStorage;
    }

    [HttpGet]
    [ProducesResponseType(typeof(MeetingListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();

        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            ModelState.AddModelError(nameof(page), "Sayfa numarası çok büyük.");
            return ValidationProblem(ModelState);
        }

        var query = _context.Meetings.AsNoTracking()
            .Where(meeting => meeting.CreatedById == userId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var meetings = await query
            .OrderByDescending(meeting => meeting.CreatedAtUtc)
            .ThenByDescending(meeting => meeting.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new MeetingListResponse
        {
            Items = meetings.Select(ToResponse).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MeetingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();

        var meeting = await _context.Meetings.AsNoTracking().SingleOrDefaultAsync(
            meeting => meeting.Id == id && meeting.CreatedById == userId.Value,
            cancellationToken);

        return meeting is null ? NotFound() : Ok(ToResponse(meeting));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [ProducesResponseType(typeof(MeetingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        [FromForm] MeetingRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();

        StoredMeetingDocument? document = null;
        if (request.Document is not null)
        {
            try
            {
                document = await _documentStorage.SaveAsync(request.Document, cancellationToken);
            }
            catch (ValidationException exception)
            {
                ModelState.AddModelError(nameof(request.Document), exception.Message);
                return ValidationProblem(ModelState);
            }
        }

        var meeting = new Meeting
        {
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            StartsAtUtc = request.StartsAtUtc!.Value.UtcDateTime,
            EndsAtUtc = request.EndsAtUtc!.Value.UtcDateTime,
            CreatedById = userId.Value,
            DocumentPath = document?.FileName,
            DocumentOriginalName = document?.OriginalName
        };

        try
        {
            _context.Meetings.Add(meeting);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _documentStorage.TryDelete(document?.FileName);
            throw;
        }

        return CreatedAtAction(nameof(GetById), new { id = meeting.Id }, ToResponse(meeting));
    }

    [HttpPut("{id:int}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [ProducesResponseType(typeof(MeetingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        int id,
        [FromForm] MeetingRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();

        var meeting = await _context.Meetings.AsNoTracking().SingleOrDefaultAsync(
            meeting => meeting.Id == id && meeting.CreatedById == userId.Value,
            cancellationToken);

        if (meeting is null) return NotFound();
        if (meeting.IsCancelled)
        {
            return Conflict(new { message = "İptal edilmiş toplantı güncellenemez." });
        }

        StoredMeetingDocument? document = null;
        if (request.Document is not null)
        {
            try
            {
                document = await _documentStorage.SaveAsync(request.Document, cancellationToken);
            }
            catch (ValidationException exception)
            {
                ModelState.AddModelError(nameof(request.Document), exception.Message);
                return ValidationProblem(ModelState);
            }
        }

        var oldFile = meeting.DocumentPath;
        var fileName = document?.FileName ?? oldFile;
        var originalName = document?.OriginalName ?? meeting.DocumentOriginalName;
        var name = request.Name.Trim();
        var description = request.Description.Trim();
        var startsAt = request.StartsAtUtc!.Value.UtcDateTime;
        var endsAt = request.EndsAtUtc!.Value.UtcDateTime;

        int updated;
        try
        {
            updated = await _context.Meetings
                .Where(current => current.Id == id && current.CreatedById == userId.Value
                    && current.DocumentPath == oldFile && !current.IsCancelled)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(current => current.Name, name)
                    .SetProperty(current => current.Description, description)
                    .SetProperty(current => current.StartsAtUtc, startsAt)
                    .SetProperty(current => current.EndsAtUtc, endsAt)
                    .SetProperty(current => current.DocumentPath, fileName)
                    .SetProperty(current => current.DocumentOriginalName, originalName), cancellationToken);
        }
        catch
        {
            _documentStorage.TryDelete(document?.FileName);
            throw;
        }

        if (updated == 0)
        {
            _documentStorage.TryDelete(document?.FileName);
            return ChangedMeeting();
        }

        if (document is not null) _documentStorage.TryDelete(oldFile);

        meeting.Name = name;
        meeting.Description = description;
        meeting.StartsAtUtc = startsAt;
        meeting.EndsAtUtc = endsAt;
        meeting.DocumentPath = fileName;
        meeting.DocumentOriginalName = originalName;
        return Ok(ToResponse(meeting));
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();

        var meeting = await _context.Meetings.AsNoTracking().SingleOrDefaultAsync(
            meeting => meeting.Id == id && meeting.CreatedById == userId.Value,
            cancellationToken);

        if (meeting is null) return NotFound();

        var deleted = await _context.Meetings
            .Where(current => current.Id == id && current.CreatedById == userId.Value
                && current.DocumentPath == meeting.DocumentPath)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0) return ChangedMeeting();

        _documentStorage.TryDelete(meeting.DocumentPath);
        return NoContent();
    }

    [HttpGet("{id:int}/document")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDocument(int id, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();

        var meeting = await _context.Meetings.AsNoTracking().SingleOrDefaultAsync(
            meeting => meeting.Id == id && meeting.CreatedById == userId.Value,
            cancellationToken);

        if (meeting?.DocumentPath is null) return NotFound();

        var stream = _documentStorage.OpenRead(meeting.DocumentPath);
        if (stream is null) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(stream, _documentStorage.GetContentType(meeting.DocumentPath),
            meeting.DocumentOriginalName ?? meeting.DocumentPath, enableRangeProcessing: true);
    }

    [HttpDelete("{id:int}/document")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDocument(int id, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        if (userId is null) return Unauthorized();

        var meeting = await _context.Meetings.AsNoTracking().SingleOrDefaultAsync(
            meeting => meeting.Id == id && meeting.CreatedById == userId.Value,
            cancellationToken);

        if (meeting?.DocumentPath is null) return NotFound();
        if (meeting.IsCancelled)
        {
            return Conflict(new { message = "İptal edilmiş toplantı güncellenemez." });
        }

        var updated = await _context.Meetings
            .Where(current => current.Id == id && current.CreatedById == userId.Value
                && current.DocumentPath == meeting.DocumentPath && !current.IsCancelled)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(current => current.DocumentPath, (string?)null)
                .SetProperty(current => current.DocumentOriginalName, (string?)null), cancellationToken);

        if (updated == 0) return ChangedMeeting();

        _documentStorage.TryDelete(meeting.DocumentPath);
        return NoContent();
    }

    private async Task<int?> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
        {
            return null;
        }

        return await _context.Users.AnyAsync(user => user.Id == userId, cancellationToken)
            ? userId : null;
    }

    private ConflictObjectResult ChangedMeeting() => Conflict(new
    {
        message = "Toplantı başka bir işlemle değiştirildi. Yenileyip tekrar deneyin."
    });

    private static MeetingResponse ToResponse(Meeting meeting) => new()
    {
        Id = meeting.Id,
        Name = meeting.Name,
        Description = meeting.Description,
        StartsAtUtc = DateTime.SpecifyKind(meeting.StartsAtUtc, DateTimeKind.Utc),
        EndsAtUtc = DateTime.SpecifyKind(meeting.EndsAtUtc, DateTimeKind.Utc),
        CreatedAtUtc = DateTime.SpecifyKind(meeting.CreatedAtUtc, DateTimeKind.Utc),
        IsCancelled = meeting.IsCancelled,
        CancelledAtUtc = meeting.CancelledAtUtc.HasValue
            ? DateTime.SpecifyKind(meeting.CancelledAtUtc.Value, DateTimeKind.Utc) : null,
        DocumentOriginalName = meeting.DocumentOriginalName,
        HasDocument = meeting.DocumentPath is not null
    };
}
