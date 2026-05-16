using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow : Window
{
    public ObservableCollection<FileItem> LeftItems { get; } = [];
    public ObservableCollection<FileItem> RightItems { get; } = [];

    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];
    private readonly ObservableCollection<string> _pinned = [];
    private readonly List<FileColumnDefinition> _fileColumns = [];
    private readonly string _settingsPath;
    private readonly Brush _activeBrush = new SolidColorBrush(Color.FromRgb(30, 37, 43));
    private readonly Brush _inactiveBrush = new SolidColorBrush(Color.FromRgb(23, 27, 31));

    private AppSettings _settings = new();
    private DataGrid _activeGrid;
    private Popup? _columnsPopup;
    private StackPanel? _columnsPanel;
    private DateTime _columnsClosedAt = DateTime.MinValue;
    private string _leftPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _rightPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string[] _cutBuffer = [];
    private Point _dragStart;
    private FileItem? _pendingFileDragItem;
    private string[] _pendingFileDragPaths = [];
    private bool _isRubberBandSelecting;
    private Point _rubberBandStart;
    private FrameworkElement? _rubberBandSource;
    private DataGrid? _rubberBandGrid;
    private ListBox? _rubberBandListBox;
    private bool _syncingSelection;
    private bool _suspendSettingsSave;
    private bool _syncingFolderTree;
    private string? _leftPendingSelectionName;
    private string? _rightPendingSelectionName;
    private CancellationTokenSource? _leftReloadCts;
    private CancellationTokenSource? _rightReloadCts;
    private CancellationTokenSource? _previewCts;
    private string? _archiveTempRoot;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        DataContext = this;
        _activeGrid = LeftGrid;
        ApplyLocalization();

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tfx");
        Directory.CreateDirectory(appData);
        _settingsPath = Path.Combine(appData, "settings.json");

        LoadSettings();
        InitializeFileColumns();
        LoadPinned();
        FolderTree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(FolderTree_Expanded));
        LoadDrives();

        _suspendSettingsSave = true;
        var initial = ResolveInitialPath();
        Navigate(LeftGrid, initial, false);
        Navigate(RightGrid, _settings.RightPath, false);
        ApplyLayoutSettings();
        UpdateActivePane(string.Equals(_settings.ActivePane, "Right", StringComparison.OrdinalIgnoreCase) && _settings.ShowSplit ? RightGrid : LeftGrid);
        QueueFolderTreeSyncToActivePane();
        _suspendSettingsSave = false;

        InitializeAutoRefresh();
    }

    private string ResolveInitialPath()
    {
        var args = Environment.GetCommandLineArgs().Skip(1);
        var firstDirectoryArg = args.FirstOrDefault(Directory.Exists);
        if (!string.IsNullOrWhiteSpace(firstDirectoryArg))
        {
            return Path.GetFullPath(firstDirectoryArg);
        }

        if (TryGetMeaningfulWorkingDirectory(out var cwd))
        {
            return cwd;
        }

        if (IsPathRestorable(_settings.LeftPath))
        {
            return _settings.LeftPath;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static bool TryGetMeaningfulWorkingDirectory(out string path)
    {
        path = "";
        try
        {
            var cwd = Environment.CurrentDirectory;
            if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
            {
                return false;
            }

            var normalizedCwd = NormalizeDirectory(cwd);
            var exePath = Environment.ProcessPath;
            var exeDir = !string.IsNullOrWhiteSpace(exePath)
                ? Path.GetDirectoryName(exePath)
                : AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(exeDir) &&
                string.Equals(normalizedCwd, NormalizeDirectory(exeDir), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var systemDir = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.System));
            var windowsDir = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.Equals(normalizedCwd, systemDir, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedCwd, windowsDir, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = Path.GetFullPath(cwd);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private void ApplyLocalization()
    {
        BackButton.ToolTip = Loc.T("Back (Ctrl+[)");
        ForwardButton.ToolTip = Loc.T("Forward (Ctrl+])");
        ParentButton.ToolTip = Loc.T("Up (Ctrl+Up / Backspace)");
        OpenFolderButton.ToolTip = Loc.T("Open folder");
        TogglePinButton.ToolTip = Loc.T("Pin / unpin current folder");
        ActiveHeaderPathBorder.ToolTip = Loc.T("Edit current path (Ctrl+L)");
        FocusSearchButton.ToolTip = Loc.T("Focus search (Ctrl+F)");
        ViewModeButton.ToolTip = Loc.T("Switch view mode");
        HiddenButton.ToolTip = Loc.T("Toggle hidden files (Ctrl+Shift+.)");
        TerminalButton.ToolTip = Loc.T("Open Terminal here (Ctrl+Shift+T)");
        ExplorerButton.ToolTip = Loc.T("Reveal in Explorer");
        SelectAllButton.ToolTip = Loc.T("Select all (Ctrl+A)");
        ReloadButton.ToolTip = Loc.T("Reload (Ctrl+R)");
        PreviewButton.ToolTip = Loc.T("Toggle preview");
        RenderedToggle.ToolTip = Loc.T("Show rendered Markdown / HTML");
        SplitButton.ToolTip = Loc.T("Toggle split pane");
        ColumnsButton.ToolTip = Loc.T("Columns");
        PinnedHeader.Text = Loc.T("PINNED");
        FoldersHeader.Text = Loc.T("FOLDERS");
        PreviewHeader.Text = Loc.T("PREVIEW");
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
            }
        }
        catch
        {
            _settings = new AppSettings();
        }

        if (!IsPathRestorable(_settings.LeftPath))
        {
            _settings.LeftPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (!IsPathRestorable(_settings.RightPath))
        {
            _settings.RightPath = _settings.LeftPath;
        }
    }

    private static bool IsPathRestorable(string path)
    {
        if (ArchivePath.TryParse(path, out var archive, out _))
        {
            return File.Exists(archive);
        }
        return Directory.Exists(path);
    }

    private void SaveSettings()
    {
        if (_suspendSettingsSave)
        {
            return;
        }

        _settings.LeftPath = _leftPath;
        _settings.RightPath = _rightPath;
        _settings.ActivePane = _activeGrid == RightGrid ? "Right" : "Left";
        var showPreview = PreviewColumn.Width.Value > 0;
        var showSplit = RightPaneColumn.Width.Value > 0;
        _settings.ShowPreview = showPreview;
        _settings.ShowSplit = showSplit;
        _settings.ShowHidden = ShowHidden;
        _settings.PinnedFolders = _pinned.ToList();
        _settings.VisibleFileColumns = _fileColumns
            .Where(column => column.Left.Visibility == Visibility.Visible)
            .Select(column => column.Id)
            .ToList();
        _settings.FileColumnOrder = _fileColumns.Select(c => c.Id).ToList();
        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;
        _settings.Width = bounds.Width;
        _settings.Height = bounds.Height;
        _settings.IsMaximized = WindowState == WindowState.Maximized;
        _settings.SidebarWidth = SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : SidebarColumn.Width.Value;
        if (showPreview)
        {
            _settings.PreviewWidth = PreviewColumn.ActualWidth > 0 ? PreviewColumn.ActualWidth : PreviewColumn.Width.Value;
        }

        var totalPaneWidth = LeftPaneColumn.ActualWidth + RightPaneColumn.ActualWidth;
        if (showSplit && totalPaneWidth > 0)
        {
            _settings.LeftPaneRatio = Math.Clamp(LeftPaneColumn.ActualWidth / totalPaneWidth, 0.15, 0.85);
        }

        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }

    private bool ShowHidden
    {
        get => _settings.ShowHidden;
        set => _settings.ShowHidden = value;
    }

    private void ApplyLayoutSettings()
    {
        if (_settings.Width > 0)
        {
            Width = _settings.Width;
        }

        if (_settings.Height > 0)
        {
            Height = _settings.Height;
        }

        if (!double.IsNaN(_settings.Left) && !double.IsNaN(_settings.Top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.Left;
            Top = _settings.Top;
        }

        if (_settings.SidebarWidth >= SidebarColumn.MinWidth)
        {
            SidebarColumn.Width = new GridLength(_settings.SidebarWidth);
        }

        SetSplitVisible(_settings.ShowSplit);
        SetPreviewVisible(_settings.ShowPreview);
        if (_settings.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
        HiddenButton.IsChecked = _settings.ShowHidden;
        ApplyColumnVisibility();
        ApplyColumnOrder();
        ApplyViewMode();
    }

    private void UpdateStatus()
    {
        var grid = _activeGrid;
        var path = GetCurrentPath(grid);
        var source = ItemsOf(PaneOf(grid));
        var totalCount = source.Count(i => !i.IsParent);
        var selected = ActiveSelectedItems().Where(i => !i.IsParent).ToList();

        if (selected.Count == 0)
        {
            SetStatus(Loc.F("{0}  {1} items", path, totalCount));
        }
        else
        {
            var totalSize = selected.Where(i => !i.IsDirectory).Sum(i => i.Size);
            var sizeText = totalSize > 0 ? $" ({FileItem.FormatSize(totalSize)})" : "";
            SetStatus(Loc.F("{0}  {1} of {2} selected{3}", path, selected.Count, totalCount, sizeText));
        }

        FreeSpaceText.Text = GetFreeSpaceText(path);
    }

    private static string GetFreeSpaceText(string path)
    {
        try
        {
            if (ArchivePath.TryParse(path, out var archive, out _))
            {
                path = archive;
            }

            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                return "";
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return "";
            }

            return Loc.F("{0}  {1} free of {2}", drive.Name, FileItem.FormatSize(drive.AvailableFreeSpace), FileItem.FormatSize(drive.TotalSize));
        }
        catch
        {
            return "";
        }
    }

    private IEnumerable<FileItem> ActiveSelectedItems()
    {
        var icons = _settings.ViewMode == ViewMode.Icons;
        return icons
            ? IconViewOf(ActivePane).SelectedItems.OfType<FileItem>()
            : SelectedItems(_activeGrid);
    }

    private string GetCurrentPath(DataGrid grid) => PathOf(PaneOf(grid));

    private IEnumerable<FileItem> SelectedItems(DataGrid grid) => grid.SelectedItems.Cast<FileItem>();

    private void SetStatus(string text) => StatusText.Text = text;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
        DisposeAutoRefresh();
        CleanupArchiveTemp();
    }

    private void CleanupArchiveTemp()
    {
        if (string.IsNullOrEmpty(_archiveTempRoot))
        {
            return;
        }
        try
        {
            Directory.Delete(_archiveTempRoot, recursive: true);
        }
        catch
        {
        }
    }
}
