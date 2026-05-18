using Xunit;

namespace Tfx.Tests;

public class ArchivePathTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(@"C:\foo\bar.txt", false)]
    [InlineData(@"C:\foo\bar.zip", false)]
    [InlineData(@"C:\foo\bar.zip::", true)]
    [InlineData(@"C:\foo\bar.zip::docs/", true)]
    [InlineData(@"C:\foo\bar.zip::docs/readme.txt", true)]
    public void Contains_DetectsSeparator(string? path, bool expected)
    {
        Assert.Equal(expected, ArchivePath.Contains(path));
    }

    [Fact]
    public void TryParse_ReturnsFalseForRegularPath()
    {
        var ok = ArchivePath.TryParse(@"C:\foo\bar.zip", out var archive, out var inner);
        Assert.False(ok);
        Assert.Equal("", archive);
        Assert.Equal("", inner);
    }

    [Fact]
    public void TryParse_ReturnsFalseForNull()
    {
        var ok = ArchivePath.TryParse(null, out var archive, out var inner);
        Assert.False(ok);
        Assert.Equal("", archive);
        Assert.Equal("", inner);
    }

    [Theory]
    [InlineData(@"C:\foo\bar.zip::", @"C:\foo\bar.zip", "")]
    [InlineData(@"C:\foo\bar.zip::docs/", @"C:\foo\bar.zip", "docs/")]
    [InlineData(@"C:\foo\bar.zip::docs/readme.txt", @"C:\foo\bar.zip", "docs/readme.txt")]
    [InlineData(@"C:\foo\bar.zip::docs\sub\readme.txt", @"C:\foo\bar.zip", "docs/sub/readme.txt")]
    public void TryParse_SplitsArchiveAndInner(string input, string expectedArchive, string expectedInner)
    {
        var ok = ArchivePath.TryParse(input, out var archive, out var inner);
        Assert.True(ok);
        Assert.Equal(expectedArchive, archive);
        Assert.Equal(expectedInner, inner);
    }

    [Theory]
    [InlineData(@"C:\foo\bar.zip", "", @"C:\foo\bar.zip::")]
    [InlineData(@"C:\foo\bar.zip", "docs/", @"C:\foo\bar.zip::docs/")]
    [InlineData(@"C:\foo\bar.zip", "/docs/", @"C:\foo\bar.zip::docs/")]
    [InlineData(@"C:\foo\bar.zip", @"docs\sub\readme.txt", @"C:\foo\bar.zip::docs/sub/readme.txt")]
    public void Combine_BuildsArchivePath(string archive, string inner, string expected)
    {
        Assert.Equal(expected, ArchivePath.Combine(archive, inner));
    }

    [Theory]
    [InlineData(@"C:\foo\bar.zip::", true)]
    [InlineData(@"C:\foo\bar.zip::docs/", false)]
    [InlineData(@"C:\foo\bar.zip", false)]
    [InlineData(null, false)]
    public void IsArchiveRoot_TrueOnlyWhenInnerEmpty(string? path, bool expected)
    {
        Assert.Equal(expected, ArchivePath.IsArchiveRoot(path));
    }

    [Theory]
    [InlineData(@"C:\foo\bar.zip::docs/sub/", @"C:\foo\bar.zip::docs/")]
    [InlineData(@"C:\foo\bar.zip::docs/", @"C:\foo\bar.zip::")]
    [InlineData(@"C:\foo\bar.zip::", @"C:\foo")]
    public void GetParent_WalksUp(string input, string expected)
    {
        Assert.Equal(expected, ArchivePath.GetParent(input));
    }

    [Fact]
    public void GetParent_ReturnsNullForNonArchivePath()
    {
        Assert.Null(ArchivePath.GetParent(@"C:\foo\bar"));
    }

    [Theory]
    [InlineData(@"C:\foo\bar.zip", true)]
    [InlineData(@"C:\foo\bar.ZIP", true)]
    [InlineData(@"C:\foo\bar.Zip", true)]
    [InlineData(@"C:\foo\bar.txt", false)]
    [InlineData(@"C:\foo\bar", false)]
    [InlineData(@"C:\foo\bar.zipx", false)]
    public void IsZipFile_MatchesZipExtensionCaseInsensitively(string path, bool expected)
    {
        Assert.Equal(expected, ArchivePath.IsZipFile(path));
    }
}
