using System.Globalization;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace MeetingApp.Api.Services;

public enum SmtpDeliveryStage
{
    Connect,
    Authenticate,
    Send
}

public sealed class SmtpDeliveryException : Exception
{
    private const string DisconnectWithResponsePrefix = "The SMTP server has unexpectedly disconnected: ";
    private static readonly (string Marker, string Reason)[] KnownServerReasons =
    [
        ("BadCredentials", "BadCredentials"),
        ("InvalidSecondFactor", "InvalidSecondFactor"),
        ("Application-specific password required", "ApplicationSpecificPasswordRequired"),
        ("Too many login attempts", "TooManyLoginAttempts"),
        ("WebLoginRequired", "WebLoginRequired"),
        ("AccountDisabled", "AccountDisabled")
    ];

    public SmtpDeliveryStage Stage { get; }

    public SmtpDeliveryException(SmtpDeliveryStage stage, Exception innerException)
        : base("SMTP delivery failed.", innerException)
    {
        Stage = stage;
    }

    public static string GetSafeSummary(Exception exception)
    {
        var delivery = exception as SmtpDeliveryException;
        var stage = delivery?.Stage switch
        {
            SmtpDeliveryStage.Connect => "Connect",
            SmtpDeliveryStage.Authenticate => "Authenticate",
            SmtpDeliveryStage.Send => "Send",
            _ => "Unknown"
        };
        var errors = new List<Exception>();
        Exception? current = delivery?.InnerException ?? exception;
        for (var depth = 0; current is not null && depth < 6; depth++, current = current.InnerException)
            errors.Add(current);

        var fields = new List<string> { $"stage={stage}", $"type={SafeType(errors[0])}" };
        if (errors.Count > 1)
            fields.Add($"inner={string.Join(">", errors.Skip(1).Select(SafeType))}");

        foreach (var protocol in errors.OfType<SmtpProtocolException>())
        {
            if (AddProtocolDetails(fields, protocol.Message)) break;
        }

        if (errors.OfType<SocketException>().FirstOrDefault() is { } socket)
        {
            fields.Add($"socket={socket.SocketErrorCode}");
            fields.Add($"native={socket.NativeErrorCode.ToString(CultureInfo.InvariantCulture)}");
        }
        if (errors.OfType<SmtpCommandException>().FirstOrDefault() is { } command)
        {
            fields.Add($"smtpStatus={((int)command.StatusCode).ToString(CultureInfo.InvariantCulture)}");
            fields.Add($"smtpError={command.ErrorCode}");
        }

        var summary = string.Join(";", fields);
        return summary.Length > 500 ? summary[..500] : summary;
    }

    private static bool AddProtocolDetails(List<string> fields, string message)
    {
        var category = message switch
        {
            "The SMTP server has unexpectedly disconnected." => "UnexpectedDisconnect",
            "Unable to parse status code returned by the server." => "UnparseableStatusCode",
            "Invalid status code returned by the server." => "InvalidStatusCode",
            "The status codes returned by the server did not match." => "MismatchedStatusCodes",
            _ => null
        };
        if (category is not null)
        {
            fields.Add($"category={category}");
            return true;
        }
        if (!message.StartsWith(DisconnectWithResponsePrefix, StringComparison.Ordinal)) return false;

        fields.Add("category=UnexpectedDisconnect");
        var response = message[DisconnectWithResponsePrefix.Length..];
        // Only an anchored numeric code and constant reason labels may leave this method.
        var status = Regex.Match(response, @"\A([45]\.[0-9]{1,3}\.[0-9]{1,3})(?:[ \t\r\n]|\z)",
            RegexOptions.CultureInvariant);
        if (status.Success) fields.Add($"enhancedStatus={status.Groups[1].Value}");
        foreach (var (marker, reason) in KnownServerReasons)
        {
            if (!response.Contains(marker, StringComparison.OrdinalIgnoreCase)) continue;
            fields.Add($"reason={reason}");
            break;
        }
        return true;
    }

    private static string SafeType(Exception error) => error switch
    {
        SmtpProtocolException => "SmtpProtocolException",
        SmtpCommandException => "SmtpCommandException",
        SslHandshakeException => "SslHandshakeException",
        MailKit.Security.AuthenticationException => "AuthenticationException",
        System.Security.Authentication.AuthenticationException => "AuthenticationException",
        SocketException => "SocketException",
        IOException => "IOException",
        TimeoutException => "TimeoutException",
        OperationCanceledException => "OperationCanceledException",
        InvalidOperationException => "InvalidOperationException",
        ArgumentException => "ArgumentException",
        _ => "Exception"
    };
}
