using System.Management.Automation;
using System.Security.Cryptography;
using System.Text;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>Bounded error metadata only; never exception messages, target objects or script text.</summary>
public sealed record SccmFailureEvidence(string Stage, string Command, string ErrorId, string ErrorIdHash,
    string Category, string ExceptionType, int HResult, string? InnerExceptionType, int? InnerHResult,
    int? ScriptLineNumber, int? OffsetInLine, string InvokedCommand)
{
    /// <summary>Projects a PowerShell failure without serializing its potentially sensitive body.</summary>
    public static SccmFailureEvidence Capture(string stage, Exception exception, ErrorRecord? record)
    {
        string id = record?.FullyQualifiedErrorId ?? "";
        string code = id.Split(',')[0];
        string safeId = code is "Modules_ModuleNotFound" or "Modules_InvalidManifest" or "Modules_ModuleFileNotFound"
            or "UnauthorizedAccess" or "PSSecurityException" or "DriveNotFound" or "ProviderNotFound"
            or "PathNotFound" or "CommandNotFoundException" or "ItemNotFound" ? code : "Unclassified";
        Exception detail = record?.Exception ?? exception;
        return new(stage, stage switch
        {
            "ImportModule" => "Import-Module",
            "CreateSiteDrive" => "New-PSDrive",
            "SelectSiteDrive" => "Set-Location",
            "ReadCollection" => "Get-CMDevice | Select-Object",
            _ => "Runspace.Open"
        }, safeId, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))),
            record?.CategoryInfo.Category.ToString() ?? "NotSpecified", detail.GetType().Name, detail.HResult,
            detail.InnerException?.GetType().Name, detail.InnerException?.HResult,
            record?.InvocationInfo?.ScriptLineNumber, record?.InvocationInfo?.OffsetInLine,
            record?.InvocationInfo?.MyCommand?.Name is "Import-Module" or "New-PSDrive" or "Set-Location"
                or "Get-CMDevice" or "Select-Object" ? record.InvocationInfo.MyCommand.Name : "NotExposed");
    }
}

/// <summary>Read-only collection diagnostic; contains counts and error metadata, never device names.</summary>
public sealed record SccmCollectionDiagnostic(string PowerShellVersion, string PowerShellAssemblySha256, string Runtime, string Architecture,
    bool SmsAdminUiPathPresent, bool LegacyModuleFilePresent, string State, int? DeviceCount,
    bool? Complete, SccmFailureEvidence? Failure);
