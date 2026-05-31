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
    // Per-pane tab paths + active index (additive; empty lists fall back to
    // LeftPath / RightPath so older settings.json files keep working).
    public List<string> LeftTabs { get; set; } = [];
    public List<string> RightTabs { get; set; } = [];
    public int LeftActiveTab { get; set; }
    public int RightActiveTab { get; set; }
    public List<string> PinnedFolders { get; set; } = [];
    public string TerminalCommand { get; set; } = "";
    public string TerminalArguments { get; set; } = "";

    // Built-in terminal pane (§2.9). Visibility + layout persist across runs.
    public bool ShowTerminalPane { get; set; }
    public double TerminalPaneHeight { get; set; } = 220;
    public double TerminalPaneFontSize { get; set; } = 14;

    // ─── PDF preview safety ──────────────────────────────────────────
    // See docs / CHANGELOG for the threat model. Defaults are chosen so a
    // freshly installed tfx never opts into the higher-risk paths
    // (in-process shell thumbnailer / unbounded files) without explicit
    // consent.
    public bool EnablePdfPreview { get; set; } = true;
    public string PdfRendererPath { get; set; } = "";
    // Default true: the in-proc Windows shell PDF thumbnailer is the same code
    // path Explorer runs every time the user browses a folder containing PDFs.
    // Disabling it inside tfx while leaving it active in Explorer offers no real
    // attack-surface reduction (the user is already exposed through Explorer)
    // and breaks tfx's ability to preview PDFs it has not seen before. Users
    // who want to opt out can still set this to false explicitly.
    public bool AllowShellPdfThumbnail { get; set; } = true;
    public long PdfPreviewMaxBytes { get; set; } = 500L * 1024 * 1024;
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
