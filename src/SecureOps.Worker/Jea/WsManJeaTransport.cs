using System.Management.Automation;
using System.Management.Automation.Remoting;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Options;

namespace SecureOps.Worker.Jea;

/// <summary>Worker JEA runner settings (<c>JeaRunner</c> section). The endpoint and function are fixed in <see cref="JeaEndpointCatalog"/>, not here.</summary>
public sealed class JeaRunnerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "JeaRunner";

    /// <summary><c>WsMan</c> (default, Windows only) or <c>Fake</c> (deterministic, Development/Demo/Test only; never contacts a server).</summary>
    public string Transport { get; set; } = JeaTransportKinds.WsMan;

    /// <summary>WinRM over HTTPS (default, docs/agent-guides/030). False uses HTTP with Kerberos message encryption.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>0 = WinRM default for the scheme (5986 HTTPS, 5985 HTTP).</summary>
    public int Port { get; set; }
}

/// <summary>Known transport kinds.</summary>
public static class JeaTransportKinds
{
    /// <summary>Real WinRM client (System.Management.Automation).</summary>
    public const string WsMan = "WsMan";

    /// <summary>Deterministic fake (<see cref="FakeJeaRunner"/>).</summary>
    public const string Fake = "Fake";
}

/// <summary>
/// The real transport: a remote runspace over <see cref="WSManConnectionInfo"/> whose shell URI is the catalog's session configuration
/// (the <c>-ConfigurationName</c> equivalent), Kerberos only, no credential object — the Worker service identity (the read gMSA,
/// ADR-0024 R2-1) authenticates, so no password exists anywhere. The command is one <see cref="PowerShell.AddCommand(string)"/> with typed
/// <see cref="PowerShell.AddParameter(string, object)"/> values, which is also all a <c>NoLanguage</c> JEA session accepts. Not run locally:
/// no server is contacted by tests (Windows/TEST acceptance pending).
/// </summary>
public sealed class WsManJeaTransport(IOptions<JeaRunnerOptions> options) : IJeaTransport
{
    private const string _shellPrefix = "http://schemas.microsoft.com/powershell/";

    /// <inheritdoc />
    public async Task<IJeaSession> OpenAsync(string computerName, JeaEndpoint endpoint, TimeSpan openTimeout, CancellationToken cancellationToken)
    {
        JeaRunnerOptions settings = options.Value;
        if (!OperatingSystem.IsWindows())
        {
            throw new JeaTransportException(JeaInvocationStatus.Unreachable, "wsmanNotAvailable");
        }

        if (endpoint.ConfigurationName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
        {
            throw new JeaCommandRejectedException("defaultEndpoint");
        }

        int port = settings.Port is > 0 and <= 65535 ? settings.Port : settings.UseSsl ? 5986 : 5985;
        var connection = new WSManConnectionInfo(settings.UseSsl, computerName, port, "/wsman", _shellPrefix + endpoint.ConfigurationName, (PSCredential?)null)
        {
            AuthenticationMechanism = AuthenticationMechanism.Kerberos,
            OpenTimeout = Milliseconds(openTimeout),
            // The runner fences the operation itself; bound what a server may send back (well above any accepted document).
            MaximumReceivedDataSizePerCommand = 8 * 1024 * 1024,
            MaximumReceivedObjectSize = 8 * 1024 * 1024,
            NoMachineProfile = true,
            SkipCACheck = false,
            SkipCNCheck = false,
            SkipRevocationCheck = false
        };
        Runspace runspace = RunspaceFactory.CreateRunspace(connection);
        try
        {
            await Task.Run(runspace.Open, cancellationToken);
            return new Session(runspace);
        }
        catch (PSRemotingTransportException failure)
        {
            runspace.Dispose();
            throw new JeaTransportException(JeaFailureClassifier.ClassifyOpen(failure.ErrorCode), "winrm:" + failure.ErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            runspace.Dispose();
            throw;
        }
    }

    private static int Milliseconds(TimeSpan value) => (int)Math.Clamp(value.TotalMilliseconds, 1_000, int.MaxValue);

    private sealed class Session(Runspace runspace) : IJeaSession
    {
        private PowerShell? _shell;

        public async Task<IReadOnlyList<object?>> InvokeAsync(JeaCommand command, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            _shell = PowerShell.Create();
            _shell.Runspace = runspace;
            _shell.AddCommand(command.Endpoint.FunctionName);
            foreach (KeyValuePair<string, object> parameter in command.Parameters)
            {
                if (parameter.Value is true)
                {
                    _shell.AddParameter(parameter.Key);
                }
                else
                {
                    _shell.AddParameter(parameter.Key, parameter.Value);
                }
            }

            using CancellationTokenRegistration stop = cancellationToken.Register(() => _shell.BeginStop(null, null));
            try
            {
                PSDataCollection<PSObject> output = await _shell.InvokeAsync();
                if (_shell.HadErrors)
                {
                    throw new JeaTransportException(JeaInvocationStatus.Malformed, "functionError");
                }

                return [.. output.Select(o => o?.BaseObject)];
            }
            catch (PSRemotingTransportException failure)
            {
                throw new JeaTransportException(JeaFailureClassifier.ClassifyInvoke(failure.ErrorCode), "winrm:" + failure.ErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        public ValueTask DisposeAsync()
        {
            _shell?.Dispose();
            runspace.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// WinRM / Win32 error code → status. Codes are the documented WinRM client values; the mapping is unverified against real servers
/// (Windows/TEST acceptance) and anything unknown keeps the conservative default of its stage.
/// </summary>
public static class JeaFailureClassifier
{
    /// <summary>ERROR_ACCESS_DENIED.</summary>
    internal const int AccessDenied = 5;

    /// <summary>SEC_E_LOGON_DENIED.</summary>
    internal const int LogonDenied = unchecked((int)0x8009030C);

    /// <summary>SEC_E_NO_CREDENTIALS.</summary>
    internal const int NoCredentials = unchecked((int)0x8009030E);

    /// <summary>SEC_E_TARGET_UNKNOWN (no SPN for the name).</summary>
    internal const int TargetUnknown = unchecked((int)0x80090303);

    /// <summary>ERROR_WSMAN_INVALID_RESOURCE_URI: the session configuration (shell URI) is not registered.</summary>
    internal const int InvalidResourceUri = unchecked((int)0x8033805B);

    /// <summary>ERROR_WSMAN_OPERATION_TIMEDOUT.</summary>
    internal const int OperationTimedOut = unchecked((int)0x80338029);

    /// <summary>ERROR_WINRM_CANNOT_CONNECT / client cannot connect to the destination.</summary>
    internal const int CannotConnect = unchecked((int)0x80338012);

    /// <summary>Failure while opening the session: unknown codes count as no usable WinRM answer.</summary>
    public static JeaInvocationStatus ClassifyOpen(int errorCode) => errorCode switch
    {
        InvalidResourceUri => JeaInvocationStatus.NoEndpoint,
        AccessDenied or LogonDenied or NoCredentials or TargetUnknown => JeaInvocationStatus.Forbidden,
        CannotConnect or OperationTimedOut => JeaInvocationStatus.Unreachable,
        _ => JeaInvocationStatus.Unreachable
    };

    /// <summary>Failure after the session opened: a timeout is <see cref="JeaInvocationStatus.Timeout"/>, anything else no valid answer.</summary>
    public static JeaInvocationStatus ClassifyInvoke(int errorCode) => errorCode switch
    {
        OperationTimedOut => JeaInvocationStatus.Timeout,
        AccessDenied => JeaInvocationStatus.Forbidden,
        _ => JeaInvocationStatus.Malformed
    };
}
