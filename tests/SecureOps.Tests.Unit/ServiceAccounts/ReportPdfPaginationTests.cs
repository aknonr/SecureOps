using FluentAssertions;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ReportPdfPaginationTests
{
    private const string _footer = "Kurum içi · salt okunur rapor nüshası";

    [Fact]
    public void SectionHeading_IsNeverLeftAloneAtThePageBottom()
    {
        for (int rows = 1; rows <= 70; rows++)
        {
            ReportSection section = new("Bölüm", ["Ad", "Değer"], [.. Enumerable.Range(0, rows).Select(i => (IReadOnlyList<ReportCell>)[$"SYN_{i}", i])]);
            ReportDocument document = new("Servis Hesapları Rapor", [("Kapsam", "SYN")], [section, section, section],
                new DateTimeOffset(2026, 10, 4, 15, 0, 0, TimeSpan.Zero), null);

            List<string> lines = [.. ReportPdfWriter.ExtractLines(ReportPdfWriter.Write(document))];
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i] == _footer)
                {
                    lines[i - 1].Should().NotBe("BÖLÜM", $"a heading must not end a page ({rows} rows)");
                    lines[i - 1].TrimStart().Should().NotStartWith("Ad ", $"a table header must not end a page ({rows} rows)");
                }
            }
        }
    }
}
