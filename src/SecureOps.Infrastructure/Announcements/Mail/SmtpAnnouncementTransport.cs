using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SecureOps.Domain.Announcements;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Announcements.Mail;

/// <summary>Exactly one SMTP attempt; no retry or delivery assertion.</summary>
public interface IAnnouncementMailTransport
{
    /// <summary>Only a durably claimed command may call this boundary.</summary>
    public Task<AnnouncementTransportOutcome> SubmitAsync(AnnouncementMailExecution execution, CancellationToken token);
}

/// <summary>MailKit owns SMTP/TLS parsing; recipient and DATA acknowledgments remain distinct.</summary>
public sealed class SmtpAnnouncementTransport(IOptions<AnnouncementMailOptions> options, AnnouncementMailPolicy policy) : IAnnouncementMailTransport
{
    /// <inheritdoc />
    public async Task<AnnouncementTransportOutcome> SubmitAsync(AnnouncementMailExecution execution, CancellationToken token)
    {
        AnnouncementMailIntent intent = execution.Command.Intent;
        string[] recipients = [.. intent.To, .. intent.Cc];
        if (policy.Validate(intent.Kind, intent.Sender, recipients) is not null || policy.Fingerprint() != intent.ConfigurationFingerprint
            || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(execution.Message)) != intent.MessageHash
            || policy.Envelope(intent.Sender) != intent.EnvelopeSender
            || (intent.Kind == "SelfTest" && (intent.To.Length != 1 || intent.To[0] != intent.Sender || intent.Cc.Length != 0)))
        { return new("Denied", [], []); }
        AnnouncementMailOptions config = options.Value;
        using var smtp = new RecipientEvidenceClient { Timeout = config.TimeoutSeconds * 1000 };
        bool submitting = false;
        try
        {
            using var bytes = new MemoryStream(execution.Message, false);
            using MimeMessage message = await MimeMessage.LoadAsync(bytes, token);
            if (!message.From.Mailboxes.Select(x => x.Address).SequenceEqual([intent.Sender], StringComparer.Ordinal)
                || !message.To.Mailboxes.Select(x => x.Address).SequenceEqual(intent.To, StringComparer.Ordinal)
                || !message.Cc.Mailboxes.Select(x => x.Address).SequenceEqual(intent.Cc, StringComparer.Ordinal)
                || message.Bcc.Count != 0 || message.MessageId != intent.MessageId || message.Subject != intent.Subject)
            { return new("Denied", [], []); }
            await smtp.ConnectAsync(config.Host, config.Port, config.Security switch
            { "StartTls" => SecureSocketOptions.StartTls, "SslOnConnect" => SecureSocketOptions.SslOnConnect, _ => SecureSocketOptions.None }, token);
            if (!string.IsNullOrEmpty(config.UserName))
            { await smtp.AuthenticateAsync(config.UserName, config.Password!, token); }
            submitting = true;
            await smtp.SendAsync(FormatOptions.Default, message, MailboxAddress.Parse(intent.EnvelopeSender), recipients.Select(MailboxAddress.Parse), token);
            // A failure to QUIT after DATA acceptance cannot turn known acceptance into Unknown.
            try
            { await smtp.DisconnectAsync(true, token); }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or SmtpProtocolException) { }
            return new(smtp.Rejected.Count == 0 ? "Accepted" : "Partial", smtp.Accepted.ToArray(), smtp.Rejected.ToArray());
        }
        catch (SmtpCommandException exception) when (exception.ErrorCode is SmtpErrorCode.MessageNotAccepted or SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.RecipientNotAccepted)
        { return new("Failed", [], recipients); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or SmtpProtocolException or System.Net.Sockets.SocketException
            or AuthenticationException or ServiceNotAuthenticatedException or ServiceNotConnectedException or SslHandshakeException)
        { return new(submitting ? "Unknown" : "Failed", [], smtp.Rejected.ToArray()); }
    }

    private sealed class RecipientEvidenceClient : SmtpClient
    {
        internal List<string> Accepted { get; } = [];
        internal List<string> Rejected { get; } = [];
        protected override void OnRecipientAccepted(MimeMessage message, MailboxAddress mailbox, SmtpResponse response) => Accepted.Add(mailbox.Address);
        protected override void OnRecipientNotAccepted(MimeMessage message, MailboxAddress mailbox, SmtpResponse response) => Rejected.Add(mailbox.Address);
    }
}
