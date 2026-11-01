using System.ComponentModel.DataAnnotations;
using MailKit.Security;
using MimeKit;

namespace MeetingApp.Api.Options;

public class SmtpOptions : IValidatableObject
{
    [Required, StringLength(253)]
    public string Host { get; set; } = "127.0.0.1";

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;

    [Required, StringLength(256)]
    public string FromAddress { get; set; } = "noreply@meetingapp.test";

    [Required, StringLength(100)]
    public string FromName { get; set; } = "MeetingApp";

    public string? Username { get; set; }

    public string? Password { get; set; }

    // Enable only for a trusted development relay on a private Docker network.
    public bool AllowUnencryptedRelay { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Security is not (SecureSocketOptions.None or SecureSocketOptions.StartTls
            or SecureSocketOptions.SslOnConnect))
        {
            yield return new ValidationResult(
                "SMTP güvenliği None, StartTls veya SslOnConnect olmalıdır.", [nameof(Security)]);
        }

        if (Security == SecureSocketOptions.None && Host is not ("127.0.0.1" or "::1")
            && !AllowUnencryptedRelay)
        {
            yield return new ValidationResult(
                "Şifrelenmemiş SMTP için yerel test sunucusu veya açıkça izin verilen bir test relay'i kullanılmalıdır.",
                [nameof(Security), nameof(Host)]);
        }

        var hasUsername = !string.IsNullOrWhiteSpace(Username);
        var hasPassword = !string.IsNullOrEmpty(Password);
        if (hasUsername != hasPassword)
        {
            yield return new ValidationResult(
                "SMTP kullanıcı adı ve parolası birlikte tanımlanmalıdır.",
                [nameof(Username), nameof(Password)]);
        }

        if ((hasUsername || hasPassword) && Security == SecureSocketOptions.None)
        {
            yield return new ValidationResult(
                "SMTP kimlik bilgileri için TLS bağlantısı gereklidir.", [nameof(Security)]);
        }

        if (!MailboxAddress.TryParse(FromAddress, out var from)
            || !string.Equals(from.Address, FromAddress, StringComparison.Ordinal)
            || ContainsNewLine(FromAddress))
        {
            yield return new ValidationResult(
                "Gönderici adresi geçerli, tek bir e-posta adresi olmalıdır.", [nameof(FromAddress)]);
        }

        if (ContainsNewLine(Host) || ContainsNewLine(FromName))
        {
            yield return new ValidationResult(
                "SMTP sunucusu ve gönderici adı satır sonu içeremez.", [nameof(Host), nameof(FromName)]);
        }
    }

    private static bool ContainsNewLine(string? value) =>
        value?.IndexOfAny(['\r', '\n']) >= 0;
}
