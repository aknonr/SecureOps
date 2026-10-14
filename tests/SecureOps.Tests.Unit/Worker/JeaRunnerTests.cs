using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SecureOps.Worker.Jea;

namespace SecureOps.Tests.Unit.Worker;

/// <summary>
/// The generic JEA runner (TARAMA-C B4): the allow-list is the only way to build a command (AGENTS.md rules 1 and 3), the runner enforces
/// both timeouts, classifies failures per stage, requires exactly one text output and always closes the session. A fake transport stands in
/// for WinRM; no server, endpoint or network is contacted.
/// </summary>
public sealed class JeaRunnerTests
{
    private static readonly JeaInvocationLimits _limits = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300), 1024);

    [Fact]
    public void Catalog_BuildsTheUsageCommand_WithTypedValuesInCatalogOrder()
    {
        JeaCommand command = Usage(new() { ["AsJson"] = true, ["Account"] = new[] { "SYN\\svc_a", "svc_b" }, ["ExpectedAccount"] = "SYN\\gmsa_a$" });

        command.Endpoint.ConfigurationName.Should().Be("SecureOpsServiceAccountUsage");
        command.Endpoint.FunctionName.Should().Be("Get-SecureOpsAccountUsage");
        command.Parameters.Select(p => p.Key).Should().Equal("Account", "ExpectedAccount", "AsJson");
        command.Parameters[0].Value.Should().BeOfType<string[]>().Which.Should().Equal("SYN\\svc_a", "svc_b");
        command.Parameters[2].Value.Should().Be(true);
        Usage(new() { ["Account"] = new[] { "svc_a" }, ["ExpectedAccount"] = null, ["AsJson"] = true }).Parameters.Select(p => p.Key)
            .Should().Equal("Account", "AsJson");
    }

    [Theory]
    [InlineData("Microsoft.PowerShell", "Get-SecureOpsAccountUsage", "defaultEndpoint")]
    [InlineData("microsoft.powershell32", "Get-SecureOpsAccountUsage", "defaultEndpoint")]
    [InlineData("", "Get-SecureOpsAccountUsage", "defaultEndpoint")]
    [InlineData("SecureOpsRemediation", "Get-SecureOpsAccountUsage", "unknownEndpoint")]
    [InlineData("SecureOpsServiceAccountUsage", "Set-Service", "unknownFunction")]
    [InlineData("SecureOpsServiceAccountUsage", "Stop-Service", "unknownFunction")]
    [InlineData("SecureOpsServiceAccountUsage", "Invoke-Expression", "unknownFunction")]
    [InlineData("SecureOpsServiceAccountUsage", "Get-WebConfigurationProperty", "unknownFunction")]
    [InlineData("SecureOpsServiceAccountUsage", "get-securopsaccountusage", "unknownFunction")]
    public void Catalog_RefusesAnythingButTheOneFunctionOnItsEndpoint(string configuration, string function, string code)
    {
        Action create = () => JeaCommand.Create(configuration, function, new Dictionary<string, object?> { ["Account"] = new[] { "svc_a" }, ["AsJson"] = true });

        create.Should().Throw<JeaCommandRejectedException>().Which.Code.Should().Be(code);
    }

    public static TheoryData<string, object?, string> RejectedValues => new()
    {
        { "ScriptBlock", "Stop-Service x", "unknownParameter" },
        { "Command", "Remove-Item C:\\", "unknownParameter" },
        { "account", new[] { "svc_a" }, "unknownParameter" },
        { "Account", new[] { "svc_a'; Stop-Service x; '" }, "invalidValue" },
        { "Account", new[] { "svc a" }, "invalidValue" },
        { "Account", new[] { "svc_a$(Remove-Item)" }, "invalidValue" },
        { "Account", new[] { "svc_a\n" }, "invalidValue" },
        { "Account", Array.Empty<string>(), "invalidValue" },
        { "Account", Enumerable.Range(1, 21).Select(i => "svc" + i).ToArray(), "invalidValue" },
        { "Account", "svc_a", "invalidValue" },
        { "ExpectedAccount", "SYN\\gmsa_without_dollar", "invalidValue" },
        { "ExpectedAccount", "SYN\\gmsa$\n", "invalidValue" },
        { "AsJson", "true", "invalidValue" },
        { "AsJson", 1, "invalidValue" }
    };

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void Catalog_RefusesUnknownParameters_AndValuesOutsideTheirPattern(string name, object? value, string code)
    {
        Dictionary<string, object?> values = new() { ["Account"] = new[] { "svc_a" }, ["AsJson"] = true };
        values[name] = value;

        ((Action)(() => Usage(values))).Should().Throw<JeaCommandRejectedException>().Which.Code.Should().Be(code);
    }

    [Fact]
    public void Catalog_RequiresTheRequiredParameters()
    {
        ((Action)(() => Usage(new() { ["AsJson"] = true }))).Should().Throw<JeaCommandRejectedException>().Which.Code.Should().Be("missingParameter");
        ((Action)(() => Usage(new() { ["Account"] = new[] { "svc_a" }, ["AsJson"] = false }))).Should().Throw<JeaCommandRejectedException>()
            .Which.Code.Should().Be("missingParameter", "the Worker always asks for JSON text");
    }

    [Fact]
    public void Catalog_HoldsOnlyReadOnlyFunctions_AndACommandCannotBeBuiltAroundIt()
    {
        JeaEndpointCatalog.All.Should().NotBeEmpty();
        foreach (JeaEndpoint endpoint in JeaEndpointCatalog.All)
        {
            JeaEndpointCatalog.ReadOnlyVerbs.Should().Contain(endpoint.FunctionName.Split('-')[0], "AGENTS.md rule 1: no write verb can be called");
            endpoint.ConfigurationName.Should().NotStartWith("Microsoft.");
            endpoint.Parameters.Should().OnlyContain(p => p.Kind == JeaParameterKind.Switch || p.Pattern != null);
        }

        typeof(JeaCommand).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty("only Create can build a command");
        typeof(JeaCommand).GetProperties().Should().OnlyContain(p => p.SetMethod == null || !p.SetMethod.IsPublic);
    }

    [Fact]
    public void WorkerJeaSources_ContainNoScriptTextUnconstrainedRunspaceCredentialOrWriteCommand()
    {
        string folder = Path.Combine(Root(), "src", "SecureOps.Worker", "Jea");
        string[] forbidden = ["AddScript", "Invoke-Expression", "ScriptBlock", "CreateDefault", "CreateRunspace()", "Process.Start", "new PSCredential",
            "SecureString", "Password", "/powershell/Microsoft.", "Set-", "Stop-", "Start-Service", "Restart-", "Remove-", "New-PSSession", "Enter-PSSession",
            "AuthenticationMechanism.Basic", "AuthenticationMechanism.Credssp", "AuthenticationMechanism.Negotiate", "SkipCACheck = true", "SkipCNCheck = true"];
        foreach (string file in Directory.GetFiles(folder, "*.cs"))
        {
            string text = File.ReadAllText(file);
            foreach (string word in forbidden)
            {
                text.Should().NotContain(word, $"{Path.GetFileName(file)} must not build a write command, script text or credential");
            }
        }
    }

    [Fact]
    public async Task Ok_ReturnsTheOneTextValue_AndClosesTheSession()
    {
        FakeTransport transport = new() { Output = ["{\"a\":1}"] };

        JeaInvocationResult result = await Runner(transport).InvokeAsync("syn-app-01.syn.example", Command(), _limits, CancellationToken.None);

        result.Status.Should().Be(JeaInvocationStatus.Ok);
        result.Output.Should().Be("{\"a\":1}");
        transport.Opened.Should().Equal("syn-app-01.syn.example");
        transport.Disposed.Should().Be(1);
        transport.Invoked.Should().ContainSingle().Which.Should().BeSameAs(transport.LastCommand);
    }

    [Theory]
    [InlineData(JeaInvocationStatus.NoEndpoint)]
    [InlineData(JeaInvocationStatus.Forbidden)]
    [InlineData(JeaInvocationStatus.Unreachable)]
    public async Task ClassifiedOpenFailure_IsTheResult(JeaInvocationStatus status)
    {
        FakeTransport transport = new() { OpenFailure = new JeaTransportException(status, "winrm:1") };

        JeaInvocationResult result = await Runner(transport).InvokeAsync("syn-app-01", Command(), _limits, CancellationToken.None);

        result.Should().BeEquivalentTo(new { Status = status, Output = (string?)null, FailureType = "winrm:1" });
        transport.Invoked.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenThatNeverAnswers_IsUnreachable_WithinTheOpenTimeout_AndALateSessionIsClosed()
    {
        TaskCompletionSource<IJeaSession> late = new();
        FakeTransport transport = new() { OpenGate = late.Task };

        JeaInvocationResult result = await Runner(transport).InvokeAsync("syn-app-01", Command(), _limits, CancellationToken.None);

        result.Status.Should().Be(JeaInvocationStatus.Unreachable);
        result.FailureType.Should().Be("openTimeout");
        result.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        FakeSession session = new(transport);
        late.SetResult(session);
        await WaitUntilAsync(() => transport.Disposed == 1);
    }

    [Fact]
    public async Task InvokeThatNeverAnswers_IsTimeout_AndTheSessionIsClosed()
    {
        FakeTransport transport = new() { InvokeGate = new TaskCompletionSource<IReadOnlyList<object?>>().Task };

        JeaInvocationResult result = await Runner(transport).InvokeAsync("syn-app-01", Command(), _limits, CancellationToken.None);

        result.Status.Should().Be(JeaInvocationStatus.Timeout);
        result.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => transport.Disposed == 1);
    }

    public static TheoryData<object?[]> BadOutputs => new()
    {
        Array.Empty<object?>(),
        new object?[] { "{}", "{}" },
        new object?[] { 42 },
        new object?[] { null },
        new object?[] { string.Empty },
        new object?[] { new string('x', 1025) }
    };

    [Theory]
    [MemberData(nameof(BadOutputs))]
    public async Task OutputThatIsNotExactlyOneTextWithinTheLimit_IsMalformed(object?[] output)
    {
        FakeTransport transport = new() { Output = output };

        JeaInvocationResult result = await Runner(transport).InvokeAsync("syn-app-01", Command(), _limits, CancellationToken.None);

        result.Status.Should().Be(JeaInvocationStatus.Malformed);
        result.Output.Should().BeNull();
        transport.Disposed.Should().Be(1);
    }

    [Fact]
    public async Task UnclassifiedFailures_AreUnreachableBeforeOpen_AndMalformedAfter()
    {
        FakeTransport open = new() { OpenFailure = new InvalidOperationException("remote text that must not be kept") };
        FakeTransport invoke = new() { InvokeFailure = new InvalidOperationException("remote text that must not be kept") };

        JeaInvocationResult beforeOpen = await Runner(open).InvokeAsync("syn-app-01", Command(), _limits, CancellationToken.None);
        JeaInvocationResult afterOpen = await Runner(invoke).InvokeAsync("syn-app-01", Command(), _limits, CancellationToken.None);

        beforeOpen.Status.Should().Be(JeaInvocationStatus.Unreachable);
        afterOpen.Status.Should().Be(JeaInvocationStatus.Malformed);
        new[] { beforeOpen.FailureType, afterOpen.FailureType }.Should().OnlyContain(t => t == nameof(InvalidOperationException), "the type, never the message");
        invoke.Disposed.Should().Be(1);
    }

    [Theory]
    [InlineData("syn app 01")]
    [InlineData("syn-app-01;calc")]
    [InlineData("http://syn-app-01")]
    [InlineData("-syn")]
    [InlineData("")]
    public async Task ATargetThatIsNotAHostName_IsNeverContacted(string computerName)
    {
        FakeTransport transport = new() { Output = ["{}"] };

        JeaInvocationResult result = await Runner(transport).InvokeAsync(computerName, Command(), _limits, CancellationToken.None);

        result.Status.Should().Be(JeaInvocationStatus.Unreachable);
        transport.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_ThrowsPromptly_AndClosesTheSession()
    {
        FakeTransport transport = new() { InvokeGate = new TaskCompletionSource<IReadOnlyList<object?>>().Task };
        JeaInvocationLimits patient = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), 1024);
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(100));

        Func<Task> run = () => Runner(transport).InvokeAsync("syn-app-01", Command(), patient, cancel.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        await WaitUntilAsync(() => transport.Disposed == 1);
    }

    [Theory]
    [InlineData(JeaFailureClassifier.InvalidResourceUri, JeaInvocationStatus.NoEndpoint)]
    [InlineData(JeaFailureClassifier.AccessDenied, JeaInvocationStatus.Forbidden)]
    [InlineData(JeaFailureClassifier.LogonDenied, JeaInvocationStatus.Forbidden)]
    [InlineData(JeaFailureClassifier.NoCredentials, JeaInvocationStatus.Forbidden)]
    [InlineData(JeaFailureClassifier.TargetUnknown, JeaInvocationStatus.Forbidden)]
    [InlineData(JeaFailureClassifier.CannotConnect, JeaInvocationStatus.Unreachable)]
    [InlineData(JeaFailureClassifier.OperationTimedOut, JeaInvocationStatus.Unreachable)]
    [InlineData(53, JeaInvocationStatus.Unreachable)]
    public void OpenErrorCodes_Map(int code, JeaInvocationStatus expected) => JeaFailureClassifier.ClassifyOpen(code).Should().Be(expected);

    [Theory]
    [InlineData(JeaFailureClassifier.OperationTimedOut, JeaInvocationStatus.Timeout)]
    [InlineData(JeaFailureClassifier.AccessDenied, JeaInvocationStatus.Forbidden)]
    [InlineData(1726, JeaInvocationStatus.Malformed)]
    public void InvokeErrorCodes_Map(int code, JeaInvocationStatus expected) => JeaFailureClassifier.ClassifyInvoke(code).Should().Be(expected);

    [Fact]
    public void TransportException_NeverCarriesOk() =>
        new JeaTransportException(JeaInvocationStatus.Ok, "x").Status.Should().Be(JeaInvocationStatus.Malformed);

    [Theory]
    [InlineData("syn-app-01", JeaInvocationStatus.Ok)]
    [InlineData("syn-unreach-01", JeaInvocationStatus.Unreachable)]
    [InlineData("syn-noep-01", JeaInvocationStatus.NoEndpoint)]
    [InlineData("syn-deny-01", JeaInvocationStatus.Forbidden)]
    [InlineData("syn-slow-01", JeaInvocationStatus.Timeout)]
    [InlineData("syn-bad-01", JeaInvocationStatus.Malformed)]
    public async Task FakeRunner_IsDeterministic(string server, JeaInvocationStatus expected)
    {
        FakeJeaRunner fake = new();

        JeaInvocationResult first = await fake.InvokeAsync(server, Command(), _limits, CancellationToken.None);
        JeaInvocationResult second = await fake.InvokeAsync(server, Command(), _limits, CancellationToken.None);

        first.Status.Should().Be(expected);
        second.Should().Be(first);
        (first.Output is null).Should().Be(expected != JeaInvocationStatus.Ok);
    }

    internal static JeaCommand Command() => Usage(new() { ["Account"] = new[] { "SYN\\svc_a" }, ["AsJson"] = true });

    private static JeaCommand Usage(Dictionary<string, object?> values) =>
        JeaCommand.Create(JeaEndpointCatalog.ServiceAccountUsage.ConfigurationName, JeaEndpointCatalog.ServiceAccountUsage.FunctionName, values);

    private static JeaRunner Runner(FakeTransport transport) => new(transport, NullLogger<JeaRunner>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue();
    }

    internal static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FakeTransport : IJeaTransport
    {
        private int _disposed;

        public List<string> Opened { get; } = [];

        public List<JeaCommand> Invoked { get; } = [];

        public JeaCommand? LastCommand { get; set; }

        public IReadOnlyList<object?> Output { get; init; } = ["{}"];

        public Exception? OpenFailure { get; init; }

        public Exception? InvokeFailure { get; init; }

        public Task<IJeaSession>? OpenGate { get; init; }

        public Task<IReadOnlyList<object?>>? InvokeGate { get; init; }

        public int Disposed => Volatile.Read(ref _disposed);

        public void MarkDisposed() => Interlocked.Increment(ref _disposed);

        public async Task<IJeaSession> OpenAsync(string computerName, JeaEndpoint endpoint, TimeSpan openTimeout, CancellationToken cancellationToken)
        {
            lock (Opened)
            {
                Opened.Add(computerName);
            }

            if (OpenFailure is not null)
            {
                throw OpenFailure;
            }

            return OpenGate is null ? new FakeSession(this) : await OpenGate;
        }
    }

    private sealed class FakeSession(FakeTransport transport) : IJeaSession
    {
        public async Task<IReadOnlyList<object?>> InvokeAsync(JeaCommand command, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            transport.Invoked.Add(command);
            transport.LastCommand = command;
            if (transport.InvokeFailure is not null)
            {
                throw transport.InvokeFailure;
            }

            return transport.InvokeGate is null ? transport.Output : await transport.InvokeGate;
        }

        public ValueTask DisposeAsync()
        {
            transport.MarkDisposed();
            return ValueTask.CompletedTask;
        }
    }
}
