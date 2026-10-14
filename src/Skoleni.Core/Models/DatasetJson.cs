using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skoleni.Core.Models;

/// <summary>
/// Serializace vyčištěného datasetu. Source-generated (bez reflexe), aby fungovala i v Blazor WASM po trimování.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dataset))]
public sealed partial class DatasetJsonContext : JsonSerializerContext
{
    public static string Serialize(Dataset ds) => JsonSerializer.Serialize(ds, Default.Dataset);

    /// <summary>V prohlížeči (Blazor WASM, .NET 10) je tělo odpovědi HttpClient čitelné jen asynchronně – synchronní čtení vyhodí výjimku.</summary>
    public static async Task<Dataset> DeserializeAsync(Stream json, CancellationToken ct = default) =>
        await JsonSerializer.DeserializeAsync(json, Default.Dataset, ct) ?? throw new InvalidDataException("dataset.json je prázdný");

    public static Dataset Deserialize(Stream json) =>
        JsonSerializer.Deserialize(json, Default.Dataset) ?? throw new InvalidDataException("dataset.json je prázdný");
}
