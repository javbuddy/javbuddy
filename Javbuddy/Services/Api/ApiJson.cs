using System.Text.Json;
using System.Text.Json.Serialization;

namespace Javbuddy.Services.Api;

/// <summary>The one JSON shape of the public API (REST bodies and MCP tool results): camelCase, enums as camelCase strings, nulls kept.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
