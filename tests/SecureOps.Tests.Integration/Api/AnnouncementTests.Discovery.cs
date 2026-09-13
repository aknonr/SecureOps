using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;
using SecureOps.Tests.Integration.Sql;

namespace SecureOps.Tests.Integration.Api;

public sealed partial class AnnouncementTests
{
    [Fact]
    public void UiContract_JsonExamplesMatchImplementedContracts()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SecureOps.sln")))
        { root = root.Parent; }
        root.Should().NotBeNull();
        string document = File.ReadAllText(Path.Combine(root!.FullName, "docs", "contracts", "planned-announcements-v1.md"));
        string[] examples = System.Text.RegularExpressions.Regex.Matches(document, @"```json\s*(.*?)\s*```", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value).ToArray();
        examples.Should().HaveCount(4);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        AnnouncementValidation.Errors(JsonSerializer.Deserialize<AnnouncementContent>(examples[0], json)!, true).Should().BeEmpty();
        JsonSerializer.Deserialize<AnnouncementPage>(examples[1], json)!.Items.Single().MissingFieldCount.Should().Be(0);
        JsonSerializer.Deserialize<AnnouncementBanner[]>(examples[2], json)!.Select(b => b.State).Should().Equal("PresentNotValidated", "Missing");
        using var configuration = JsonDocument.Parse(examples[3]);
        AnnouncementOptions options = configuration.RootElement.GetProperty("Announcements").Deserialize<AnnouncementOptions>()!;
        AnnouncementAssetBundle bundle = options.Bundles.Single().Value;
        bundle.Assets.Keys.Should().BeEquivalentTo(_roles);
        bundle.Assets.Values.Should().OnlyContain(revision => options.Banners.ContainsKey(revision));
        bundle.Footer.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Banners_MetadataIsBoundedDoesNotReadImageBytesAndHasNoPaths()
    {
        string root = Assets();
        var config = new AnnouncementOptions
        {
            AssetDirectory = root,
            Banners = new() { ["synthetic-v1"] = "banner.bin", ["missing-v1"] = "absent.png" },
            BannerLabels = new() { ["synthetic-v1"] = "Türkçe & sade", ["missing-v1"] = "<script>" }
        };
        var renderer = new AnnouncementRenderer(Options.Create(config));
        await using (var locked = new FileStream(Path.Combine(root, "banner.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            renderer.Banners().Last().Should().Be(new AnnouncementBanner("synthetic-v1", "Türkçe & sade", "PresentNotValidated"));
            await FluentActions.Awaiting(() => renderer.AssetAsync("synthetic-v1", default)).Should().ThrowAsync<IOException>();
        }
        await File.WriteAllBytesAsync(Path.Combine(root, "banner.bin"), new byte[64]);
        renderer.Banners().Last().State.Should().Be("PresentNotValidated");
        await FluentActions.Awaiting(() => renderer.AssetAsync("synthetic-v1", default)).Should().ThrowAsync<InvalidOperationException>();
        renderer.Banners().First().Should().Be(new AnnouncementBanner("missing-v1", "missing-v1", "Missing"));
        JsonSerializer.Serialize(renderer.Banners()).Should().NotContain(root).And.NotContain("banner.bin").And.NotContain("<script>");
        var watch = Stopwatch.StartNew();
        for (int n = 0; n < 100; n++)
        { renderer.Banners().Should().HaveCount(2); }
        output.WriteLine("100 two-banner metadata calls ms={0:F2}; image bytes read=0 (exclusive-lock proof)", watch.Elapsed.TotalMilliseconds);
        config.Banners = Enumerable.Range(0, 33).ToDictionary(n => "banner-" + n, _ => "banner.bin");
        FluentActions.Invoking(() => renderer.Banners()).Should().Throw<InvalidOperationException>();
    }

    [LocalResourceSqlFact]
    public async Task Discovery_SqlOwnerLatestPagesConcurrencyAndLocalMeasurements()
    {
        string value = Environment.GetEnvironmentVariable("SECUREOPS_SQL_TEST_CONNECTION")!;
        var guard = new SqlConnectionStringBuilder(value);
        guard.DataSource.Should().BeEquivalentTo("(localdb)\\SecureOpsResourcesV1");
        guard.InitialCatalog.Should().StartWith("SecureOps_ResourcesV1_Oco");
        guard.IntegratedSecurity.Should().BeTrue();
        await using var sql = new SqlConnection(value);
        Guid owner = Guid.NewGuid(), other = Guid.NewGuid();
        foreach (Guid actor in new[] { owner, other })
        { await sql.ExecuteAsync("INSERT INTO security.Users(UserId,CorporateIdentity,AuthenticationSource,AccessStatus) VALUES(@actor,@identity,'test','Approved')", new { actor, identity = "synthetic:oco:" + actor }); }
        IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SecureOpsDb"] = value }).Build();
        var store = new SqlAnnouncementStore(config);
        var time = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
        List<Guid> ids = [];
        foreach (Guid actor in new[] { owner, other })
        {
            for (int n = 0; n < 120; n++)
            {
                var id = Guid.NewGuid();
                if (actor == owner)
                { ids.Add(id); }
                for (int revision = 1; revision <= 5; revision++)
                {
                    var draft = new AnnouncementDraft(id, actor, revision, time, Content() with
                    {
                        Subject = n == 0 ? "" : "Synthetic &lt;b&gt; " + n,
                        Description = new string('x', 3500)
                    }, "sender@example.invalid", "synthetic");
                    (await store.SaveAsync(draft, "synthetic", default)).Should().BeNull();
                }
            }
        }
        AnnouncementPage first = await store.ListAsync(owner, 1, 25, default);
        first.Total.Should().Be(120);
        first.Items.Should().OnlyContain(i => i.Version == 5);
        first.Items.Select(i => i.Id).Should().Equal(ids.OrderBy(i => i.ToString("D"), StringComparer.Ordinal).Take(25));
        List<Guid> seen = [];
        var watch = Stopwatch.StartNew();
        int bytes = 0;
        for (int page = 1; page <= 5; page++)
        {
            AnnouncementPage found = await store.ListAsync(owner, page, 25, default);
            seen.AddRange(found.Items.Select(i => i.Id));
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(found, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            bytes = Math.Max(bytes, json.Length);
            System.Text.Encoding.UTF8.GetString(json).Should().NotContain("description").And.NotContain("recipient").And.NotContain("sender");
        }
        seen.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(ids);
        output.WriteLine("SQL 240 drafts / 1200 revisions: five 25-item pages ms={0:F2}; largest bytes={1}", watch.Elapsed.TotalMilliseconds, bytes);
        (await store.ListAsync(owner, 6, 25, default)).Items.Should().BeEmpty();
        (await store.ListAsync(owner, 10000, 100, default)).Total.Should().Be(120);
        (await store.ListAsync(Guid.NewGuid(), 1, 25, default)).Total.Should().Be(0);
        (await store.ListAsync(owner, 1, 100, default)).Items.Should().HaveCount(100);
        (await store.ListAsync(other, 1, 100, default)).Items.Should().NotContain(i => ids.Contains(i.Id));
        AnnouncementDraft latest = (await store.GetAsync(ids[0], owner, default))!;
        (await store.ListAsync(owner, 1, 100, default)).Items.Where(i => i.Id == ids[0]).Should().OnlyContain(i => i.MissingFieldCount == 1);
        Task<string?> save = store.SaveAsync(latest with { Version = 6, SavedAt = time.AddMinutes(1) }, "synthetic", default);
        AnnouncementPage during = await new SqlAnnouncementStore(config).ListAsync(owner, 1, 100, default);
        (await save).Should().BeNull();
        during.Total.Should().Be(120);
        during.Items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        AnnouncementPage after = await store.ListAsync(owner, 1, 25, default);
        after.Items.First().Id.Should().Be(ids[0]);
        after.Items.First().Version.Should().Be(6);
        after.Items.First().MissingFieldCount.Should().Be(1);
        for (int revision = 7; revision <= 12; revision++)
        {
            Task<string?> update = store.SaveAsync(latest with { Version = revision, SavedAt = time.AddMinutes(revision) }, "synthetic", default);
            Task<AnnouncementPage>[] readers = Enumerable.Range(0, 3).Select(_ => new SqlAnnouncementStore(config).ListAsync(owner, 1, 25, default)).ToArray();
            (await update).Should().BeNull();
            foreach (AnnouncementPage found in await Task.WhenAll(readers))
            {
                found.Total.Should().Be(120);
                found.Items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
                found.Items.First().Version.Should().BeOneOf((long)revision, revision - 1L);
            }
        }
        AnnouncementDraft legacy = latest with { Id = Guid.NewGuid(), Version = 1 };
        JsonNode document = JsonSerializer.SerializeToNode(legacy)!;
        document.AsObject().Remove("MissingFieldCount");
        await sql.ExecuteAsync("INSERT INTO announcements.DraftRevisions(Id,Version,OwnerId,DocumentJson) VALUES(@Id,1,@OwnerId,@json)", new { legacy.Id, legacy.OwnerId, json = document.ToJsonString() });
        List<AnnouncementSummary> all = [];
        for (int page = 1; page <= 2; page++)
        { all.AddRange((await store.ListAsync(owner, page, 100, default)).Items); }
        all.Single(i => i.Id == legacy.Id).MissingFieldCount.Should().BeNull();
        string[] plans = (await sql.QueryAsync<string>("""
            SELECT CONVERT(nvarchar(max),qp.query_plan) FROM sys.dm_exec_query_stats qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) t CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
            WHERE t.text LIKE '%INTO #LatestAnnouncements%' AND t.text NOT LIKE '%dm_exec_query_stats%'
              AND EXISTS(SELECT 1 FROM sys.dm_exec_plan_attributes(qs.plan_handle) a WHERE a.attribute='dbid' AND CONVERT(int,a.value)=DB_ID());
            """)).ToArray();
        plans.Should().NotBeEmpty();
        output.WriteLine("Cached list plans reference owner index={0}; contain Index Seek={1}",
            plans.Any(p => p.Contains("IX_AnnouncementDraftRevisions_OwnerLatest", StringComparison.Ordinal)), plans.Any(p => p.Contains("Index Seek", StringComparison.Ordinal)));
        string principal = "OcoLockTest_" + Guid.NewGuid().ToString("N");
        await sql.ExecuteAsync($"CREATE USER [{principal}] WITHOUT LOGIN;");
        int acquired = await sql.ExecuteScalarAsync<int>($"""
            EXECUTE AS USER='{principal}';
            BEGIN TRY
                BEGIN TRANSACTION;
                DECLARE @r int;
                EXEC @r=sys.sp_getapplock @Resource='SyntheticOcoPublicLock',@LockMode='Shared',@LockOwner='Transaction',@DbPrincipal='public',@LockTimeout=0;
                ROLLBACK; REVERT; SELECT @r;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT>0 ROLLBACK;
                REVERT; THROW;
            END CATCH;
            """);
        acquired.Should().BeGreaterThanOrEqualTo(0);
    }
}
