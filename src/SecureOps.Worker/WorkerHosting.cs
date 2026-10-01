using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Json;

namespace SecureOps.Worker;

internal static class WorkerHosting
{
    internal const string ServiceName = "SecureOps.Worker";

    internal static HostApplicationBuilder CreateBuilder(string[] args, string? baseDirectory = null)
    {
        string[] hostArgs = args.Where(arg => arg is not "--diagnostics" and not "--sccm-diagnostics").ToArray();
        IConfigurationRoot commandLine = new ConfigurationBuilder().AddCommandLine(hostArgs).Build();
        string contentRoot = commandLine[HostDefaults.ContentRootKey] ?? baseDirectory ?? AppContext.BaseDirectory;
        if (!Path.IsPathFullyQualified(contentRoot))
        { throw new InvalidOperationException("WorkerHosting.AbsoluteContentRootRequired"); }
        return Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = hostArgs,
            ContentRootPath = contentRoot
        });
    }

    internal static string? Validate(IConfiguration configuration, bool service)
    {
        if (service && string.IsNullOrWhiteSpace(configuration[HostDefaults.EnvironmentKey]))
        { throw new InvalidOperationException("WorkerHosting.EnvironmentRequired"); }
        string? directory = configuration["WorkerHosting:DataDirectory"];
        if (string.IsNullOrWhiteSpace(directory))
        {
            if (service)
            { throw new InvalidOperationException("WorkerHosting.DataDirectoryRequired"); }
            return null;
        }
        if (!Path.IsPathFullyQualified(directory) || directory.StartsWith(@"\\", StringComparison.Ordinal)
            || !Directory.Exists(directory))
        { throw new InvalidOperationException("WorkerHosting.InvalidDataDirectory"); }
        return Path.GetFullPath(directory);
    }

    internal static FileStream? Acquire(string? directory) => directory is null ? null :
        new FileStream(Path.Combine(directory, ".secureops-worker.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);

    internal static Serilog.Core.Logger? CreateLifecycleLog(string? directory)
    {
        if (directory is null)
        { return null; }
        string path = Path.Combine(directory, "worker.log");
        // Fail before hosting if the configured log cannot be opened. Never probe from diagnostics.
        using (new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
        { }
        return new LoggerConfiguration().WriteTo.File(new JsonFormatter(), path,
            fileSizeLimitBytes: 10 * 1024 * 1024, rollOnFileSizeLimit: true,
            retainedFileCountLimit: 14, buffered: false).CreateLogger();
    }
}
