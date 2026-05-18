using System.Text.Json;

namespace Tfx;

public static class JsonPrettyPrinter
{
    /// <summary>
    /// Returns the pretty-printed JSON representation of <paramref name="text"/>,
    /// or <c>null</c> if the input is not valid JSON.
    /// </summary>
    public static string? TryPrettyPrint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
