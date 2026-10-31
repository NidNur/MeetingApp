using MeetingApp.Model.Entities;

namespace MeetingApp.Api.Services;

public interface IEmailSender
{
    Task SendAsync(EmailOutboxMessage message, CancellationToken cancellationToken);
}
