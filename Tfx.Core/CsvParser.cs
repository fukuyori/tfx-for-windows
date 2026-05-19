namespace Tfx;

/// <summary>
/// RFC 4180-flavoured CSV / TSV parser.
///
/// Recognises double-quoted fields (including embedded delimiters, escaped <c>""</c>
/// quotes, and line breaks inside quotes). Recognises <c>\r\n</c>, <c>\n</c>, and a
/// lone <c>\r</c> as record terminators. The number of columns is taken from the
/// first record; later records are padded with empty cells or truncated to match,
/// so consumers can render an aligned table without checking each row's length.
/// </summary>
public static class CsvParser
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text, char delimiter = ',')
    {
        using var _ = PerformanceTrace.Begin($"CsvParser.Parse(len={text.Length})");
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text))
        {
            return rows;
        }

        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                i++;
                continue;
            }

            if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                i++;
                continue;
            }

            if (c == '\r' || c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();

                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i += 2;
                }
                else
                {
                    i++;
                }
                continue;
            }

            field.Append(c);
            i++;
        }

        // Trailing field / row (no terminator at EOF).
        if (field.Length > 0 || row.Count > 0 || inQuotes)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        if (rows.Count == 0)
        {
            return rows;
        }

        var width = rows[0].Count;
        foreach (var r in rows)
        {
            while (r.Count < width)
            {
                r.Add("");
            }
            if (r.Count > width)
            {
                r.RemoveRange(width, r.Count - width);
            }
        }

        return rows;
    }

    public static char DetectDelimiter(string extension) =>
        string.Equals(extension, ".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
}
