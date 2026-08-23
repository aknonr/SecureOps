using System.Text.Json;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Reads JSON without accepting an unbounded dependency response.</summary>
internal static class BoundedJsonHttpContent
{
    public static async Task<JsonDocument> ReadAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("Dependency response exceeded the configured limit.");
        }

        await using Stream source = await content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream destination = new(Math.Min(maximumBytes, 81_920));
        byte[] buffer = new byte[16_384];
        int total = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException("Dependency response exceeded the configured limit.");
            }

            destination.Write(buffer, 0, read);
        }

        destination.Position = 0;
        return await JsonDocument.ParseAsync(
            destination,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 },
            cancellationToken);
    }
}

/// <summary>Preserves the reviewed legacy property casing instead of web camel-casing.</summary>
internal static class LegacyContractJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null
    };
}
