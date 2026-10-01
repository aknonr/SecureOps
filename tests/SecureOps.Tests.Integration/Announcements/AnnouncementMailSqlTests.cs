using System.Security.Claims;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MimeKit;
using NSubstitute;
using SecureOps.Api.Security;
using SecureOps.Domain.Access;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Infrastructure.Announcements.Mail;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;
using SecureOps.Tests.Integration.Sql;

namespace SecureOps.Tests.Integration.Announcements;

public sealed class MailSqlFactAttribute : FactAttribute
{
    public MailSqlFactAttribute() { if (Environment.GetEnvironmentVariable("SECUREOPS_MAIL_SQL_CONNECTION") is null) { Skip = "Requires fresh task-owned 001-020 LocalDB OcoMail harness and loopback SMTP sink."; } }
}

public sealed class AnnouncementMailSqlTests
{
    [MailSqlFact]
    public async Task Mail_ExactPreview_SelfOnly_Replay_RestartUnknown_Revocation_AuditAndHistoricalIdentity()
    {
        string connection = Environment.GetEnvironmentVariable("SECUREOPS_MAIL_SQL_CONNECTION")!;
        var guard = new SqlConnectionStringBuilder(connection);
        guard.DataSource.Should().Be("(localdb)\\SecureOpsResourcesV1");
        guard.InitialCatalog.Should().StartWith("SecureOps_ResourcesV1_OcoMail");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SecureOpsDb"] = connection }).Build();
        await using var sql = new SqlConnection(connection);
        (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM announcements.MailCommands")).Should().Be(0, "this test requires its own fresh database");
        string identity = await SqlAccessTestActors.AdminAsync(configuration);
        var repository = new SqlAccessRepository(configuration);
        ApplicationUser actor = (await repository.GetUserAsync(identity, default))!;
        // New external capabilities are never seeded by migration; this isolated fixture assigns both explicitly.
        await sql.ExecuteAsync("""
            INSERT INTO security.Roles(RoleId,RoleCode,DisplayName,Purpose,Version,CapabilitiesJson)
                VALUES(100,'SyntheticMail','Synthetic mail','Local sink only',1,'["Announcements.SelfTest","Announcements.Send"]');
            INSERT INTO security.RoleAssignments(RoleAssignmentId,UserId,RoleId,GrantedByCorporateIdentity) VALUES(NEWID(),@id,100,'synthetic-fixture');
            UPDATE security.Users SET AccessVersion=AccessVersion+1 WHERE UserId=@id;
            """, new { id = actor.Id });
        actor = (await repository.GetUserAsync(identity, default))!;
        IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
        access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
            .Returns(async _ => AccessServiceResult<EnsureAccessUserResult>.Success(new((await repository.GetUserAsync(identity, default))!, null, false, false)));
        var drafts = new SqlAnnouncementStore(configuration);
        var store = new SqlAnnouncementMailStore(configuration);
        await using var sink = new LocalSmtpSink();
        AnnouncementMailOptions config = AnnouncementSmtpTests.Configuration(sink.Port);
        var policy = new AnnouncementMailPolicy(Options.Create(config), "Test");
        var transport = new SmtpAnnouncementTransport(Options.Create(config), policy);
        var worker = new AnnouncementMailWorker(store, policy, transport, Options.Create(config));
        IAnnouncementMailDispatcher dispatch = Substitute.For<IAnnouncementMailDispatcher>();
        dispatch.IsConfigured.Returns(true);
        var service = new AnnouncementMailService(access, drafts, store, policy, dispatch,
            new AnnouncementMailPreviewCodec(new EphemeralDataProtectionProvider()), Options.Create(new AnnouncementOptions { Enabled = true }));
        var principal = new ClaimsPrincipal();
        var context = new AccessOperationContext("synthetic-mail", "mail-sql", null);
        PreparedAnnouncement prepared = await PreparationAsync(drafts, actor);

        AnnouncementMailPreview preview = (await service.PreviewAsync(principal, context, new(prepared.Id, "SelfTest"), default)).Preview!;
        preview.Should().NotBeNull();
        preview.To.Should().Equal(actor.Mail!);
        preview.Cc.Should().BeEmpty();
        preview.RecipientCount.Should().Be(1);
        preview.OcoReference.Should().Be(prepared.Draft.Content.OcoReference);
        preview.WorkStart.Should().Be(prepared.Draft.Content.WorkStart);
        preview.WorkEnd.Should().Be(prepared.Draft.Content.WorkEnd);
        preview.PreparedAt.Should().Be(prepared.PreparedAt);
        (await service.ConfirmAsync(principal, context, new(preview.PreviewToken + "tampered"), default)).Error.Should().Be("AnnouncementMailPreviewExpired");
        AnnouncementMailResult[] clicks = await Task.WhenAll(service.ConfirmAsync(principal, context, new(preview.PreviewToken), default), service.ConfirmAsync(principal, context, new(preview.PreviewToken), default));
        clicks.Should().OnlyContain(r => r.Error == null && r.Status!.CommandId == preview.CommandId);
        await Task.WhenAll(worker.RunAsync(preview.CommandId, default), worker.RunAsync(preview.CommandId, default));
        sink.Messages.Should().ContainSingle();
        sink.Messages[0].Recipients.Should().Equal(actor.Mail!);
        using (var sent = MimeMessage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(sink.Messages[0].Data))))
        { sent.To.Mailboxes.Select(x => x.Address).Should().Equal(actor.Mail!); sent.Cc.Count.Should().Be(0); sent.From.Mailboxes.Single().Address.Should().Be(actor.Mail); }
        (await service.ConfirmAsync(principal, context, new(preview.PreviewToken), default)).Status!.State.Should().Be("Accepted");
        await worker.RunAsync(preview.CommandId, default);
        sink.Messages.Should().ContainSingle();
        (await drafts.PreparedAsync(prepared.Id, actor.Id, default))!.Email.Should().Equal(prepared.Email);
        (await drafts.GetAsync(prepared.Draft.Id, actor.Id, default))!.Content.To.Should().Equal("ok@example.invalid");
        (await drafts.GetAsync(prepared.Draft.Id, actor.Id, default))!.Content.Cc.Should().Equal("reject@example.invalid");

        sink.Mode = "Unknown";
        AnnouncementMailPreview distribution = (await service.PreviewAsync(principal, context, new(prepared.Id, "Send"), default)).Preview!;
        distribution.RecipientCount.Should().Be(2);
        distribution.WorkStart.Should().Be(preview.WorkStart);
        (await service.ConfirmAsync(principal, context, new(distribution.PreviewToken), default)).Error.Should().BeNull();
        await worker.RunAsync(distribution.CommandId, default);
        (await store.GetAsync(distribution.CommandId, actor.Id, default))!.State.Should().Be("Unknown");
        sink.Messages.Should().HaveCount(2);
        var restarted = new AnnouncementMailWorker(new(configuration), policy, transport, Options.Create(config));
        await restarted.RunAsync(distribution.CommandId, default);
        sink.Messages.Should().HaveCount(2);
        AnnouncementMailPreview second = (await service.PreviewAsync(principal, context, new(prepared.Id, "Send"), default)).Preview!;
        (await service.ConfirmAsync(principal, context, new(second.PreviewToken), default)).Error.Should().Be("AnnouncementMailAlreadyRequested");
        AnnouncementMailPreview repeatedSelf = (await service.PreviewAsync(principal, context, new(prepared.Id, "SelfTest"), default)).Preview!;
        (await service.ConfirmAsync(principal, context, new(repeatedSelf.PreviewToken), default)).Error.Should().Be("AnnouncementMailAlreadyRequested");

        PreparedAnnouncement another = await PreparationAsync(drafts, actor);
        AnnouncementMailPreview queued = (await service.PreviewAsync(principal, context, new(another.Id, "SelfTest"), default)).Preview!;
        await service.ConfirmAsync(principal, context, new(queued.PreviewToken), default);
        await sql.ExecuteAsync("UPDATE security.Users SET AccessVersion=AccessVersion+1,DisplayName='Later profile' WHERE UserId=@id", new { id = actor.Id });
        await restarted.RunAsync(queued.CommandId, default);
        (await store.GetAsync(queued.CommandId, actor.Id, default))!.State.Should().Be("Denied");
        sink.Messages.Should().HaveCount(2);
        string historical = await sql.QuerySingleAsync<string>("SELECT EvidenceJson FROM ops.OperationEvents WHERE CommandId=@id AND Outcome='Requested'", new { id = preview.CommandId });
        historical.Should().Contain("Synthetic administrator").And.NotContain("Later profile");

        AnnouncementMailPreview auditPreview = (await service.PreviewAsync(principal, context, new(another.Id, "SelfTest"), default)).Preview!;
        await sql.ExecuteAsync("CREATE TRIGGER audit.TR_SyntheticMailFailure ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Action='AnnouncementSelfTest') THROW 51182,'Synthetic mail audit failure',1; END;");
        try
        {
            (await service.ConfirmAsync(principal, context, new(auditPreview.PreviewToken), default)).Error.Should().Be("AnnouncementMailUnavailable");
            (await store.GetAsync(auditPreview.CommandId, actor.Id, default)).Should().BeNull();
        }
        finally { await sql.ExecuteAsync("DROP TRIGGER audit.TR_SyntheticMailFailure"); }
        await service.ConfirmAsync(principal, context, new(auditPreview.PreviewToken), default);
        AnnouncementMailExecution claimed = (await store.ClaimAsync(auditPreview.CommandId, policy.Fingerprint(), new("Synthetic.Worker", "old"), default))!;
        await sql.ExecuteAsync("UPDATE announcements.MailCommands SET LeaseExpiresAt=DATEADD(MINUTE,-1,SYSUTCDATETIME()) WHERE CommandId=@id", new { id = auditPreview.CommandId });
        (await store.RecoverAsync(default)).Should().NotContain(auditPreview.CommandId);
        (await store.CompleteAsync(claimed, new("Accepted", [actor.Mail!], []), new("Synthetic.Worker", "old"), default)).Should().BeFalse();
        (await store.GetAsync(auditPreview.CommandId, actor.Id, default))!.State.Should().Be("Unknown");
        await restarted.RunAsync(auditPreview.CommandId, default);
        sink.Messages.Should().HaveCount(2);
        await FluentActions.Awaiting(() => sql.ExecuteAsync("UPDATE ops.OperationEvents SET Outcome='Verified' WHERE CommandId=@id", new { id = preview.CommandId })).Should().ThrowAsync<SqlException>();
        await FluentActions.Awaiting(() => sql.ExecuteAsync("UPDATE announcements.MailCommands SET IntentJson='{}' WHERE CommandId=@id", new { id = preview.CommandId })).Should().ThrowAsync<SqlException>();
    }

    private static async Task<PreparedAnnouncement> PreparationAsync(SqlAnnouncementStore store, ApplicationUser actor)
    {
        var content = new AnnouncementContent("OCO-100", "Synthetic", "Synthetic OCO", "2026-09-15", "2026-09-15T10:00:00Z", "2026-09-15T11:00:00Z",
            "Synthetic work", "Synthetic impact", "Synthetic check", "", ["ok@example.invalid"], ["reject@example.invalid"], "synthetic");
        var draft = new AnnouncementDraft(Guid.NewGuid(), actor.Id, 1, DateTimeOffset.UtcNow, content, actor.Mail!, "synthetic");
        (await store.SaveAsync(draft, "mail-test", default)).Should().BeNull();
        var prepared = new PreparedAnnouncement(Guid.NewGuid(), draft, DateTimeOffset.UtcNow, "Synthetic", "", "<p>Synthetic</p>",
            await AnnouncementSmtpTests.MimeAsync(actor.Mail!, content.To, content.Cc), new Dictionary<string, string>());
        prepared = prepared with { Fingerprint = AnnouncementService.PreparationFingerprint(prepared) };
        return (await store.PrepareAsync(prepared, "mail-test", default)).Snapshot!;
    }
}
