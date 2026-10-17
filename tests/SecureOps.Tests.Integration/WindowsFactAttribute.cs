namespace SecureOps.Tests.Integration;

/// <summary>Runs Windows DPAPI acceptance only where the native protection provider exists.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    /// <summary>Never skips on Windows; native failures must fail the test.</summary>
    public WindowsFactAttribute([System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null, [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows DPAPI; no portable substitute validates persistent Windows keys.";
        }
    }
}

/// <summary>Runs Windows DPAPI data cases without hiding failures on Windows.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    /// <summary>Never skips on Windows.</summary>
    public WindowsTheoryAttribute([System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null, [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows DPAPI; no portable substitute validates persistent Windows keys.";
        }
    }
}
