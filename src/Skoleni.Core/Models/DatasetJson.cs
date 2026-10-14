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

    public static Dataset Deserialize(Stream json) =>
        JsonSerializer.Deserialize(json, Default.Dataset) ?? throw new InvalidDataException("dataset.json je prázdný");
}
