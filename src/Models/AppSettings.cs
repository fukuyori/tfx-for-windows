namespace Tfx;

public enum ViewMode
{
    Details,
    Icons
}

public sealed class AppSettings
{
    public ViewMode ViewMode { get; set; } = ViewMode.Details;

    public string LeftPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string RightPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string ActivePane { get; set; } = "Left";
    public bool ShowSplit { get; set; } = true;
    public bool ShowPreview { get; set; } = true;
    public bool ShowHidden { get; set; }
    public bool RenderMarkdownHtml { get; set; } = true;
    public bool ShowPerformanceLogs { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 760;
    public bool IsMaximized { get; set; }
    public double SidebarWidth { get; set; } = 260;
    public double PreviewWidth { get; set; } = 320;
    public double LeftPaneRatio { get; set; } = 0.5;
    public List<string> PinnedFolders { get; set; } = [];
    public List<string> VisibleFileColumns { get; set; } =
    [
        "Name",
        "DateModified",
        "Type",
        "Size",
        "DateCreated",
        "Owner",
        "Attribute"
    ];
    public List<string> FileColumnOrder { get; set; } = [];
}
