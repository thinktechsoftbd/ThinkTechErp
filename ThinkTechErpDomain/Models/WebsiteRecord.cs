using System.Text.Json;

namespace ThinkTechErpDomain.Models;

/// <summary>A row from one of the supported website schema tables.</summary>
public sealed class WebsiteRecord : Dictionary<string, JsonElement>
{
    public static WebsiteRecord FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A record must be a JSON object.");
        }

        var record = new WebsiteRecord();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            record[property.Name] = property.Value.Clone();
        }

        return record;
    }
}
