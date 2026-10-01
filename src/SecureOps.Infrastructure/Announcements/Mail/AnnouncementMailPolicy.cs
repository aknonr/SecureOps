using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Announcements.Mail;

/// <summary>Explicit bounded relay policy. No domain inference or shared-From fallback.</summary>
public sealed class AnnouncementMailPolicy(IOptions<AnnouncementMailOptions> options, string environmentName)
{
    /// <summary>Returns a safe code without private relay configuration.</summary>
    public string? Validate(string kind, string sender, IReadOnlyList<string> recipients)
    {
        AnnouncementMailOptions config = options.Value;
        if (!config.Enabled || (kind == "SelfTest" ? !config.SelfTestEnabled : kind != "Send" || !config.SendEnabled))
        { return "AnnouncementMailDisabled"; }
        if (string.IsNullOrWhiteSpace(config.Host) || config.Host.Length > 253 || Uri.CheckHostName(config.Host) == UriHostNameType.Unknown
            || config.Port is < 1 or > 65535 || config.TimeoutSeconds is < 1 or > 120
            || config.MaxRecipients is < 1 or > 200 || string.IsNullOrWhiteSpace(config.PolicyRevision) || config.PolicyRevision.Length > 100
            || config.EnvelopeMode is not ("Actor" or "Configured")
            || config.AllowedRecipientDomains is not { Length: > 0 and <= 50 }
            || config.AllowedRecipientDomains.Any(domain => domain is null || domain.Length is < 1 or > 253 || Uri.CheckHostName(domain) != UriHostNameType.Dns)
            || (config.EnvelopeMode == "Configured" && !AnnouncementValidation.Address(config.EnvelopeSender))
            || string.IsNullOrEmpty(config.UserName) != string.IsNullOrEmpty(config.Password))
        { return "AnnouncementMailConfigurationInvalid"; }
        if (config.Security is not ("StartTls" or "SslOnConnect") &&
            !(config.Security == "PlaintextLoopback" && environmentName is "Development" or "Demo" or "Test"
                && IPAddress.TryParse(config.Host, out IPAddress? address) && IPAddress.IsLoopback(address) && config.UserName is null && config.Password is null))
        { return "AnnouncementMailConfigurationInvalid"; }
        if (!AnnouncementValidation.Address(sender) || recipients.Count is < 1 || recipients.Count > config.MaxRecipients
            || recipients.Any(recipient => !AnnouncementValidation.Address(recipient) ||
                !config.AllowedRecipientDomains.Contains(recipient[(recipient.LastIndexOf('@') + 1)..], StringComparer.OrdinalIgnoreCase)))
        { return "AnnouncementMailAudienceInvalid"; }
        return null;
    }

    /// <summary>Nonsecret configuration identity binds preview to destinations and security policy.</summary>
    public string Fingerprint()
    {
        AnnouncementMailOptions config = options.Value;
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            config.Enabled,
            config.SelfTestEnabled,
            config.SendEnabled,
            config.Host,
            config.Port,
            config.Security,
            config.PolicyRevision,
            config.EnvelopeMode,
            config.EnvelopeSender,
            config.MaxRecipients,
            Domains = config.AllowedRecipientDomains.Order(StringComparer.OrdinalIgnoreCase),
            AuthenticationConfigured = !string.IsNullOrEmpty(config.UserName)
        })));
    }

    /// <summary>Envelope is independent of the MIME From header.</summary>
    public string Envelope(string sender) => options.Value.EnvelopeMode == "Actor" ? sender : options.Value.EnvelopeSender;
}
