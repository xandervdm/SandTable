using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandTable.Api;

internal static class ApiJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };
}
