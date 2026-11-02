using System.Text;
using MailKit.Net.Smtp;
using MeetingApp.Api.Options;
using MeetingApp.Model.Entities;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MeetingApp.Api.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(EmailOutboxMessage message, CancellationToken cancellationToken)
    {
        using var email = new MimeMessage();
        email.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        email.To.Add(new MailboxAddress(message.RecipientName, message.RecipientEmail));
        email.Subject = message.Subject;
        email.MessageId = $"{message.Id:N}.outbox@meetingapp.local";
        email.Date = new DateTimeOffset(DateTime.SpecifyKind(message.CreatedAtUtc, DateTimeKind.Utc));
        var body = new TextPart("plain");
        body.SetText(Encoding.UTF8, message.TextBody);
        email.Body = body;

        using var client = new SmtpClient { Timeout = 10_000 };
        var stage = SmtpDeliveryStage.Connect;
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, _options.Security, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                stage = SmtpDeliveryStage.Authenticate;
                await client.AuthenticateAsync(_options.Username, _options.Password!, cancellationToken);
            }

            stage = SmtpDeliveryStage.Send;
            await client.SendAsync(email, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SmtpDeliveryException(stage, exception);
        }
    }
}
