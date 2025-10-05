using System.Text.Json.Serialization;
using DoorbellSvc.Protocol;

namespace DoorbellSvc.Core;

/// <summary>
///     JSON serialization context for AOT compilation
/// </summary>
[JsonSerializable(typeof(DoorbellMessage))]
[JsonSerializable(typeof(DoorbellResponse))]
[JsonSerializable(typeof(SuccessResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(PingResponse))]
[JsonSerializable(typeof(PrewarmResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class DoorbellJsonContext : JsonSerializerContext
{
}