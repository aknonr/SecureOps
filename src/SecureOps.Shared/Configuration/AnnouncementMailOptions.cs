namespace SecureOps.Shared.Configuration;

/// <summary>Opt-in relay configuration; credentials remain process/server-owned and never enter commands.</summary>
public sealed class AnnouncementMailOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "AnnouncementMail";
    /// <summary>Master dispatch fence, disabled by default.</summary>
    public bool Enabled { get; set; }
    /// <summary>Separately enables actor-only self-test.</summary>
    public bool SelfTestEnabled { get; set; }
    /// <summary>Separately enables distribution mail.</summary>
    public bool SendEnabled { get; set; }
    /// <summary>Approved relay DNS name or test loopback address.</summary>
    public string Host { get; set; } = "";
    /// <summary>Relay port.</summary>
    public int Port { get; set; } = 587;
    /// <summary>StartTls or SslOnConnect; PlaintextLoopback is isolated-test-only.</summary>
    public string Security { get; set; } = "StartTls";
    /// <summary>Reviewed nonsecret relay-policy revision; changing it invalidates outstanding previews.</summary>
    public string PolicyRevision { get; set; } = "";
    /// <summary>Actor or Configured. Empty fails closed until the relay envelope policy is known.</summary>
    public string EnvelopeMode { get; set; } = "";
    /// <summary>Approved relay envelope address only for Configured mode; displayed From still comes from actor Mail.</summary>
    public string EnvelopeSender { get; set; } = "";
    /// <summary>Allowed destination domains; no wildcard or suffix matching.</summary>
    public string[] AllowedRecipientDomains { get; set; } = [];
    /// <summary>Maximum combined distinct To/Cc addresses.</summary>
    public int MaxRecipients { get; set; } = 100;
    /// <summary>Bounded network timeout.</summary>
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>Optional private relay authentication identity, distinct from displayed From.</summary>
    public string? UserName { get; set; }
    /// <summary>Optional private relay password. Never log or persist in command evidence.</summary>
    public string? Password { get; set; }
}
