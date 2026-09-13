using System.Globalization;
using FluentAssertions;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

public sealed class AnnouncementTimeTests
{
    [Fact]
    public async Task PreviewGeneration_RejectsLateSuccessOrFailureAndCancelsHiddenOrDisposedWork()
    {
        using var lifetime = new CancellationTokenSource();
        using var requests = new AnnouncementPreviewRequests();
        var late = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        (long first, CancellationToken oldToken) = requests.Begin(lifetime.Token);
        async Task<string?> CompleteAsync()
        { string result = await late.Task; return requests.IsCurrent(first) ? result : null; }
        Task<string?> old = CompleteAsync();
        (long second, CancellationToken newToken) = requests.Begin(lifetime.Token);
        oldToken.IsCancellationRequested.Should().BeTrue();
        newToken.IsCancellationRequested.Should().BeFalse();
        late.SetResult("obsolete HTML");
        (await old).Should().BeNull();
        requests.IsCurrent(first).Should().BeFalse();
        requests.IsCurrent(second).Should().BeTrue();
        requests.Cancel();
        newToken.IsCancellationRequested.Should().BeTrue();
        requests.IsCurrent(second).Should().BeFalse();
        (long third, CancellationToken token) = requests.Begin(lifetime.Token);
        lifetime.Cancel();
        token.IsCancellationRequested.Should().BeTrue();
        requests.IsCurrent(third).Should().BeFalse();
    }
    [Theory]
    [InlineData("2026-09-13T23:59:37+03:00", "2026-09-13T20:59:37+00:00")]
    [InlineData("2026-09-13T01:01:59+05:45", "2026-09-12T19:16:59+00:00")]
    public void OffsetChange_PreservesInstantSecondsAndOvernightDate(string value, string expected)
    {
        var time = new AnnouncementTime();
        time.Load(value, "+00:00");
        time.Value.Should().Be(value);
        time.ConvertOffset("+00:00").Should().BeTrue();
        time.Value.Should().Be(expected);
        DateTimeOffset.Parse(time.Value, CultureInfo.InvariantCulture).Should().Be(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture));
        for (int minute = 0; minute < 60; minute++)
        { time.Minute = minute.ToString("00"); time.Value.Should().Contain(":" + minute.ToString("00") + ":"); }
    }
    [Fact]
    public void DefaultsAndPartialInput_DoNotFabricateTimesOrUpgradeLegacyDrafts()
    {
        var time = new AnnouncementTime();
        time.Load("", "+03:00");
        time.Value.Should().BeEmpty();
        time.Offset.Should().Be("+03:00");
        time.Day = "2026-09-13";
        time.Value.Should().Be("2026-09-13T::00+03:00");
        time.ConvertOffset("+01:00").Should().BeFalse();
        AnnouncementTime.ValidOffset("+14:30").Should().BeFalse();
        AnnouncementTime.ValidOffset("source-LMT").Should().BeFalse();
        var form = new AnnouncementForm();
        form.Template.Should().Be("oco-table-v2");
        form.Values["WorkStart"] = "2026-09-13T23:59:37+05:45";
        SecureOps.Domain.Announcements.AnnouncementContent legacy = form.Content() with { TemplateRevision = "oco-v1", DateTextRevision = "iso-v1", AffectedServices = null };
        AnnouncementForm.From(legacy).Content().Should().BeEquivalentTo(legacy);
    }
}
