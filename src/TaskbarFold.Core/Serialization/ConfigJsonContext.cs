using System.Text.Json;
using System.Text.Json.Serialization;
using TaskbarFold.Core.Models;

namespace TaskbarFold.Core.Serialization;

/// <summary>
/// Source-generated serializer for the configuration document. Using the generator
/// keeps reflection out of the startup path, which matters because the resident
/// reads the config before it can show anything.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(GroupsDocument))]
[JsonSerializable(typeof(Group))]
[JsonSerializable(typeof(Shortcut))]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class ConfigJsonContext : JsonSerializerContext;
