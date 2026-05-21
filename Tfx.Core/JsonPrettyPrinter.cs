using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tfx;

public static class JsonPrettyPrinter
{
    // Default `JsonSerializerOptions.Encoder` (`JavaScriptEncoder.Default`)
    // escapes every non-ASCII code point as `\uXXXX`, which means a JSON
    // file containing Japanese stored as `ス...` round-trips back to the
    // same escape sequences instead of rendering the actual characters.
    // `UnsafeRelaxedJsonEscaping` only escapes the bare-minimum control
    // characters and the JSON-syntactic ones, letting CJK / Latin-with-
    // diacritics / emoji etc. appear as-is. The "unsafe" name refers to HTML
    // injection in `<script>` blocks, which is not applicable here — we
    // display the output inside a read-only `TextBox`, not inside a web page.
    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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

        using var _ = PerformanceTrace.Begin($"JsonPrettyPrinter(len={text.Length})");
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            return JsonSerializer.Serialize(document.RootElement, PrettyOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
