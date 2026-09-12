using SecureOps.Domain.Announcements;

namespace SecureOps.Ui.Services;

/// <summary>Editable plain text and stable recipient rows; source provenance stays server-owned.</summary>
public sealed class AnnouncementForm
{
    /// <summary>Ordered fields, label, input type, and optional grouping.</summary>
    public static readonly (string Key, string Label, string Type, bool Optional)[] Fields =
    [
        ("Subject", "Konu", "text", false), ("OcoReference", "OCO", "text", false),
        ("Scope", "Çalışma kapsamı", "textarea", false), ("AnnouncementDate", "Duyuru tarihi", "date", false),
        ("WorkStart", "Çalışma başlangıcı", "datetime-local", false), ("WorkEnd", "Çalışma bitişi", "datetime-local", false),
        ("Description", "Açıklama", "textarea", false), ("Impact", "Etki", "textarea", false),
        ("Checks", "Kontroller", "textarea", false), ("Notes", "Notlar", "textarea", true),
        ("RestartStart", "Yeniden başlatma başlangıcı", "datetime-local", true), ("RestartEnd", "Yeniden başlatma bitişi", "datetime-local", true)
    ];
    /// <summary>Original field values; no HTML decoding.</summary>
    public Dictionary<string, string> Values { get; } = Fields.ToDictionary(f => f.Key, _ => "");
    /// <summary>Editable recipient rows.</summary>
    public List<Recipient> Recipients { get; } = [];
    /// <summary>Explicit allowlisted choice.</summary>
    public string Banner { get; set; } = "";
    /// <summary>One stable UI row; not an identity.</summary>
    public sealed class Recipient(string kind, string address)
    {
        /// <summary>To or Cc.</summary>
        public string Kind { get; } = kind;
        /// <summary>Operator-entered address.</summary>
        public string Address { get; set; } = address;
    }
    /// <summary>Builds the existing content contract without modifying source strings.</summary>
    public AnnouncementContent Content() => new(Values["OcoReference"], Values["Scope"], Values["Subject"],
        Values["AnnouncementDate"], Values["WorkStart"], Values["WorkEnd"], Values["Description"], Values["Impact"],
        Values["Checks"], Values["Notes"], Addresses("To"), Addresses("Cc"), Banner,
        EmptyToNull(Values["RestartStart"]), EmptyToNull(Values["RestartEnd"]));
    private string[] Addresses(string kind) => [.. Recipients.Where(r => r.Kind == kind && !string.IsNullOrWhiteSpace(r.Address)).Select(r => r.Address)];
    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
    /// <summary>Loads an explicit read/save response; never called after a failed write.</summary>
    public static AnnouncementForm From(AnnouncementContent c)
    {
        AnnouncementForm form = new() { Banner = c.BannerRevision };
        string[] values = [c.Subject, c.OcoReference, c.Scope, c.AnnouncementDate, c.WorkStart, c.WorkEnd,
            c.Description, c.Impact, c.Checks, c.Notes, c.RestartStart ?? "", c.RestartEnd ?? ""];
        for (int i = 0; i < Fields.Length; i++)
        {
            form.Values[Fields[i].Key] = values[i];
        }

        form.Recipients.AddRange(c.To.Select(a => new Recipient("To", a)).Concat(c.Cc.Select(a => new Recipient("Cc", a))));
        return form;
    }
    /// <summary>Only changed fields, including recipient and banner edits.</summary>
    public IEnumerable<(string Label, string Saved, string Local)> Differences(AnnouncementContent current)
    {
        AnnouncementForm saved = From(current);
        foreach ((string Key, string Label, string Type, bool Optional) f in Fields.Where(f => Values[f.Key] != saved.Values[f.Key]))
        {
            yield return (f.Label, saved.Values[f.Key], Values[f.Key]);
        }

        foreach (string kind in new[] { "To", "Cc" })
        {
            if (!Addresses(kind).SequenceEqual(saved.Addresses(kind)))
            {
                yield return (kind == "To" ? "Alıcılar" : "Bilgi", string.Join(", ", saved.Addresses(kind)), string.Join(", ", Addresses(kind)));
            }
        }

        if (Banner != saved.Banner)
        {
            yield return ("Görsel", saved.Banner, Banner);
        }
    }
}
