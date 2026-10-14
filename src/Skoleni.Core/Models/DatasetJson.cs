using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skoleni.Core.Models;

/// <summary>
/// Serialisation of the cleaned dataset. Source-generated (no reflection) so it keeps working in Blazor WASM after trimming.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dataset))]
public sealed partial class DatasetJsonContext : JsonSerializerContext
{
    public static string Serialize(Dataset ds) => JsonSerializer.Serialize(ds, Default.Dataset);

    /// <summary>In the browser (Blazor WASM, .NET 10) the HttpClient response body can only be read asynchronously - synchronous reads throw.</summary>
    public static async Task<Dataset> DeserializeAsync(Stream json, CancellationToken ct = default) =>
        await JsonSerializer.DeserializeAsync(json, Default.Dataset, ct) ?? throw new InvalidDataException("dataset.json is empty");

    public static Dataset Deserialize(Stream json) =>
        JsonSerializer.Deserialize(json, Default.Dataset) ?? throw new InvalidDataException("dataset.json is empty");
}
