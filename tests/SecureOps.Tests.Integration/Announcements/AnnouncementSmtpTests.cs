using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MimeKit;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Infrastructure.Announcements.Mail;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Integration.Announcements;

public sealed class AnnouncementSmtpTests
{
    [Theory]
    [InlineData("Accepted", "Accepted", 2, 0)]
    [InlineData("Partial", "Partial", 1, 1)]
    [InlineData("Rejected", "Failed", 0, 2)]
    [InlineData("DataRejected", "Failed", 0, 2)]
    [InlineData("Unknown", "Unknown", 0, 0)]
    public async Task RealSmtp_SeparatesRecipientDataAcknowledgmentAndLostResponse(string mode, string state, int accepted, int rejected)
    {
        await using var sink = new LocalSmtpSink { Mode = mode };
        AnnouncementMailOptions config = Configuration(sink.Port);
        var policy = new AnnouncementMailPolicy(Options.Create(config), "Test");
        var transport = new SmtpAnnouncementTransport(Options.Create(config), policy);
        AnnouncementMailExecution execution = await ExecutionAsync(policy);
        AnnouncementTransportOutcome result = await transport.SubmitAsync(execution, TestContext.Current.CancellationToken);
        result.State.Should().Be(state);
        result.Accepted.Count.Should().Be(accepted);
        result.Rejected.Count.Should().Be(rejected);
        sink.Connections.Should().Be(1);
        if (mode == "Unknown")
        { sink.Messages.Should().ContainSingle("DATA was received, but no success response reached the client"); }
    }

    [Fact]
    public async Task ConfigurationAndByteTampering_FailBeforeAnyConnection()
    {
        await using var sink = new LocalSmtpSink();
        AnnouncementMailOptions config = Configuration(sink.Port);
        var policy = new AnnouncementMailPolicy(Options.Create(config), "Production");
        policy.Validate("Send", "actor@example.invalid", ["to@example.invalid"]).Should().Be("AnnouncementMailConfigurationInvalid");
        config.Host = "192.0.2.1";
        new AnnouncementMailPolicy(Options.Create(config), "Test").Validate("Send", "actor@example.invalid", ["to@example.invalid"]).Should().Be("AnnouncementMailConfigurationInvalid");
        config.Host = "127.0.0.1";
        policy = new(Options.Create(config), "Test");
        AnnouncementMailExecution execution = await ExecutionAsync(policy);
        var transport = new SmtpAnnouncementTransport(Options.Create(config), policy);
        (await transport.SubmitAsync(execution with { Message = [1, 2, 3] }, TestContext.Current.CancellationToken)).State.Should().Be("Denied");
        (await transport.SubmitAsync(execution with { Command = execution.Command with { Intent = execution.Command.Intent with { Kind = "SelfTest" } } }, TestContext.Current.CancellationToken)).State.Should().Be("Denied");
        sink.Connections.Should().Be(0);
    }

    internal static AnnouncementMailOptions Configuration(int port) => new()
    {
        Enabled = true,
        SelfTestEnabled = true,
        SendEnabled = true,
        Host = "127.0.0.1",
        Port = port,
        Security = "PlaintextLoopback",
        PolicyRevision = "synthetic-v1",
        EnvelopeMode = "Actor",
        AllowedRecipientDomains = ["example.invalid"],
        TimeoutSeconds = 5
    };
    internal static async Task<byte[]> MimeAsync(string sender, string[] to, string[] cc)
    {
        using var message = new MimeMessage { Subject = "Synthetic OCO", Date = DateTimeOffset.UnixEpoch, MessageId = "synthetic@wasas.invalid", Body = new TextPart("plain") { Text = "Synthetic only." } };
        message.From.Add(MailboxAddress.Parse(sender));
        message.To.AddRange(to.Select(MailboxAddress.Parse));
        message.Cc.AddRange(cc.Select(MailboxAddress.Parse));
        using var output = new MemoryStream();
        await message.WriteToAsync(output);
        return output.ToArray();
    }
    private static async Task<AnnouncementMailExecution> ExecutionAsync(AnnouncementMailPolicy policy)
    {
        byte[] bytes = await MimeAsync("actor@example.invalid", ["ok@example.invalid"], ["reject@example.invalid"]);
        var intent = new AnnouncementMailIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "preparation", "Send",
            new(Guid.NewGuid(), "Human", "Synthetic actor", "synthetic", "actor@example.invalid"), 1, "actor@example.invalid", "actor@example.invalid",
            ["ok@example.invalid"], ["reject@example.invalid"], "Synthetic OCO", "synthetic@wasas.invalid", Convert.ToHexString(SHA256.HashData(bytes)), policy.Fingerprint(), "synthetic", "preview");
        return new(new(intent, 2, "Dispatching", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [], null), Guid.NewGuid(), bytes);
    }
}
