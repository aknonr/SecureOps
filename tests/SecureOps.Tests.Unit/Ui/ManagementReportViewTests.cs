using FluentAssertions;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Reporting;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the two presentation rules the reporting policy actually rests on: an unmeasured metric is
/// never shown as zero, and no figure appears that the API did not send.
/// </summary>
public sealed class ManagementReportViewTests
{
    [Fact]
    public void NullCount_RendersAsUnavailableRatherThanZero()
    {
        // rateLimitEvents is null because rate-limit rejections are not audited yet. Rendering it
        // as "0" would assert that none happened, which nobody knows.
        ManagementReportView.Count((long?)null).Should().Be(ManagementReportView.UnavailableText);
        ManagementReportView.Count((long?)0).Should().Be("0");
    }

    [Fact]
    public void Count_GroupsThousandsIndependentlyOfTheMachineCulture()
    {
        ManagementReportView.Count(1234567).Should().Be("1.234.567");
    }

    [Theory]
    [InlineData(0, "0 sn")]
    [InlineData(45, "45 sn")]
    [InlineData(60, "1 dk")]
    [InlineData(150, "2 dk 30 sn")]
    [InlineData(3600, "1 sa")]
    [InlineData(11640, "3 sa 14 dk")]
    [InlineData(86400, "1 gün")]
    [InlineData(190800, "2 gün 5 sa")]
    public void FormatSeconds_DropsPrecisionAsTheMagnitudeGrows(double seconds, string expected)
    {
        ManagementReportView.FormatSeconds(seconds).Should().Be(expected);
    }

    [Fact]
    public void FormatSeconds_ReturnsNullForAnUnmeasuredDuration()
    {
        ManagementReportView.FormatSeconds(null).Should().BeNull();
        ManagementReportView.FormatSeconds(double.NaN).Should().BeNull();
        ManagementReportView.FormatSeconds(-1).Should().BeNull();
    }

    [Fact]
    public void Duration_WithoutSamples_ReportsNoSamplesRatherThanZeroSeconds()
    {
        // The projector returns this shape whenever no workflow in the window produced a
        // measurable interval. "0 sn" would claim the work was instantaneous.
        ManagementReportView.DurationView view = ManagementReportView.Duration(
            new DurationStatisticsResponse(
                "claimToCompletion", "Workflow claim to durable workflow completion", 0, null, null, null));

        view.HasSamples.Should().BeFalse();
        view.Average.Should().BeNull();
        view.Label.Should().Be("Devralma → tamamlanma");
    }

    [Fact]
    public void Duration_WithSamples_IsFormatted()
    {
        ManagementReportView.DurationView view = ManagementReportView.Duration(
            new DurationStatisticsResponse(
                "importToPreview", "First persisted import to first persisted preview", 12, 30, 150, 3600));

        view.HasSamples.Should().BeTrue();
        view.SampleCount.Should().Be(12);
        view.Minimum.Should().Be("30 sn");
        view.Average.Should().Be("2 dk 30 sn");
        view.Maximum.Should().Be("1 sa");
    }

    [Theory]
    [InlineData("importToPreview")]
    [InlineData("claimToJiraCreation")]
    [InlineData("claimToCompletion")]
    public void DurationLabel_TranslatesEveryStableKeyTheContractDefines(string key)
    {
        // These three keys are the contract's identity for a duration. A missing one would fall
        // through to English definition text on a Turkish screen.
        ManagementReportView.DurationLabel(key, "definition text").Should().NotBe("definition text");
    }

    [Fact]
    public void DurationLabel_IgnoresTheDefinitionTextWhenChoosingALabel()
    {
        // The contract states that definition wording is a presentation detail. A reworded English
        // definition must not change what the UI thinks the row means.
        ManagementReportView.DurationLabel("claimToCompletion", "Completely different wording")
            .Should().Be("Devralma → tamamlanma");
    }

    [Fact]
    public void DurationLabel_FallsBackToTheServerDefinitionForAnUnknownKey()
    {
        // An interval added server-side must render as itself rather than be mislabelled as one of
        // the three the UI knows.
        ManagementReportView.DurationLabel("someNewInterval", "Some interval the UI has never seen")
            .Should().Be("Some interval the UI has never seen");
    }

    [Fact]
    public void Duration_CarriesTheStableKeyThrough()
    {
        ManagementReportView.Duration(
            new DurationStatisticsResponse("claimToJiraCreation", "anything", 1, 1, 1, 1))
            .Key.Should().Be("claimToJiraCreation");
    }

    [Fact]
    public void Coverage_CompleteMeansAZeroIsAMeasuredZero()
    {
        ManagementReportView.CoverageView view = ManagementReportView.Coverage(
            new ReportingEvidenceCoverageResponse(
                Utc(8, 16), Utc(8, 23), Utc(8, 1), CoverageComplete: true));

        view.State.Should().Be(ManagementReportView.CoverageState.Complete);
        view.ZeroIsMeasured.Should().BeTrue();
        view.UncoveredDays.Should().Be(0);
    }

    [Fact]
    public void Coverage_WithNoBoundaryAtAllIsReportedAsNone()
    {
        ManagementReportView.CoverageView view = ManagementReportView.Coverage(
            new ReportingEvidenceCoverageResponse(
                Utc(8, 16), Utc(8, 23), CoverageFromUtc: null, CoverageComplete: false));

        view.State.Should().Be(ManagementReportView.CoverageState.None);
        view.ZeroIsMeasured.Should().BeFalse();
        view.UncoveredDays.Should().Be(7);
    }

    [Fact]
    public void Coverage_WithABoundaryInsideTheWindowMeasuresTheUncoveredHead()
    {
        // The case the brief names: 30 days asked for, 10 days retained. The other 20 must never be
        // drawn as zero activity, and the notice has to be able to say how many they are.
        ManagementReportView.CoverageView view = ManagementReportView.Coverage(
            new ReportingEvidenceCoverageResponse(
                Utc(7, 24), Utc(8, 23), Utc(8, 13), CoverageComplete: false));

        view.State.Should().Be(ManagementReportView.CoverageState.Partial);
        view.RequestedDays.Should().Be(30);
        view.UncoveredDays.Should().Be(20);
        view.ZeroIsMeasured.Should().BeFalse();
    }

    [Fact]
    public void Coverage_WithABoundaryAfterTheWindowLeavesNothingCovered()
    {
        // Persistence began after the whole requested range. Nothing in the window is measured, so
        // the uncovered span is the entire window rather than a negative number.
        ManagementReportView.CoverageView view = ManagementReportView.Coverage(
            new ReportingEvidenceCoverageResponse(
                Utc(7, 24), Utc(8, 3), Utc(8, 20), CoverageComplete: false));

        view.State.Should().Be(ManagementReportView.CoverageState.Partial);
        view.UncoveredDays.Should().Be(view.RequestedDays).And.Be(10);
    }

    [Fact]
    public void Coverage_TrustsTheServerFlagRatherThanRecomputingItFromDates()
    {
        // The server owns the verdict. Even with a boundary that looks complete, a false flag must
        // not be overridden by client-side arithmetic.
        ManagementReportView.CoverageView view = ManagementReportView.Coverage(
            new ReportingEvidenceCoverageResponse(
                Utc(8, 16), Utc(8, 23), Utc(8, 1), CoverageComplete: false));

        view.State.Should().NotBe(ManagementReportView.CoverageState.Complete);
        view.ZeroIsMeasured.Should().BeFalse();
    }

    private static DateTimeOffset Utc(int month, int day) =>
        new(2026, month, day, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HasEvidence_IsFalseForACompletelyEmptyWindow()
    {
        ManagementReportView.HasEvidence(Empty()).Should().BeFalse();
    }

    [Fact]
    public void HasEvidence_IsTrueWhenASingleProtectiveCountIsPresent()
    {
        // A window whose only content is a prevented duplicate still has evidence in it, and must
        // not be replaced by the "no persisted evidence" empty state.
        ManagementReportResponse report = Empty() with
        {
            OperationalWorkflow = Empty().OperationalWorkflow with { DuplicateCreatePrevented = 1 }
        };

        ManagementReportView.HasEvidence(report).Should().BeTrue();
    }

    [Fact]
    public void Attention_IsEmptyWhenNothingNeedsReview()
    {
        ManagementReportView.Attention(Empty()).Should().BeEmpty();
    }

    [Fact]
    public void Attention_PutsReconciliationFirstAndMarksItUrgent()
    {
        ManagementReportResponse report = Empty() with
        {
            OperationalWorkflow = Empty().OperationalWorkflow with
            {
                ReconciliationRequired = 2,
                DuplicateCreatePrevented = 9
            },
            SecurityAndQuality = Empty().SecurityAndQuality with { ConcurrencyConflicts = 4 }
        };

        IReadOnlyList<ManagementReportView.AttentionItem> items = ManagementReportView.Attention(report);

        items[0].Tone.Should().Be(ManagementReportView.AttentionTone.Urgent);
        items[0].Count.Should().Be(2);

        // Most urgent first, so the first thing read is the thing that needs someone. The enum is
        // ordered Guarded < Notable < Urgent, so severity order is descending by value.
        items.Select(item => (int)item.Tone).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Attention_TreatsAPreventionAsAGuardRatherThanAFault()
    {
        // A stopped duplicate is a safety mechanism doing its job. Presenting it beside failures
        // would teach the reader to treat a correct outcome as an incident.
        ManagementReportResponse report = Empty() with
        {
            OperationalWorkflow = Empty().OperationalWorkflow with
            {
                SourceChangedPrevented = 3,
                ClosedOrMissingPrevented = 1,
                DuplicateCreatePrevented = 2
            }
        };

        ManagementReportView.Attention(report)
            .Should().OnlyContain(item => item.Tone == ManagementReportView.AttentionTone.Guarded)
            .And.HaveCount(3);
    }

    [Fact]
    public void Attention_QuotesTheBackendCountUnchanged()
    {
        // Every item must be one server count with a "greater than zero" threshold. A reader who
        // clicks through has to find exactly the number quoted.
        ManagementReportResponse report = Empty() with
        {
            OperationalWorkflow = Empty().OperationalWorkflow with { Failures = 7 }
        };

        ManagementReportView.Attention(report).Single().Count.Should().Be(7);
    }

    [Fact]
    public void Attention_SurfacesAnUnresolvedRetryWithoutCallingItAFailure()
    {
        ManagementReportResponse report = Empty() with
        {
            OperationalWorkflow = Empty().OperationalWorkflow with
            {
                Retries = new RetryOutcomeMetricsResponse(5, 3, 1, 1)
            }
        };

        ManagementReportView.AttentionItem item = ManagementReportView.Attention(report).Single();

        item.Count.Should().Be(1);
        item.Tone.Should().Be(ManagementReportView.AttentionTone.Notable);
    }

    [Theory]
    [InlineData("IdentityLookup")]
    [InlineData("Access")]
    [InlineData("OperationalRecordJira")]
    public void WorkflowLabel_TranslatesEveryServerCategory(string name)
    {
        ManagementReportView.WorkflowLabel(name).Should().NotBe(name);
    }

    [Fact]
    public void WorkflowLabel_LeavesAnUnknownCategoryAsItIs()
    {
        ManagementReportView.WorkflowLabel("SomethingNew").Should().Be("SomethingNew");
    }

    [Fact]
    public void LimitationLabel_TranslatesEveryLimitationTheProjectorSends()
    {
        // These five are the complete set in ManagementReportProjector. Any one left untranslated
        // would put an English sentence on a Turkish management screen.
        string[] codes =
        [
            "RateLimitRejectionsUnavailable",
            "AccessVersionConflictHistoryUnavailable",
            "OperationalSourceOutageHistoryUnavailable",
            "BulkIdentityInvalidItemHistoryUnavailable",
            "DuplicateCreatePreventionHistoryIncomplete",
            "ElapsedDurationsNotActiveEffort",
            "HistoryBeforePersistenceUnavailable"
        ];

        foreach (string code in codes)
        {
            string label = ManagementReportView.LimitationLabel(new DataLimitationResponse(code, null));
            label.Should().NotBe(code);
            label.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void LimitationLabel_IgnoresTheServerMessageForAKnownCode()
    {
        // message is fallback presentation text and must never drive what is shown for a code the
        // UI already translates; otherwise a backend wording change silently rewrites the screen.
        ManagementReportView
            .LimitationLabel(new DataLimitationResponse("ElapsedDurationsNotActiveEffort", "English text"))
            .Should().NotBe("English text");
    }

    [Fact]
    public void LimitationLabel_FallsBackToTheServerMessageForAnUnknownCode()
    {
        ManagementReportView
            .LimitationLabel(new DataLimitationResponse("SomethingNew", "Server-supplied fallback."))
            .Should().Be("Server-supplied fallback.");
    }

    [Fact]
    public void LimitationLabel_FallsBackToTheCodeWhenThereIsNoMessage()
    {
        // Failing towards the server's own identifier is the safe direction: a raw code on screen is
        // a blemish, a dropped limitation is a false statement about the data.
        ManagementReportView
            .LimitationLabel(new DataLimitationResponse("SomethingNew", null))
            .Should().Be("SomethingNew");
    }

    [Theory]
    [InlineData(AuditActions.AccessRequested)]
    [InlineData(AuditActions.AccessApproved)]
    [InlineData(AuditActions.AccessRejected)]
    [InlineData(AuditActions.AccessDisabled)]
    [InlineData(AuditActions.RoleAssigned)]
    [InlineData(AuditActions.RoleRemoved)]
    public void AccessActionLabel_TranslatesEveryActionTheReportSends(string action)
    {
        // These are the exact six the projector emits in accessRequestActivity. A missing one would
        // surface a raw audit action code on a management screen.
        ManagementReportView.AccessActionLabel(action).Should().NotBe(action);
    }

    [Fact]
    public void HasTrend_IsFalseWhenEveryDayIsZero()
    {
        ManagementReportResponse report = Empty() with
        {
            IdentityLookup = Empty().IdentityLookup with
            {
                Trend =
                [
                    new IdentityLookupTrendPointResponse(new DateOnly(2026, 8, 22), 0, 0, 0, 0, 0, 0),
                    new IdentityLookupTrendPointResponse(new DateOnly(2026, 8, 23), 0, 0, 0, 0, 0, 0)
                ]
            }
        };

        ManagementReportView.HasTrend(report).Should().BeFalse();
    }

    private static ManagementReportResponse Empty() => new(
        new ReportingWindowResponse(
            "7d",
            new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)),
        new IdentityLookupMetricsResponse(0, 0, 0, 0, 0, 0, 0, []),
        new OperationalWorkflowMetricsResponse(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            new RetryOutcomeMetricsResponse(0, 0, 0, 0),
            [
                new DurationStatisticsResponse(
                    "importToPreview", "First persisted import to first persisted preview", 0, null, null, null),
                new DurationStatisticsResponse(
                    "claimToJiraCreation", "Workflow claim to durable Jira issue-key persistence", 0, null, null, null),
                new DurationStatisticsResponse(
                    "claimToCompletion", "Workflow claim to durable workflow completion", 0, null, null, null)
            ]),
        new PlatformAdoptionMetricsResponse(0, 0, 0, 0, [], []),
        new SessionGovernanceMetricsResponse(0, 0, 0, 0, 0, 0, 0),
        new SecurityQualityMetricsResponse(0, 0, 0, 0, null),
        ["Rate-limit rejections are not currently persisted as audit events; this metric is unavailable."],
        new ReportingEvidenceCoverageResponse(
            new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            CoverageComplete: true),
        [new DataLimitationResponse("RateLimitRejectionsUnavailable", null)]);
}
