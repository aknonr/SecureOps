namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// PowerShell execution policy is process-wide on Windows. Keep module imports from racing with tests that open
/// restricted runspaces, while preserving their execution policies.
/// </summary>
[CollectionDefinition(nameof(ServiceAccountPowerShellCollection), DisableParallelization = true)]
public sealed class ServiceAccountPowerShellCollection;
