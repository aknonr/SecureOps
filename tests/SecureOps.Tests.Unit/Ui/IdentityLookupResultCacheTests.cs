using FluentAssertions;
using SecureOps.Shared.Contracts.Identity;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Verifies short-lived result reuse suppresses redundant repeats without ever showing a stale or
/// unrelated answer as current.
/// </summary>
public sealed class IdentityLookupResultCacheTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    private static IdentityLookupRequest Request(
        string account = "pam12356",
        string purpose = "Alarm incelemesi",
        Guid? alertId = null,
        string? evtId = null) =>
        new(account, purpose, alertId, evtId);

    private static IdentityLookupResponse Response(string account = "pam12356") =>
        new("Found", account, "Mock", new IdentityLookupUserDto(
            "Test User", account, null, null, null, null, null, true, false));

    [Fact]
    public void TryReuse_ReturnsFalseBeforeAnythingIsStored()
    {
        IdentityLookupResultCache cache = new();

        cache.TryReuse(Request(), _now, out IdentityLookupResponse? response).Should().BeFalse();
        response.Should().BeNull();
    }

    [Fact]
    public void TryReuse_ReusesAnIdenticalRequestInsideTheWindow()
    {
        IdentityLookupResultCache cache = new();
        cache.Store(Request(), Response(), _now);

        bool reused = cache.TryReuse(Request(), _now.AddSeconds(5), out IdentityLookupResponse? response);

        reused.Should().BeTrue();
        response.Should().NotBeNull();
    }

    [Fact]
    public void TryReuse_StopsReusingOnceTheWindowHasPassed()
    {
        IdentityLookupResultCache cache = new();
        cache.Store(Request(), Response(), _now);

        cache.TryReuse(Request(), _now + IdentityLookupResultCache.Lifetime, out _)
            .Should().BeFalse("the result is no longer guaranteed to reflect the directory");
    }

    [Fact]
    public void TryReuse_TreatsDomainPrefixedAndBareAccountsAsOneQuestion()
    {
        IdentityLookupResultCache cache = new();
        cache.Store(Request("CONTOSO\\PAM12356"), Response(), _now);

        // The server normalizes both to the same account, so re-asking is genuinely a repeat.
        cache.TryReuse(Request("pam12356"), _now.AddSeconds(1), out _).Should().BeTrue();
    }

    [Fact]
    public void TryReuse_DoesNotReuseWhenThePurposeChanges()
    {
        IdentityLookupResultCache cache = new();
        cache.Store(Request(purpose: "Alarm incelemesi"), Response(), _now);

        // Purpose is recorded with the lookup: a different purpose is a different audited action and
        // must reach the server.
        cache.TryReuse(Request(purpose: "Yetki gozden gecirme"), _now.AddSeconds(1), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryReuse_DoesNotReuseWhenTheAccountChanges()
    {
        IdentityLookupResultCache cache = new();
        cache.Store(Request("pam12356"), Response(), _now);

        cache.TryReuse(Request("pam99999"), _now.AddSeconds(1), out _).Should().BeFalse();
    }

    [Fact]
    public void TryReuse_DoesNotReuseWhenEventReferencesChange()
    {
        IdentityLookupResultCache cache = new();
        var alertId = Guid.NewGuid();
        cache.Store(Request(), Response(), _now);

        cache.TryReuse(Request(alertId: alertId), _now.AddSeconds(1), out _).Should().BeFalse();
        cache.TryReuse(Request(evtId: "EVT-1"), _now.AddSeconds(1), out _).Should().BeFalse();
    }

    [Fact]
    public void SignatureFields_CannotBleedIntoEachOther()
    {
        IdentityLookupResultCache cache = new();
        cache.Store(Request(account: "ab", purpose: "cd"), Response(), _now);

        // Naive concatenation would make "ab"+"cd" and "a"+"bcd" collide.
        cache.TryReuse(Request(account: "a", purpose: "bcd"), _now.AddSeconds(1), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Invalidate_ForcesTheNextRequestToReachTheServer()
    {
        IdentityLookupResultCache cache = new();
        cache.Store(Request(), Response(), _now);

        cache.Invalidate();

        cache.TryReuse(Request(), _now.AddSeconds(1), out _).Should().BeFalse();
        cache.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void CompletedAt_ReportsWhenTheDisplayedResultWasProduced()
    {
        IdentityLookupResultCache cache = new();
        cache.Store(Request(), Response(), _now);

        cache.CompletedAt.Should().Be(_now);
    }
}
