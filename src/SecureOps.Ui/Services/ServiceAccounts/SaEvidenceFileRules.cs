namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// The file hints shared by every evidence upload form (account evidence and plan evidence): allowed types and the size bound.
/// They only spare the person a round trip; the server checks type, signature and size again and decides.
/// </summary>
public static class SaEvidenceFileRules
{
    /// <summary>Largest file the browser reads into memory.</summary>
    public const long MaxBytes = 10L * 1024 * 1024;

    /// <summary>Value for the file input's accept attribute.</summary>
    public const string Accept = ".pdf,.png,.jpg,.jpeg,.txt,.eml,.xlsx,.docx";

    /// <summary>The hint shown beside the file input.</summary>
    public const string Hint = "Dosya (PDF, PNG, JPEG, TXT, EML, XLSX, DOCX; en çok 10 MB)";

    private static readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".txt"] = "text/plain",
        [".eml"] = "message/rfc822",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    /// <summary>The content type for a file name, or false when the extension is not accepted.</summary>
    public static bool TryGetContentType(string fileName, out string contentType)
    {
        bool found = _types.TryGetValue(Path.GetExtension(fileName), out string? type);
        contentType = type ?? string.Empty;
        return found;
    }

    /// <summary>Why the browser refuses a file before sending it, or null when it may be sent.</summary>
    public static string? Refusal(string fileName, long size) =>
        !TryGetContentType(fileName, out _) ? "Bu dosya türü kabul edilmiyor."
        : size is 0 or > MaxBytes ? "Dosya boş veya 10 MB sınırını aşıyor."
        : null;
}
