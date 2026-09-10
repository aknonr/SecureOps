using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.OperationalRecords;

if (args.Length is not (5 or 7) || args[4] != "--collect" || (args.Length == 7 && args[5] != "--rfc-contract") || !OperatingSystem.IsWindows())
{
    Console.WriteLine("Usage (approved TEST Windows host only): InUseEvidence <server-config.json> <source-id> <approved-dictionary.json> <new-private-output.json> --collect [--rfc-contract <approved-rfc-contract.json>]");
    return 2;
}
try
{
    if (!Path.IsPathFullyQualified(args[0]) || !Path.IsPathFullyQualified(args[2]) || !Path.IsPathFullyQualified(args[3])
        || new FileInfo(args[2]).Length > 4096 || File.Exists(args[3]))
    { return 2; }
    byte[] dictionaryBytes = await File.ReadAllBytesAsync(args[2]);
    Dictionary<string, string> dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(dictionaryBytes)
        ?? throw new InvalidDataException();
    InUseReferencedRequestContract? referenced = null;
    byte[]? referenceBytes = null;
    if (args.Length == 7)
    {
        if (!Path.IsPathFullyQualified(args[6]) || new FileInfo(args[6]).Length > 4096)
        { return 2; }
        referenceBytes = await File.ReadAllBytesAsync(args[6]);
        referenced = JsonSerializer.Deserialize<InUseReferencedRequestContract>(referenceBytes) ?? throw new InvalidDataException();
    }
    IConfiguration configuration = new ConfigurationBuilder().AddJsonFile(args[0], optional: false).Build();
    if (configuration["OperationalRecords:SourceProvider"] != "TuruncuHat"
        || !Uri.TryCreate(configuration["TuruncuHat:BaseUrl"], UriKind.Absolute, out Uri? uri)
        || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
    { return 2; }
    var services = new ServiceCollection();
    services.AddSingleton(configuration);
    services.AddLogging(logging => logging.ClearProviders());
    services.AddSecureOpsInfrastructure(configuration);
    using ServiceProvider provider = services.BuildServiceProvider();
    var client = (TuruncuHatOperationalRecordClient)provider.GetRequiredService<IOperationalRecordClient>();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    // Reserve output before any read. A failure leaves a bounded local attempt, never a successful report.
    using var output = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write, FileShare.None);
    using var actor = WindowsIdentity.GetCurrent();
    byte[] attempt = JsonSerializer.SerializeToUtf8Bytes(new { Status = "StartedNotCompleted", ActorSid = actor.User?.Value, At = DateTimeOffset.UtcNow });
    await output.WriteAsync(attempt, timeout.Token);
    output.Flush(true);
    JsonElement evidence = await client.DiagnoseAsync(args[1], dictionary, timeout.Token, referenced);
    byte[] result = JsonSerializer.SerializeToUtf8Bytes(new
    {
        Status = "CollectedNotMapped",
        ActorSid = actor.User?.Value,
        At = DateTimeOffset.UtcNow,
        DictionarySha256 = Convert.ToHexString(SHA256.HashData(dictionaryBytes)),
        RfcContractSha256 = referenceBytes is null ? null : Convert.ToHexString(SHA256.HashData(referenceBytes)),
        Evidence = evidence
    });
    output.Position = 0;
    await output.WriteAsync(result, timeout.Token);
    output.SetLength(result.Length);
    output.Flush(true);
    Console.WriteLine("Bounded evidence written. Keep actor provenance local; share only the Evidence property.");
    return 0;
}
catch (Exception)
{
    Console.Error.WriteLine("Collection did not complete. Stop; do not share configuration or raw errors. Review local execution prerequisites with the integration owner.");
    return 1;
}
