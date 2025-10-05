using System.Text.Json.Serialization;
using DoorbellSvc.Protocol;

namespace DoorbellSvc.Core;

/// <summary>
///     JSON serialization context for AOT compilation
/// </summary>
[JsonSerializable(typeof(DoorbellMessage))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class DoorbellJsonContext : JsonSerializerContext
{
}