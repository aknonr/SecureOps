namespace SecureOps.Shared.Configuration;

/// <summary>Configuration for the authenticated operator Swagger surface.</summary>
public sealed class SwaggerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Swagger";

    /// <summary>Whether Swagger is explicitly enabled in the current environment.</summary>
    public bool Enabled { get; set; }
}
