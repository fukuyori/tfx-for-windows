using Xunit;

namespace Tfx.Tests;

public class GitStatusParserTests
{
    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        var r = GitStatusParser.Parse("C:\\repo", "");
        Assert.Empty(r.Files);
        Assert.Null(r.Branch);
        Assert.Equal("C:\\repo", r.Root);
    }

    [Fact]
    public void Parse_BranchHeader()
    {
        var output = "# branch.oid abc123\n# branch.head main\n# branch.upstream origin/main\n# branch.ab +0 -0\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Equal("main", r.Branch);
    }

    [Fact]
    public void Parse_DetachedHeadIgnored()
    {
        var output = "# branch.head (detached)\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Null(r.Branch);
    }

    [Fact]
    public void Parse_OrdinaryModified_WorktreeWins()
    {
        // "1 .M N... 100644 100644 100644 abc def src/file.cs"
        var output = "1 .M N... 100644 100644 100644 abcdef abcdef src/file.cs\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Equal(GitFileStatus.Modified, r.Files["src/file.cs"]);
    }

    [Fact]
    public void Parse_OrdinaryStagedAdded_UsesX()
    {
        var output = "1 A. N... 100644 100644 100644 abcdef abcdef src/new.cs\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Equal(GitFileStatus.Added, r.Files["src/new.cs"]);
    }

    [Fact]
    public void Parse_Untracked()
    {
        var output = "? untracked.txt\n? path/with/space file.txt\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Equal(GitFileStatus.Untracked, r.Files["untracked.txt"]);
        Assert.Equal(GitFileStatus.Untracked, r.Files["path/with/space file.txt"]);
    }

    [Fact]
    public void Parse_Ignored()
    {
        var output = "! bin/Debug/output.dll\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Equal(GitFileStatus.Ignored, r.Files["bin/Debug/output.dll"]);
    }

    [Fact]
    public void Parse_Unmerged()
    {
        var output = "u UU N... 100644 100644 100644 100644 a b c conflict.txt\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Equal(GitFileStatus.Conflicted, r.Files["conflict.txt"]);
    }

    [Fact]
    public void Parse_Renamed_UsesNewPath()
    {
        // type 2 has Xnnn field (e.g. R100) then "newpath\toldpath"
        var output = "2 R. N... 100644 100644 100644 abcdef abcdef R100 src/new.cs\tsrc/old.cs\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Equal(GitFileStatus.Renamed, r.Files["src/new.cs"]);
        Assert.False(r.Files.ContainsKey("src/old.cs"));
    }

    [Fact]
    public void Parse_MultipleEntries()
    {
        var output = string.Join("\n",
            "# branch.head feature/x",
            "1 .M N... 100644 100644 100644 a b src/a.cs",
            "1 A. N... 100644 100644 100644 c d src/b.cs",
            "? newfile.txt",
            ""
        );
        var r = GitStatusParser.Parse("/r", output);
        Assert.Equal("feature/x", r.Branch);
        Assert.Equal(3, r.Files.Count);
        Assert.Equal(GitFileStatus.Modified, r.Files["src/a.cs"]);
        Assert.Equal(GitFileStatus.Added, r.Files["src/b.cs"]);
        Assert.Equal(GitFileStatus.Untracked, r.Files["newfile.txt"]);
    }

    [Fact]
    public void Parse_PathWithSpaces()
    {
        var output = "1 .M N... 100644 100644 100644 a b src/file with spaces.cs\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.True(r.Files.ContainsKey("src/file with spaces.cs"));
    }

    [Fact]
    public void DirectoryBadges_TrackedChangeMarksAncestors()
    {
        var output = "1 .M N... 100644 100644 100644 a b src/deep/inner/file.cs\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Equal("M", r.DirectoryBadges["src"]);
        Assert.Equal("M", r.DirectoryBadges["src/deep"]);
        Assert.Equal("M", r.DirectoryBadges["src/deep/inner"]);
        Assert.False(r.DirectoryBadges.ContainsKey("src/deep/inner/file.cs"));
    }

    [Fact]
    public void DirectoryBadges_UntrackedOnlyIsQuestionMark_TrackedWins()
    {
        var output =
            "? docs/new.txt\n" +
            "1 .M N... 100644 100644 100644 a b docs/api/changed.md\n";
        var r = GitStatusParser.Parse("/r", output);
        // docs contains both an untracked file and (via docs/api) a tracked
        // change — the tracked change wins.
        Assert.Equal("M", r.DirectoryBadges["docs"]);
        Assert.Equal("M", r.DirectoryBadges["docs/api"]);
    }

    [Fact]
    public void DirectoryBadges_UntrackedOnly()
    {
        var output = "? docs/new.txt\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Equal("?", r.DirectoryBadges["docs"]);
    }

    [Fact]
    public void DirectoryBadges_IgnoredFilesExcluded()
    {
        var output = "! bin/out.dll\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.False(r.DirectoryBadges.ContainsKey("bin"));
    }

    [Fact]
    public void DirectoryBadges_RootLevelFileAddsNoDirectories()
    {
        var output = "1 .M N... 100644 100644 100644 a b rootfile.cs\n";
        var r = GitStatusParser.Parse("/r", output);
        Assert.Empty(r.DirectoryBadges);
    }

    [Theory]
    [InlineData(GitFileStatus.Modified, "M")]
    [InlineData(GitFileStatus.Added, "A")]
    [InlineData(GitFileStatus.Deleted, "D")]
    [InlineData(GitFileStatus.Renamed, "R")]
    [InlineData(GitFileStatus.Copied, "C")]
    [InlineData(GitFileStatus.Untracked, "?")]
    [InlineData(GitFileStatus.Ignored, "!")]
    [InlineData(GitFileStatus.Conflicted, "U")]
    [InlineData(GitFileStatus.Unmodified, "")]
    public void Badge_ReturnsExpectedChar(GitFileStatus s, string expected)
    {
        Assert.Equal(expected, GitStatusParser.Badge(s));
    }
}
