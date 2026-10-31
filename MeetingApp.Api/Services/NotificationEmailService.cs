using System.Globalization;
using System.Text;
using MeetingApp.Context;
using MeetingApp.Model.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeetingApp.Api.Services;

public enum MeetingNotificationType
{
    Created,
    Updated,
    Cancelled
}

public class NotificationEmailService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public NotificationEmailService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public void QueueWelcome(AppUser user)
    {
        var recipientName = $"{user.FirstName} {user.LastName}".Trim();
        var body = $"""
            Merhaba {recipientName},

            MeetingApp'e hoş geldiniz. Hesabınız başarıyla oluşturuldu.
            Artık toplantılarınızı oluşturabilir, düzenleyebilir ve dokümanlarınızı yönetebilirsiniz.

            İyi çalışmalar,
            MeetingApp
            """;

        Queue(user.Email, recipientName, "MeetingApp'e hoş geldiniz", body);
    }

    public async Task QueueMeetingAsync(
        Meeting meeting,
        MeetingNotificationType type,
        CancellationToken cancellationToken)
    {
        var owner = await _context.Users.AsNoTracking()
            .Where(user => user.Id == meeting.CreatedById)
            .Select(user => new { user.Email, user.FirstName, user.LastName })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Toplantının sahibi bulunamadı.");

        var (subject, change) = type switch
        {
            MeetingNotificationType.Created =>
                ("Toplantınız oluşturuldu", "Toplantınız oluşturuldu."),
            MeetingNotificationType.Updated =>
                ("Toplantınız güncellendi", "Toplantınızın bilgileri güncellendi."),
            MeetingNotificationType.Cancelled =>
                ("Toplantınız iptal edildi", "Toplantınız iptal edildi."),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        var recipientName = $"{owner.FirstName} {owner.LastName}".Trim();
        var body = new StringBuilder()
            .AppendLine($"Merhaba {recipientName},")
            .AppendLine()
            .AppendLine(change)
            .AppendLine()
            .AppendLine($"Toplantı no: {meeting.Id}")
            .AppendLine($"Toplantı adı: {meeting.Name}")
            .AppendLine($"Başlangıç: {FormatUtc(meeting.StartsAtUtc)}")
            .AppendLine($"Bitiş: {FormatUtc(meeting.EndsAtUtc)}")
            .AppendLine()
            .AppendLine("Açıklama:")
            .AppendLine(meeting.Description);

        if (!string.IsNullOrWhiteSpace(meeting.DocumentOriginalName))
            body.AppendLine().AppendLine($"Doküman: {meeting.DocumentOriginalName}");

        body.AppendLine()
            .AppendLine("İyi çalışmalar,")
            .AppendLine("MeetingApp");

        Queue(owner.Email, recipientName, subject, body.ToString());
    }

    private void Queue(string recipientEmail, string recipientName, string subject, string textBody)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _context.EmailOutboxMessages.Add(new EmailOutboxMessage
        {
            Id = Guid.NewGuid(),
            RecipientEmail = recipientEmail.Trim(),
            RecipientName = recipientName,
            Subject = subject,
            TextBody = textBody,
            CreatedAtUtc = now,
            NextAttemptAtUtc = now
        });
    }

    private static string FormatUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc)
            .ToString("dd.MM.yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
