using System.Diagnostics;

namespace SecureOps.Worker;

/// <summary>
/// Fixed-text lifecycle entries in the Windows Application log under the <c>SecureOps.Worker</c> source. The source is created once by
/// the installer (<c>scripts/worker/Install-SecureOpsWorkerService.ps1</c>, run by an administrator); the Worker never creates it (its
/// service identity may not), and writes nothing when it is missing. Entries carry no exception message, configuration value, server or
/// account name — the same rule as the lifecycle log in the data directory.
/// </summary>
internal sealed class WorkerEventLog
{
    internal const string SourceName = "SecureOps.Worker";

    private readonly bool _enabled;

    private WorkerEventLog(bool enabled) => _enabled = enabled;

    /// <summary>Uses the source only on Windows, in service mode, when it already exists.</summary>
    internal static WorkerEventLog Create(bool service)
    {
        if (!service || !OperatingSystem.IsWindows())
        {
            return new(false);
        }

        try
        {
            return new(EventLog.SourceExists(SourceName));
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or InvalidOperationException or UnauthorizedAccessException)
        {
            // A non-administrative identity may be unable to search every log; then no entry is written.
            return new(false);
        }
    }

    internal void Information(int eventId, string text) => Write(eventId, text, error: false);

    internal void Error(int eventId, string text) => Write(eventId, text, error: true);

    private void Write(int eventId, string text, bool error)
    {
        if (!_enabled || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            EventLog.WriteEntry(SourceName, text, error ? EventLogEntryType.Error : EventLogEntryType.Information, eventId);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The event log is a convenience; the lifecycle log and SCM events stay authoritative.
        }
    }
}
