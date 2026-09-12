using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Managed text-only XLSX writer: no formulas, macros, links, COM or remote calls.</summary>
public static class InUseWorkbook
{
    private const string _spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string _relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string _officeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly string[] _serverHeaders = ["ALAN ADI", "ENVANTER_ID", "HOSTNAME", "SERVER_TYPE", "SI_ENVIRONMENT",
        "CONSUMER_COMPANY", "SERVICE OWNER DIRECTORATE", "SERVICE NAME (ÜRÜN/UYGULAMA)", "SERVICE ASPECT (Servis Unsuru)",
        "NETWORK SEGMENT", "IP ADDRESS", "OS NAME", "OS_VERSION", "OS RELEASE", "Sunucudan İnternete Erişim Var mı ?",
        "İnternete Sunucuya Erişim Var mı ?", "Microsegmented", "NMS'e Dahil Edilmek İsteniyor Mu ?(Prod Sunucular İçin Zorunludur)",
        "COUNTRY", "CITY", "BUILDING", "Department", "Sub_Department", "Contact_email", "UY_Owner Mail Address",
        "ITMC_MEMORY_Alarm", "ITMC_CPU_Alarm", "ITMC_UP_DOWN_Alarm", "ITMC_Disk_Alarm"];
    private static readonly string[] _nmsHeaders = ["KONTROL", "IP Address", "ENV_ID", "ITMC_Service_ID", "ITMC_Turuncu_Sunucu_Listesi_Karsilik",
        "ITMC_Servis_Unsuru_ID", "ITMC_Servis_Unsuru", "Hardware_Type", "Device_Type", "Server_Type", "Country", "City", "Building",
        "Department", "Sub_Department", "Contact_email", "UY_Owner Mail Address", "ITMC_Event_Owner_Group", "ITMC_MEMORY_Alarm",
        "ITMC_CPU_Alarm", "ITMC_UP_DOWN_Alarm", "ITMC_Disk_Alarm"];
    private static readonly string[] _nmsFields = ["check:Verified", "IP ADDRESS", "ENVANTER_ID", "ITMC_Service_ID",
        "SERVICE NAME (ÜRÜN/UYGULAMA)", "ITMC_Servis_Unsuru_ID", "ITMC_Servis_Unsuru", "SERVER TYPE", "Device_Type", "SI_ENVIRONMENT",
        "COUNTRY", "CITY", "BUILDING", "Department", "Sub_Department", "Contact_email", "UY_Owner Mail Address", "ITMC_Event_Owner_Group",
        "check:MemoryAlarm", "check:CpuAlarm", "check:UpDownAlarm", "check:DiskAlarm"];

    /// <summary>Creates exact worksheet previews and their version-bound binary representation.</summary>
    public static InUseReport Create(InUseRecord record, Guid actor, DateTimeOffset now)
    {
        if (record.Draft is null || record.Draft.SourceVersion != record.SourceVersion)
        { throw new InvalidOperationException("Only current saved drafts may be exported."); }
        List<IReadOnlyList<string>> serverRows = [];
        for (int row = 0; row < _serverHeaders.Length; row++)
        {
            int index = row;
            serverRows.Add(new[] { _serverHeaders[row] }.Concat(record.Source.Servers.Select((server, column) => index == 0
                ? $"Sunucu {column + 1} Bilgileri" : Value(record, server, ServerField(index)))).ToArray());
        }
        List<IReadOnlyList<string>> nmsRows = [_nmsHeaders];
        nmsRows.AddRange(record.Source.Servers.Select(server => _nmsFields.Select(field => Value(record, server, field)).ToArray()));
        List<IReadOnlyList<string>> provenance =
        [
            new[] { "Report", "LOCAL DRAFT - NOT UPLOADED - NOT SOURCE APPROVAL" },
            new[] { "Template", "InUseV1-text-only; technical sheets intentionally blank in supplied script" },
            new[] { "RecordId", record.Id.ToString("D") }, new[] { "SourceId", record.Source.Id }, new[] { "Code", record.Source.Code },
            new[] { "Version", record.Version.ToString(CultureInfo.InvariantCulture) },
            new[] { "SourceVersion", record.SourceVersion.ToString(CultureInfo.InvariantCulture) }, new[] { "SourceHash", record.SourceHash },
            new[] { "LastSeenAt", record.LastSeenAt.ToString("O") }, new[] { "ReviewedBy", record.Draft.ReviewedBy.ToString("D") },
            new[] { "ReviewedAt", record.Draft.ReviewedAt.ToString("O") }, new[] { "PreparedBy", actor.ToString("D") },
            new[] { "PreparedAt", now.ToString("O") }, new[] { "Synthetic", record.Source.Synthetic.ToString() },
            new[] { "Relationships", record.Source.RelationshipEvidence }, new[] { "Notes", record.Draft.Notes },
            new[] { "RetainedEvidence", "Historical notes/checks are preserved; they do not verify changed answers or completed monitoring." },
            new[] { "ServiceItemsState", record.Source.ServiceItemsState },
            new[] { "AffectedAssetsState", record.Source.AffectedAssetsState, record.Source.AffectedAssetCount?.ToString(CultureInfo.InvariantCulture) ?? "Unknown" },
            new[] { "TechnicalCreator", record.Source.Creator?.Value ?? "Unknown", record.Source.Creator?.Source ?? "Unverified" },
            new[] { "Requester", InUseDisplayText.Decode(record.Source.Requester.Value), record.Source.Requester.Source },
            new[] { "ServiceOwner", record.Source.ServiceOwner.Value ?? "Unknown", record.Source.ServiceOwner.Source },
            new[] { "ProvisioningTeam", record.Source.ProvisioningTeam.Value ?? "Unknown", record.Source.ProvisioningTeam.Source },
            new[] { "LocalAssignee", record.AssigneeId?.ToString("D") ?? "Unassigned", "Authorized manual application assignment" }
        ];
        List<IReadOnlyList<string>> evidenceRows = [new[] { "Server", "Field or check", "Value", "Evidence" }];
        foreach (InUseServer server in record.Source.Servers)
        {
            if (server.RelatedRequestReporter is { } reporter)
            {
                evidenceRows.AddRange(new[] {
                    new[] { server.Id, InUseRelatedRequestReporter.Label, InUseDisplayText.Decode(reporter.Display), reporter.StateText(now) },
                    new[] { server.Id, "RFC reference", reporter.RfcReference ?? "Unknown", $"{reporter.RequestCode ?? "Unknown"} / {reporter.RequestId ?? "Unknown"}" },
                    new[] { server.Id, "Related-request user reference", reporter.UserReference ?? "Unknown", "SET.p_rel_requester" },
                    new[] { server.Id, "Reporter last verified", reporter.LastVerifiedAt?.ToString("O") ?? "Unknown", "Historical observation; not business ownership" }
                });
            }
            evidenceRows.AddRange(server.Fields.OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => new[] { server.Id, f.Key, InUseDisplayText.Field(f.Key, f.Value.Value), f.Value.Source }));
            evidenceRows.AddRange(record.Draft.Answers.Where(a => a.ServerId == server.Id)
                .Select(a => new[] { a.ServerId, a.Check, a.Value, a.Evidence }));
        }
        InUseSheet[] sheets = [new("Sunucular", serverRows), new("CheckList_TEKNIK", []), new("CheckList_THY", []),
            new("NMS", nmsRows), new("Provenance", provenance), new("ReviewEvidence", evidenceRows)];
        sheets = sheets.Select(s => new InUseSheet(s.Name, s.Rows.Select(r => (IReadOnlyList<string>)r.Select(Safe).ToArray()).ToArray())).ToArray();
        byte[] bytes = Write(sheets);
        return new(record.Id, record.Version, record.SourceVersion, Convert.ToHexString(SHA256.HashData(bytes)),
            $"InUse-{record.Id:D}-v{record.Version}.xlsx", bytes, sheets)
        { PreparedBy = actor, PreparedAt = now, SourceId = record.Source.Id, Size = bytes.LongLength };
    }

    private static string ServerField(int index) => index switch
    {
        3 => "SERVER TYPE",
        14 => "check:InternetOut",
        15 => "check:InternetIn",
        16 => "check:Microsegmented",
        17 => "check:NmsRequested",
        25 => "check:MemoryAlarm",
        26 => "check:CpuAlarm",
        27 => "check:UpDownAlarm",
        28 => "check:DiskAlarm",
        _ => _serverHeaders[index]
    };
    private static string Value(InUseRecord record, InUseServer server, string field)
    {
        if (!field.StartsWith("check:", StringComparison.Ordinal))
        { return server.Fields.TryGetValue(field, out InUseEvidence? value) ? InUseDisplayText.Field(field, value.Value) : "Unknown"; }
        if (!InUseChecks.OperatorCodes.Contains(field[6..]))
        { return "Unknown / not verified"; }
        string? answer = record.Draft!.Answers.SingleOrDefault(a => a.ServerId == server.Id && a.Check == field[6..])?.Value;
        return answer switch { "Yes" => "Evet", "No" => "Hayır", "NotApplicable" => "Not applicable", _ => "Unknown / not verified" };
    }
    private static string Safe(string value)
    {
        string text = new(value.Where(XmlConvert.IsXmlChar).ToArray());
        return text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+') || text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@')
            ? "'" + text : text;
    }
    private static byte[] Write(IReadOnlyList<InUseSheet> sheets)
    {
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml", writer =>
            {
                const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
                writer.WriteStartElement("Types", ns);
                Element(writer, "Default", ns, ("Extension", "rels"), ("ContentType", "application/vnd.openxmlformats-package.relationships+xml"));
                Element(writer, "Default", ns, ("Extension", "xml"), ("ContentType", "application/xml"));
                Element(writer, "Override", ns, ("PartName", "/xl/workbook.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"));
                for (int i = 1; i <= sheets.Count; i++)
                { Element(writer, "Override", ns, ("PartName", $"/xl/worksheets/sheet{i}.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")); }
                writer.WriteEndElement();
            });
            Entry(zip, "_rels/.rels", writer =>
            {
                writer.WriteStartElement("Relationships", _relationships);
                Element(writer, "Relationship", _relationships, ("Id", "rId1"), ("Type", _officeRelationships + "/officeDocument"), ("Target", "xl/workbook.xml"));
                writer.WriteEndElement();
            });
            Entry(zip, "xl/workbook.xml", writer =>
            {
                writer.WriteStartElement("workbook", _spreadsheet);
                writer.WriteStartElement("sheets", _spreadsheet);
                for (int i = 0; i < sheets.Count; i++)
                {
                    writer.WriteStartElement("sheet", _spreadsheet);
                    writer.WriteAttributeString("name", sheets[i].Name);
                    writer.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("r", "id", _officeRelationships, $"rId{i + 1}");
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndElement();
            });
            Entry(zip, "xl/_rels/workbook.xml.rels", writer =>
            {
                writer.WriteStartElement("Relationships", _relationships);
                for (int i = 1; i <= sheets.Count; i++)
                { Element(writer, "Relationship", _relationships, ("Id", $"rId{i}"), ("Type", _officeRelationships + "/worksheet"), ("Target", $"worksheets/sheet{i}.xml")); }
                writer.WriteEndElement();
            });
            for (int i = 0; i < sheets.Count; i++)
            {
                InUseSheet sheet = sheets[i];
                Entry(zip, $"xl/worksheets/sheet{i + 1}.xml", writer =>
                {
                    writer.WriteStartElement("worksheet", _spreadsheet);
                    writer.WriteStartElement("sheetData", _spreadsheet);
                    foreach (IReadOnlyList<string> row in sheet.Rows)
                    {
                        writer.WriteStartElement("row", _spreadsheet);
                        foreach (string value in row)
                        {
                            writer.WriteStartElement("c", _spreadsheet);
                            writer.WriteAttributeString("t", "inlineStr");
                            writer.WriteStartElement("is", _spreadsheet);
                            writer.WriteStartElement("t", _spreadsheet);
                            writer.WriteAttributeString("xml", "space", null, "preserve");
                            writer.WriteString(value);
                            writer.WriteEndElement();
                            writer.WriteEndElement();
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                });
            }
        }
        return output.ToArray();
    }
    private static void Entry(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        using Stream stream = zip.CreateEntry(name).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        write(writer);
    }
    private static void Element(XmlWriter writer, string name, string ns, params (string Key, string Value)[] attributes)
    {
        writer.WriteStartElement(name, ns);
        foreach ((string Key, string Value) attribute in attributes)
        { writer.WriteAttributeString(attribute.Key, attribute.Value); }
        writer.WriteEndElement();
    }
}
