using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MeetingApp.Api.Options;
using MeetingApp.Api.Services;
using MeetingApp.Model.Entities;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MeetingApp.Api.Tests;

public class SmtpEmailSenderTests
{
    [Theory]
    [InlineData(SmtpDeliveryStage.Connect)]
    [InlineData(SmtpDeliveryStage.Authenticate)]
    [InlineData(SmtpDeliveryStage.Send)]
    public async Task A_real_SMTP_disconnect_reports_the_stage_that_failed(SmtpDeliveryStage expectedStage)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = DisconnectAtStage(listener, expectedStage, deadline.Token);
        var options = CreateOptions(port);
        if (expectedStage == SmtpDeliveryStage.Authenticate)
        {
            // Deliberately fake credentials sent only to the loopback fixture.
            options.Username = "fixture@example.test";
            options.Password = "FixtureOnlyPassword";
        }
        var sender = new SmtpEmailSender(Microsoft.Extensions.Options.Options.Create(options));

        try
        {
            var failure = await Assert.ThrowsAsync<SmtpDeliveryException>(() =>
                sender.SendAsync(CreateMessage(), deadline.Token));

            Assert.Equal(expectedStage, failure.Stage);
            Assert.IsType<SmtpProtocolException>(failure.InnerException);
            var summary = SmtpDeliveryException.GetSafeSummary(failure);
            Assert.Contains($"stage={expectedStage}", summary);
            Assert.Contains("type=SmtpProtocolException", summary);
            Assert.Contains("category=UnexpectedDisconnect", summary);
            await server;
        }
        finally
        {
            await deadline.CancelAsync();
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)
                .ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    [Fact]
    public async Task A_cancelled_send_preserves_cancellation_instead_of_wrapping_it()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var sender = new SmtpEmailSender(Microsoft.Extensions.Options.Options.Create(CreateOptions(1)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sender.SendAsync(CreateMessage(), cancellation.Token));
    }

    [Fact]
    public void Safe_summary_exposes_codes_but_not_root_or_inner_messages_or_Data()
    {
        var socket = new SocketException((int)SocketError.ConnectionReset);
        var inner = new IOException("AUTH inner-secret recipient@example.test body-canary", socket);
        var protocol = new SmtpProtocolException("password-canary bearer-token-canary subject-canary", inner);
        protocol.Data["credentials"] = "data-secret";
        var failure = new SmtpDeliveryException(SmtpDeliveryStage.Connect, protocol);

        var summary = SmtpDeliveryException.GetSafeSummary(failure);

        Assert.Contains("stage=Connect", summary);
        Assert.Contains("type=SmtpProtocolException", summary);
        Assert.Contains("inner=IOException>SocketException", summary);
        Assert.Contains("socket=ConnectionReset", summary);
        Assert.Contains($"native={socket.NativeErrorCode}", summary);
        foreach (var secret in new[] { "AUTH", "inner-secret", "recipient@example.test", "body-canary",
            "password-canary", "bearer-token-canary", "subject-canary", "data-secret" })
            Assert.DoesNotContain(secret, summary);
    }

    [Fact]
    public void Command_error_reports_status_and_error_code_without_the_mailbox_or_server_text()
    {
        var command = new SmtpCommandException(SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable,
            new MailboxAddress("Private name", "private@example.test"), "server-secret AUTH body-canary");
        var failure = new SmtpDeliveryException(SmtpDeliveryStage.Send, command);

        var summary = SmtpDeliveryException.GetSafeSummary(failure);

        Assert.Contains("stage=Send", summary);
        Assert.Contains("type=SmtpCommandException", summary);
        Assert.Contains("smtpStatus=550", summary);
        Assert.Contains("smtpError=SenderNotAccepted", summary);
        Assert.DoesNotContain("Private", summary);
        Assert.DoesNotContain("private@example.test", summary);
        Assert.DoesNotContain("server-secret", summary);
        Assert.DoesNotContain("AUTH", summary);
        Assert.DoesNotContain("body-canary", summary);
    }

    [Fact]
    public void The_exact_disconnect_message_is_classified_without_an_inner_exception()
    {
        var disconnect = new SmtpDeliveryException(SmtpDeliveryStage.Connect,
            new SmtpProtocolException("The SMTP server has unexpectedly disconnected."));
        var other = new SmtpDeliveryException(SmtpDeliveryStage.Connect,
            new SmtpProtocolException("unexpectedly disconnected with secret-server-text"));

        Assert.Contains("category=UnexpectedDisconnect", SmtpDeliveryException.GetSafeSummary(disconnect));
        Assert.DoesNotContain("category=", SmtpDeliveryException.GetSafeSummary(other));
        Assert.DoesNotContain("secret-server-text", SmtpDeliveryException.GetSafeSummary(other));
    }

    [Theory]
    [InlineData("BadCredentials", "BadCredentials")]
    [InlineData("InvalidSecondFactor", "InvalidSecondFactor")]
    [InlineData("Application-specific password required", "ApplicationSpecificPasswordRequired")]
    [InlineData("Too many login attempts", "TooManyLoginAttempts")]
    [InlineData("WebLoginRequired", "WebLoginRequired")]
    [InlineData("AccountDisabled", "AccountDisabled")]
    public void Disconnect_with_a_server_response_exposes_only_numeric_status_and_known_reason(
        string marker, string expectedReason)
    {
        var protocol = new SmtpProtocolException(
            "The SMTP server has unexpectedly disconnected: 5.7.8 " + marker +
            " private@example.test AUTH password-canary body-canary https://private.invalid/secret");
        var failure = new SmtpDeliveryException(SmtpDeliveryStage.Authenticate, protocol);

        var summary = SmtpDeliveryException.GetSafeSummary(failure);

        Assert.Equal("stage=Authenticate;type=SmtpProtocolException;category=UnexpectedDisconnect;" +
            $"enhancedStatus=5.7.8;reason={expectedReason}", summary);
        foreach (var secret in new[] { "private@example.test", "AUTH", "password-canary", "body-canary",
            "https://private.invalid/secret" })
            Assert.DoesNotContain(secret, summary);
    }

    [Theory]
    [InlineData("4.7.0 temporary failure", "4.7.0")]
    [InlineData("5.123.123", "5.123.123")]
    [InlineData("5.7.8\r\nprivate-canary", "5.7.8")]
    [InlineData("2.7.0 private-canary", null)]
    [InlineData("5.7.1234 private-canary", null)]
    [InlineData("5.1234.1 private-canary", null)]
    [InlineData("5.7.8private-canary", null)]
    [InlineData("prefix 5.7.8 private-canary", null)]
    [InlineData(" 5.7.8 private-canary", null)]
    public void Enhanced_status_requires_an_anchored_bounded_numeric_code(string response, string? expectedCode)
    {
        var failure = new SmtpDeliveryException(SmtpDeliveryStage.Authenticate, new SmtpProtocolException(
            "The SMTP server has unexpectedly disconnected: " + response));

        var summary = SmtpDeliveryException.GetSafeSummary(failure);

        Assert.Contains("category=UnexpectedDisconnect", summary);
        if (expectedCode is null) Assert.DoesNotContain("enhancedStatus=", summary);
        else Assert.Contains($"enhancedStatus={expectedCode}", summary);
        Assert.DoesNotContain("private-canary", summary);
    }

    [Theory]
    [InlineData("prefix The SMTP server has unexpectedly disconnected: 5.7.8 BadCredentials")]
    [InlineData("The SMTP server has unexpectedly disconnectedness: 5.7.8 BadCredentials")]
    [InlineData("The SMTP server has unexpectedly disconnected:5.7.8 BadCredentials")]
    [InlineData("The SMTP server has unexpectedly disconnected. 5.7.8 BadCredentials")]
    [InlineData("the SMTP server has unexpectedly disconnected: 5.7.8 BadCredentials")]
    public void Similar_disconnect_prefixes_are_not_classified(string message)
    {
        var failure = new SmtpDeliveryException(SmtpDeliveryStage.Authenticate, new SmtpProtocolException(message));

        Assert.Equal("stage=Authenticate;type=SmtpProtocolException", SmtpDeliveryException.GetSafeSummary(failure));
    }

    [Theory]
    [InlineData("Unable to parse status code returned by the server.", "UnparseableStatusCode")]
    [InlineData("Invalid status code returned by the server.", "InvalidStatusCode")]
    [InlineData("The status codes returned by the server did not match.", "MismatchedStatusCodes")]
    public void Only_exact_known_parser_errors_receive_constant_categories(string message, string expectedCategory)
    {
        var known = new SmtpDeliveryException(SmtpDeliveryStage.Authenticate, new SmtpProtocolException(message));
        var extended = new SmtpDeliveryException(SmtpDeliveryStage.Authenticate,
            new SmtpProtocolException(message + " secret-canary 5.7.8 BadCredentials"));

        Assert.Equal($"stage=Authenticate;type=SmtpProtocolException;category={expectedCategory}",
            SmtpDeliveryException.GetSafeSummary(known));
        Assert.Equal("stage=Authenticate;type=SmtpProtocolException", SmtpDeliveryException.GetSafeSummary(extended));
    }

    [Fact]
    public void Deep_error_chains_are_bounded_and_unstaged_errors_remain_safe()
    {
        Exception error = new IOException("secret-canary");
        for (var depth = 0; depth < 100; depth++)
            error = new InvalidOperationException("secret-canary", error);

        var summary = SmtpDeliveryException.GetSafeSummary(error);

        Assert.Contains("stage=Unknown", summary);
        Assert.InRange(summary.Length, 1, 500);
        Assert.DoesNotContain("secret-canary", summary);
    }

    private static SmtpOptions CreateOptions(int port) => new()
    {
        Host = "127.0.0.1", Port = port, Security = SecureSocketOptions.None,
        FromAddress = "sender@example.test", FromName = "Fixture"
    };

    private static EmailOutboxMessage CreateMessage() => new()
    {
        RecipientEmail = "recipient@example.test", RecipientName = "Fixture",
        Subject = "Local fixture", TextBody = "Public dummy body."
    };

    private static async Task DisconnectAtStage(TcpListener listener, SmtpDeliveryStage stage, CancellationToken cancellationToken)
    {
        using var peer = await listener.AcceptTcpClientAsync(cancellationToken);
        if (stage == SmtpDeliveryStage.Connect) return;
        await using var stream = peer.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n", AutoFlush = true
        };
        await writer.WriteLineAsync("220 localhost fixture".AsMemory(), cancellationToken);
        var hello = await reader.ReadLineAsync(cancellationToken);
        Assert.StartsWith("EHLO ", hello);
        if (stage == SmtpDeliveryStage.Authenticate)
        {
            await writer.WriteLineAsync("250-localhost".AsMemory(), cancellationToken);
            await writer.WriteLineAsync("250 AUTH PLAIN".AsMemory(), cancellationToken);
            var authentication = await reader.ReadLineAsync(cancellationToken);
            Assert.StartsWith("AUTH PLAIN", authentication);
        }
        else
        {
            await writer.WriteLineAsync("250 localhost".AsMemory(), cancellationToken);
            var envelope = await reader.ReadLineAsync(cancellationToken);
            Assert.StartsWith("MAIL FROM:", envelope);
        }
        // Closing without a reply exercises MailKit's actual protocol exception.
    }
}
