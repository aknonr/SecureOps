using Microsoft.AspNetCore.DataProtection;
using SecureOps.Infrastructure.Announcements.Mail;

namespace SecureOps.Api.Security;

/// <summary>Mail preview protection shares the API's persistent ring, not its purpose or UI ring.</summary>
public sealed class AnnouncementMailPreviewCodec(IDataProtectionProvider provider) : IAnnouncementMailPreviewCodec
{
    private readonly IDataProtector _protector = provider.CreateProtector("SecureOps.AnnouncementMail.Preview.v1");
    /// <inheritdoc />
    public string Protect(string value) => _protector.Protect(value);
    /// <inheritdoc />
    public string Unprotect(string value) => _protector.Unprotect(value);
}
