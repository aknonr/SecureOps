namespace SecureOps.Tests.Integration;

/// <summary>Runs Windows DPAPI acceptance only where the native protection provider exists.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    /// <summary>Never skips on Windows; native failures must fail the test.</summary>
    public WindowsFactAttribute()
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
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows DPAPI; no portable substitute validates persistent Windows keys.";
        }
    }
}
