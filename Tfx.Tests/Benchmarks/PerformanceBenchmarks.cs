using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Tfx.Tests.Benchmarks;

/// <summary>
/// Informational micro-benchmarks for hot paths. These are NOT assertions —
/// they print timings and always pass. Use them to compare against rolling
/// baselines on the same machine when investigating performance regressions
/// (see <c>docs/contributing.md</c> §"Bench / performance probes").
/// </summary>
public class PerformanceBenchmarks
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarks(ITestOutputHelper output)
    {
        _output = output;
    }

    private void Run(string label, int iterations, Action body)
    {
        // Warm up: discard the first run so JIT and cache effects don't skew.
        body();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            body();
        }
        sw.Stop();
        var perIter = sw.Elapsed.TotalMilliseconds / iterations;
        _output.WriteLine($"{label,-50} {iterations,8} x  {perIter,9:0.000} ms/iter  ({sw.ElapsedMilliseconds} ms total)");
    }

    [Fact]
    public void ArchivePath_TryParse_1k()
    {
        const string sample = @"C:\foo\bar.zip::docs/sub/readme.txt";
        Run("ArchivePath.TryParse", 1000, () =>
        {
            ArchivePath.TryParse(sample, out _, out _);
        });
    }

    [Fact]
    public void ArchivePath_GetParent_1k()
    {
        const string sample = @"C:\foo\bar.zip::docs/sub/";
        Run("ArchivePath.GetParent", 1000, () =>
        {
            ArchivePath.GetParent(sample);
        });
    }

    [Fact]
    public void CsvParser_Parse_Small()
    {
        var text = BuildCsv(rows: 50, cols: 5);
        Run($"CsvParser.Parse 50x5", 100, () =>
        {
            CsvParser.Parse(text);
        });
    }

    [Fact]
    public void CsvParser_Parse_Medium()
    {
        var text = BuildCsv(rows: 1000, cols: 10);
        Run($"CsvParser.Parse 1000x10", 20, () =>
        {
            CsvParser.Parse(text);
        });
    }

    [Fact]
    public void CsvParser_Parse_Large()
    {
        var text = BuildCsv(rows: 5000, cols: 20);
        Run($"CsvParser.Parse 5000x20", 5, () =>
        {
            CsvParser.Parse(text);
        });
    }

    [Fact]
    public void JsonPrettyPrinter_Small()
    {
        var text = BuildJson(depth: 3, fanOut: 4);
        Run($"JsonPrettyPrinter depth=3 fanOut=4", 50, () =>
        {
            JsonPrettyPrinter.TryPrettyPrint(text);
        });
    }

    [Fact]
    public void JsonPrettyPrinter_Medium()
    {
        var text = BuildJson(depth: 4, fanOut: 8);
        Run($"JsonPrettyPrinter depth=4 fanOut=8", 20, () =>
        {
            JsonPrettyPrinter.TryPrettyPrint(text);
        });
    }

    private static string BuildCsv(int rows, int cols)
    {
        var sb = new StringBuilder();
        for (var c = 0; c < cols; c++)
        {
            if (c > 0) sb.Append(',');
            sb.Append("col").Append(c);
        }
        sb.AppendLine();

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append('r').Append(r).Append('c').Append(c);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildJson(int depth, int fanOut)
    {
        var sb = new StringBuilder();
        AppendJsonNode(sb, depth, fanOut);
        return sb.ToString();
    }

    private static void AppendJsonNode(StringBuilder sb, int depth, int fanOut)
    {
        if (depth == 0)
        {
            sb.Append('"').Append("leaf").Append('"');
            return;
        }
        sb.Append('{');
        for (var i = 0; i < fanOut; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append("k").Append(i).Append("\":");
            AppendJsonNode(sb, depth - 1, fanOut);
        }
        sb.Append('}');
    }
}
