using System.Text;
using FluentAssertions;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ServiceAccountImportParserTests
{
    private static readonly SpreadsheetLimits _limits = new();
    private static readonly string[] _book1 = ["Kullanıcı Adı", "Son Parola Değişiklik Zamanı", "AD veya LDAP Son Oturum Açma Zamanı",
        "AD Son Oturum Açma Zamanı", "Organizasyon", "Grup Direktorlugu", "Yorum"];

    [Fact]
    public void CoordinationList_HeaderOrderChanges_DoNotChangeMeaning()
    {
        byte[] ordered = Book1([.. _book1], ["SYN_A", 45723.44084490741, "46279", "hesap bilgisi yok", "SYN ORG", "SYN GRUP", "not"]);
        int[] order = [6, 4, 0, 3, 1, 5, 2];
        byte[] reordered = Book1([.. order.Select(i => _book1[i])],
            [.. order.Select(i => new SynCell?[] { "SYN_A", 45723.44084490741, "46279", "hesap bilgisi yok", "SYN ORG", "SYN GRUP", "not" }[i])]);
        StagedRow a = Parse(ServiceAccountImportProfiles.CoordinationList, ordered).Rows.Single();
        StagedRow b = Parse(ServiceAccountImportProfiles.CoordinationList, reordered).Rows.Single();
        b.Fields.Should().BeEquivalentTo(a.Fields);
        a[StagedFields.PasswordLastSet].Should().Be("2025-03-07T10:34:49.000", "Excel 1900 serials are read without a time zone");
        a[StagedFields.LastLogonAdOrLdap].Should().Be("2026-09-14T00:00:00.000");
        a[StagedFields.LastLogonAd].Should().BeNull("free text in a date column is never turned into a date");
        a["LastLogonAdText"].Should().Be("hesap bilgisi yok");
        a.Errors.Should().BeEmpty();
        a.Warnings.Should().Contain("ObservationTextInDateColumn:LastLogonAd");
    }

    [Fact]
    public void CoordinationList_HeaderIsNeverAnAccount_DuplicatesAndBlankNamesAreFlagged()
    {
        byte[] file = SyntheticWorkbook.Create([("Sheet2", [
            (1, _book1.Select(h => (SynCell?)h).ToArray()),
            (2, ["SYN_A", null, null, null, "O", "G", "x"]),
            (3, ["syn_a ", null, null, null, "O", "G", "dup"]),
            (4, [null, null, null, null, "O", "G", "no name"])])]);
        StagedFile staged = Parse(ServiceAccountImportProfiles.CoordinationList, file);
        staged.Rows.Should().HaveCount(3);
        staged.Rows.Should().NotContain(r => r[StagedFields.Account] == "Kullanıcı Adı");
        staged.Rows[1].Errors.Should().Contain("DuplicateInFile");
        staged.Rows[2].Errors.Should().Contain("AccountNameMissing");
    }

    [Fact]
    public void MissingRequiredHeader_IsRejected()
    {
        byte[] file = SyntheticWorkbook.Create([("Sheet1", [(1, new SynCell?[] { "Başka", "Sütun" }), (2, ["x", "y"])])]);
        Action parse = () => Parse(ServiceAccountImportProfiles.CoordinationList, file);
        parse.Should().Throw<ImportFileException>().Which.Code.Should().Be("RequiredHeaderMissing");
    }

    [Fact]
    public void MacroPackage_IsRejected()
    {
        byte[] file = SyntheticWorkbook.Create([("Sheet1", [(1, _book1.Select(h => (SynCell?)h).ToArray())])], macro: true);
        Action parse = () => Parse(ServiceAccountImportProfiles.CoordinationList, file);
        parse.Should().Throw<ImportFileException>().Which.Code.Should().Be("MacroContentRejected");
    }

    [Fact]
    public void HighlyCompressedEntry_IsRejectedBeforeXmlIsRead()
    {
        byte[] file = SyntheticWorkbook.Create([("Sheet1", [(1, _book1.Select(h => (SynCell?)h).ToArray())])], padEntry: "xl/media/pad.bin", padBytes: 4_000_000);
        Action parse = () => Parse(ServiceAccountImportProfiles.CoordinationList, file);
        parse.Should().Throw<ImportFileException>().Which.Code.Should().Be("CompressionRatioLimit");
    }

    [Fact]
    public void NonSpreadsheetBytesWithZipSignature_AreRejected()
    {
        byte[] file = [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6];
        Action parse = () => Parse(ServiceAccountImportProfiles.CoordinationList, file);
        parse.Should().Throw<ImportFileException>().Which.Code.Should().Be("NotAnXlsxFile");
    }

    [Fact]
    public void Csv_Utf8_QuotedAndSemicolon_IsParsedAsText()
    {
        string csv = "﻿Kullanıcı Adı;Yorum;Organizasyon\r\n\"SYN;QUOTED\";\"çok \"\"satırlı\"\" not\";ORG\r\nSYN_B;=CMD();ORG\r\n";
        StagedFile staged = Parse(ServiceAccountImportProfiles.CoordinationList, Encoding.UTF8.GetBytes(csv));
        staged.Rows.Should().HaveCount(2);
        staged.Rows[0][StagedFields.Account].Should().Be("SYN;QUOTED");
        staged.Rows[0][StagedFields.Comment].Should().Be("çok \"satırlı\" not");
        staged.Rows[1][StagedFields.Comment].Should().Be("=CMD()", "imported text is stored verbatim and neutralized only on export");
    }

    [Fact]
    public void LegacyWorkbook_FormulaHelperColumnsAreIgnored_AndOnlyInputSheetsAreStaged()
    {
        byte[] file = SyntheticWorkbook.Create([
            ("Hesap_Bilgileri", [
                (6, new SynCell?[] { "Servis Hesabı", "Sorumlu Ekip", "Sorumlu Kişi", "Rapor Organizasyonu", "Hesap Anahtarı Giriş" }),
                (7, ["SYN_LEGACY", "SYN TEAM", "Örnek Kişi", "SYN ORG", new SynCell("SYN_LEGACY", Formula: "UPPER(A7)")]),
                (8, [null, null, null, null, new SynCell("", Formula: "UPPER(A8)")])]),
            ("Talep_Takibi", [
                (6, new SynCell?[] { "Servis Hesabı", "Beklenen Aksiyon", "Durum", "Kayıt No" }),
                (7, ["SYN_LEGACY", "Silme", "Açık", new SynCell("TP-0001", Formula: "\"TP-\"&ROW()")]),
                (8, [null, null, "Açık", new SynCell("TP-0002", Formula: "\"TP-\"&ROW()")])]),
            ("Kaynak_Orijinal", [(6, _book1.Select(h => (SynCell?)h).ToArray()), (7, ["SYN_LEGACY", 45000, 45000, 45000, "O", "G", "x"])])]);
        StagedFile staged = ImportParser.Parse(ServiceAccountImportProfiles.LegacyWorkbook, file,
            new StageImportRequest(ServiceAccountImportProfiles.LegacyWorkbook, null, "test"), new string('a', 64), _limits);
        staged.Rows.Select(r => r.Kind).Should().BeEquivalentTo([StagedKinds.Account, StagedKinds.Ownership, StagedKinds.Request]);
        staged.Rows.Single(r => r.Kind == StagedKinds.Request).LegacyReference.Should().Be($"legacy:{new string('a', 64)}:Talep_Takibi:7");
        staged.Rows.Single(r => r.Kind == StagedKinds.Request)[StagedFields.LegacyId].Should().Be("TP-0001");
        staged.IgnoredHelperColumns.Should().Be(1);
        staged.Warnings.Should().Contain("ArchiveSheetNotImported:Kaynak_Orijinal");
    }

    [Fact]
    public void LegacyPackage_RejectsOtherSchemas_AndSkipsSourceRowHeader()
    {
        Action wrong = () => Parse(ServiceAccountImportProfiles.LegacyPackage, Encoding.UTF8.GetBytes("{\"schemaVersion\":\"other\"}"));
        wrong.Should().Throw<ImportFileException>().Which.Code.Should().Be("UnsupportedPackageSchema");
        string json = $$$"""
            {"schemaVersion":"wasas.service-account-migration.v1","sourceReportDate":"2026-09-14","reportWorkbookSha256":"{{{new string('b', 64)}}}",
             "records":{"Hesap_Bilgileri":[{"legacySourceRow":7,"migrationKey":"{{{Guid.NewGuid()}}}","fields":{"Servis Hesabı":"SYN_PKG","Sorumlu Ekip":"Belirlenecek"}}]},
             "sourceRows":{"Book1":[["Kullanıcı Adı","Son Parola Değişiklik Zamanı","AD veya LDAP Son Oturum Açma Zamanı","AD Son Oturum Açma Zamanı","Organizasyon","Grup Direktorlugu","Yorum"],
                ["SYN_PKG",45723.5,null,null,"O","G","c"]]}}
            """;
        StagedFile staged = Parse(ServiceAccountImportProfiles.LegacyPackage, Encoding.UTF8.GetBytes(json));
        staged.SuggestedReportDate.Should().Be(new DateOnly(2026, 9, 14));
        staged.Rows.Should().HaveCount(2, "placeholder owner team creates no ownership row and the header row is not an account");
        staged.Rows.Last().RowNumber.Should().Be(2);
        staged.Rows.First().LegacyReference.Should().Be(ImportParser.LegacyReference(new string('b', 64), "Hesap_Bilgileri", 7));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("12")]
    [InlineData("31.02.2026")]
    public void InvalidBusinessDate_IsARowError_NotAFabricatedDate(string value)
    {
        ImportValues.TryDate(value, out DateOnly? date).Should().BeFalse();
        date.Should().BeNull();
    }

    private static byte[] Book1(string[] headers, SynCell?[] row) =>
        SyntheticWorkbook.Create([("Sheet2", [(1, headers.Select(h => (SynCell?)h).ToArray()), (2, row)])]);

    private static StagedFile Parse(string profile, byte[] bytes) =>
        ImportParser.Parse(profile, bytes, new StageImportRequest(profile, new DateOnly(2026, 9, 14), "test"), new string('0', 64), _limits);
}
