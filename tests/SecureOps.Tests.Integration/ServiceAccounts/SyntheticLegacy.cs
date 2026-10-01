using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// Synthetic legacy tracking workbook and matching migration package (same workbook hash, same sheet rows),
/// used to prove that importing both never duplicates business records. All names are synthetic.
/// </summary>
internal sealed class SyntheticLegacy
{
    public SyntheticLegacy(string suffix, string organization)
    {
        Suffix = suffix;
        Organization = organization;
        Accounts = [.. new[] { "APP", "DB", "WEB", "CRMSVC", "CRMSSVC" }.Select(n => $"SYN{suffix}_{n}")];
        Team = "SYN TEAM " + suffix;
        TargetTeam = "SYN TARGET " + suffix;
        Person = "Sentetik Sorumlu " + suffix;
        Followup = "Sentetik Takipçi " + suffix;
        Workbook = SyntheticWorkbook.Create([
            ("Hesap_Bilgileri", [
                (6, new SynCell?[] { "Servis Hesabı", "Sorumlu Ekip", "Sorumlu Kişi", "Rapor Organizasyonu", "Açıklama", "Hesap ID", "Hesap Anahtarı Giriş" }),
                (7, [Accounts[0], Team, Person, organization, "Sahip ekip teyidi", "SH-0001", new SynCell(Accounts[0], Formula: "UPPER(A7)")]),
                (8, [Accounts[1], Team, null, organization, null, "SH-0002", null]),
                (9, [Accounts[2], "Belirlenecek", null, organization, null, "SH-0003", null]),
                (10, [Accounts[3], Team, null, organization, null, "SH-0004", null]),
                (11, [Accounts[4], "Belirlenecek", null, organization, null, "SH-0005", null])]),
            ("Talep_Takibi", [
                (6, new SynCell?[] { "Servis Hesabı", "Muhatap Ekip", "Takip Sorumlusu", "Beklenen Aksiyon", "Durum", "Plan Başlangıcı", "Plan Bitişi", "Jira / Diğer Kayıt", "Kayıt No" }),
                (7, [Accounts[2], Team, Followup, "Parola değişimi", "Açık", 46295, 46295, "SYN-1734", new SynCell("TP-0001", Formula: "\"TP-\"&ROW()")]),
                (8, [Accounts[2], Team, Followup, "İnceleme", "Açık", null, null, null, new SynCell("TP-0002", Formula: "\"TP-\"&ROW()")]),
                (9, [Accounts[1], TargetTeam, null, "gMSA ile devir", "Açık", null, null, null, new SynCell("TP-0003", Formula: "\"TP-\"&ROW()")])]),
            ("Islem_Gecmisi", [
                (6, new SynCell?[] { "Servis Hesabı", "İşlem Tarihi", "İşlem Türü", "Sonuç", "İşlemi Yapan Ekip", "Kayıt Türü", "Doğrulama Tarihi", "Doğrulayan", "Kayıt No" }),
                (7, [Accounts[0], 46255, "Parola değişimi", "Gerçekleşti", Team, "Ara adım", 46254, Person, new SynCell("IS-0001", Formula: "\"IS-\"&ROW()")]),
                (8, [Accounts[0], null, "Silme", "Planlandı", null, "Hesap kapanışı", null, null, new SynCell("IS-0002", Formula: "\"IS-\"&ROW()")])]),
            ("Mail_Gunlugu", [
                (6, new SynCell?[] { "Servis Hesabı", "Mail Tarihi", "Yön", "Mail Türü", "Muhatap Ekip", "Kısa Açıklama", "Kayıt Kapsamı" }),
                (7, [Accounts[0], 46244, "Giden", "İlk talep", Team, "Sahiplik sorusu", null]),
                (8, [null, 46282, "Gelen", "Yanıt", Team, "Ekip genel dönüşü", "Ekip"]),
                (9, [null, null, "Gelen", "Bilgilendirme", Team, "Tarihsiz ekip maili", "Ekip"])]),
            ("DBA_Devir", [
                (6, new SynCell?[] { "Servis Hesabı", "Kaynak Ekip", "Kullanan Ekip", "Hedef Ekip", "Devir Durumu", "Bildirim Tarihi", "Hedef Aksiyon" }),
                (7, [Accounts[1], Team, " SYN KULLANAN " + suffix, TargetTeam, "Devir kapsamına bildirildi", 46280, "gMSA ile devir"])])]);
        WorkbookSha = Convert.ToHexString(SHA256.HashData(Workbook)).ToLowerInvariant();
        Package = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schemaVersion = "wasas.service-account-migration.v1",
            sourceReportDate = "2026-09-14",
            reportWorkbookSha256 = WorkbookSha,
            personAliases = new[] { new[] { ("SENTETIK SORUMLU " + suffix).ToUpperInvariant(), Person } },
            records = new Dictionary<string, object>
            {
                ["Hesap_Bilgileri"] = new[]
                {
                    Record(7, new() { ["Servis Hesabı"] = Accounts[0], ["Sorumlu Ekip"] = Team, ["Sorumlu Kişi"] = Person, ["Rapor Organizasyonu"] = organization, ["Açıklama"] = "Sahip ekip teyidi", ["Hesap ID"] = "SH-0001" }),
                    Record(8, new() { ["Servis Hesabı"] = Accounts[1], ["Sorumlu Ekip"] = Team, ["Rapor Organizasyonu"] = organization, ["Hesap ID"] = "SH-0002" }),
                    Record(9, new() { ["Servis Hesabı"] = Accounts[2], ["Sorumlu Ekip"] = "Belirlenecek", ["Rapor Organizasyonu"] = organization, ["Hesap ID"] = "SH-0003" }),
                    Record(10, new() { ["Servis Hesabı"] = Accounts[3], ["Sorumlu Ekip"] = Team, ["Rapor Organizasyonu"] = organization, ["Hesap ID"] = "SH-0004" }),
                    Record(11, new() { ["Servis Hesabı"] = Accounts[4], ["Sorumlu Ekip"] = "Belirlenecek", ["Rapor Organizasyonu"] = organization, ["Hesap ID"] = "SH-0005" })
                },
                ["Talep_Takibi"] = new[]
                {
                    Record(7, new() { ["Servis Hesabı"] = Accounts[2], ["Muhatap Ekip"] = Team, ["Takip Sorumlusu"] = Followup, ["Beklenen Aksiyon"] = "Parola değişimi", ["Durum"] = "Açık", ["Plan Başlangıcı"] = "2026-09-30T00:00:00", ["Plan Bitişi"] = "2026-09-30T00:00:00", ["Jira / Diğer Kayıt"] = "SYN-1734", ["Kayıt No"] = "TP-0001" }),
                    Record(8, new() { ["Servis Hesabı"] = Accounts[2], ["Muhatap Ekip"] = Team, ["Takip Sorumlusu"] = Followup, ["Beklenen Aksiyon"] = "İnceleme", ["Durum"] = "Açık", ["Kayıt No"] = "TP-0002" }),
                    Record(9, new() { ["Servis Hesabı"] = Accounts[1], ["Muhatap Ekip"] = TargetTeam, ["Beklenen Aksiyon"] = "gMSA ile devir", ["Durum"] = "Açık", ["Kayıt No"] = "TP-0003" })
                },
                ["Islem_Gecmisi"] = new[]
                {
                    Record(7, new() { ["Servis Hesabı"] = Accounts[0], ["İşlem Tarihi"] = "2026-08-21T00:00:00", ["İşlem Türü"] = "Parola değişimi", ["Sonuç"] = "Gerçekleşti", ["İşlemi Yapan Ekip"] = Team, ["Kayıt Türü"] = "Ara adım", ["Doğrulama Tarihi"] = "2026-08-20T00:00:00", ["Doğrulayan"] = Person, ["Kayıt No"] = "IS-0001" }),
                    Record(8, new() { ["Servis Hesabı"] = Accounts[0], ["İşlem Türü"] = "Silme", ["Sonuç"] = "Planlandı", ["Kayıt Türü"] = "Hesap kapanışı", ["Kayıt No"] = "IS-0002" })
                },
                ["Mail_Gunlugu"] = new[]
                {
                    Record(7, new() { ["Servis Hesabı"] = Accounts[0], ["Mail Tarihi"] = "2026-08-10T00:00:00", ["Yön"] = "Giden", ["Mail Türü"] = "İlk talep", ["Muhatap Ekip"] = Team, ["Kısa Açıklama"] = "Sahiplik sorusu" }),
                    Record(8, new() { ["Mail Tarihi"] = "2026-09-17T00:00:00", ["Yön"] = "Gelen", ["Mail Türü"] = "Yanıt", ["Muhatap Ekip"] = Team, ["Kısa Açıklama"] = "Ekip genel dönüşü", ["Kayıt Kapsamı"] = "Ekip" }, MailKey),
                    Record(9, new() { ["Yön"] = "Gelen", ["Mail Türü"] = "Bilgilendirme", ["Muhatap Ekip"] = Team, ["Kısa Açıklama"] = "Tarihsiz ekip maili", ["Kayıt Kapsamı"] = "Ekip" })
                },
                ["Teknik_Bulgular"] = Array.Empty<object>(),
                ["DBA_Devir"] = new[]
                {
                    Record(7, new() { ["Servis Hesabı"] = Accounts[1], ["Kaynak Ekip"] = Team, ["Kullanan Ekip"] = " SYN KULLANAN " + suffix, ["Hedef Ekip"] = TargetTeam, ["Devir Durumu"] = "Devir kapsamına bildirildi", ["Bildirim Tarihi"] = "2026-09-15T00:00:00", ["Hedef Aksiyon"] = "gMSA ile devir" })
                }
            },
            communicationLinks = new[] { new { communicationMigrationKey = MailKey.ToString("D"), accounts = new[] { Accounts[0], Accounts[1], Accounts[2] } } },
            sourceRows = new
            {
                Book1 = new object?[][]
                {
                    ["Kullanıcı Adı", "Son Parola Değişiklik Zamanı", "AD veya LDAP Son Oturum Açma Zamanı", "AD Son Oturum Açma Zamanı", "Organizasyon", "Grup Direktorlugu", "Yorum"],
                    [Accounts[0], 45723.44, 46279.3, 46279.3, organization, "SYN GRUP", "parola eski"],
                    [Accounts[1], 44720.35, "last logon bilgisi yok", null, organization, "SYN GRUP", "hesap AD'de bulunmuyor"],
                    [Accounts[2], 45000.5, 46000.5, 46000.5, organization, "SYN GRUP", null],
                    [Accounts[3], 45100.5, 46100.5, 46100.5, organization, "SYN GRUP", null]
                },
                DBA = new object?[][]
                {
                    ["Kullanıcı Adı", "Ekip", "Kullanan_Ekip", "WASAS_Devir"],
                    [Accounts[1], Team, "SYN KULLANAN " + suffix, "OK"],
                    [Accounts[2], Team, "SYN KULLANAN " + suffix, null]
                }
            }
        }));
    }

    public string Suffix { get; }

    public string Organization { get; }

    public string[] Accounts { get; }

    public string Team { get; }

    public string TargetTeam { get; }

    public string Person { get; }

    public string Followup { get; }

    public byte[] Workbook { get; }

    public string WorkbookSha { get; }

    public byte[] Package { get; }

    public Guid MailKey { get; } = Guid.NewGuid();

    private static object Record(int row, Dictionary<string, object?> fields, Guid? key = null) =>
        new { legacySourceSheet = "x", legacySourceRow = row, migrationKey = (key ?? Guid.NewGuid()).ToString("D"), fields };
}
