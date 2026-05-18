using Xunit;

namespace Tfx.Tests;

public class CsvParserTests
{
    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(CsvParser.Parse(""));
    }

    [Fact]
    public void Parse_SingleField_OneRowOneCell()
    {
        var rows = CsvParser.Parse("hello");
        Assert.Single(rows);
        Assert.Single(rows[0]);
        Assert.Equal("hello", rows[0][0]);
    }

    [Fact]
    public void Parse_SimpleHeaderAndRow_PadsAndSplits()
    {
        var rows = CsvParser.Parse("a,b,c\n1,2,3");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
    }

    [Theory]
    [InlineData("a,b,c\n1,2,3")]
    [InlineData("a,b,c\r\n1,2,3")]
    [InlineData("a,b,c\r1,2,3")]
    public void Parse_HandlesAllLineEndings(string input)
    {
        var rows = CsvParser.Parse(input);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
    }

    [Fact]
    public void Parse_QuotedFieldWithComma()
    {
        var rows = CsvParser.Parse("a,b\n\"x,y\",z");
        Assert.Equal(2, rows.Count);
        Assert.Equal("x,y", rows[1][0]);
        Assert.Equal("z", rows[1][1]);
    }

    [Fact]
    public void Parse_EscapedQuoteInsideQuoted()
    {
        var rows = CsvParser.Parse("a\n\"she said \"\"hi\"\"\"");
        Assert.Equal(2, rows.Count);
        Assert.Equal("she said \"hi\"", rows[1][0]);
    }

    [Fact]
    public void Parse_NewlineInsideQuoted()
    {
        var rows = CsvParser.Parse("a,b\n\"line1\nline2\",c");
        Assert.Equal(2, rows.Count);
        Assert.Equal("line1\nline2", rows[1][0]);
        Assert.Equal("c", rows[1][1]);
    }

    [Fact]
    public void Parse_ShortRow_PaddedToHeaderWidth()
    {
        var rows = CsvParser.Parse("a,b,c\n1,2");
        Assert.Equal(3, rows[1].Count);
        Assert.Equal("", rows[1][2]);
    }

    [Fact]
    public void Parse_LongRow_TruncatedToHeaderWidth()
    {
        var rows = CsvParser.Parse("a,b\n1,2,3,4");
        Assert.Equal(2, rows[1].Count);
        Assert.Equal("1", rows[1][0]);
        Assert.Equal("2", rows[1][1]);
    }

    [Fact]
    public void Parse_TabDelimitedWithTabArg()
    {
        var rows = CsvParser.Parse("a\tb\n1\t2", delimiter: '\t');
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "1", "2" }, rows[1]);
    }

    [Fact]
    public void Parse_TrailingNewline_DoesNotEmitEmptyRow()
    {
        var rows = CsvParser.Parse("a,b\n1,2\n");
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Parse_EmptyFieldsAreKept()
    {
        var rows = CsvParser.Parse("a,b,c\n,,");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "", "", "" }, rows[1]);
    }

    [Theory]
    [InlineData(".tsv", '\t')]
    [InlineData(".TSV", '\t')]
    [InlineData(".csv", ',')]
    [InlineData(".txt", ',')]
    [InlineData("", ',')]
    public void DetectDelimiter_ReturnsTabOnlyForTsv(string extension, char expected)
    {
        Assert.Equal(expected, CsvParser.DetectDelimiter(extension));
    }
}

public class JsonPrettyPrinterTests
{
    [Fact]
    public void TryPrettyPrint_Null_ReturnsNull()
    {
        Assert.Null(JsonPrettyPrinter.TryPrettyPrint(null));
    }

    [Fact]
    public void TryPrettyPrint_Empty_ReturnsNull()
    {
        Assert.Null(JsonPrettyPrinter.TryPrettyPrint(""));
        Assert.Null(JsonPrettyPrinter.TryPrettyPrint("   "));
    }

    [Fact]
    public void TryPrettyPrint_Invalid_ReturnsNull()
    {
        Assert.Null(JsonPrettyPrinter.TryPrettyPrint("{ not valid"));
    }

    [Fact]
    public void TryPrettyPrint_CompactObject_AddsIndentation()
    {
        var result = JsonPrettyPrinter.TryPrettyPrint("{\"a\":1,\"b\":2}");
        Assert.NotNull(result);
        Assert.Contains("\n", result);
        Assert.Contains("  \"a\":", result);
    }

    [Fact]
    public void TryPrettyPrint_AllowsTrailingCommas()
    {
        var result = JsonPrettyPrinter.TryPrettyPrint("{\"a\":1,}");
        Assert.NotNull(result);
    }

    [Fact]
    public void TryPrettyPrint_AllowsComments()
    {
        var result = JsonPrettyPrinter.TryPrettyPrint("// comment\n{\"a\":1}");
        Assert.NotNull(result);
    }
}
