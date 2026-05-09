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
    private bool _syncingSelection;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        DataContext = this;
        _activeGrid = LeftGrid;

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tfx");
        Directory.CreateDirectory(appData);
        _settingsPath = Path.Combine(appData, "settings.json");

        LoadSettings();
        InitializeFileColumns();
        LoadPinned();
        FolderTree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(FolderTree_Expanded));
        LoadDrives();

        var initial = ResolveInitialPath();
        Navigate(LeftGrid, initial, false);
        Navigate(RightGrid, _settings.RightPath, false);
        ApplyLayoutSettings();
        UpdateActivePane(LeftGrid);
    }

    private string ResolveInitialPath()
    {
        var args = Environment.GetCommandLineArgs().Skip(1);
        var firstDirectoryArg = args.FirstOrDefault(Directory.Exists);
        if (!string.IsNullOrWhiteSpace(firstDirectoryArg))
        {
            return Path.GetFullPath(firstDirectoryArg);
        }

        if (Directory.Exists(_settings.LeftPath))
        {
            return _settings.LeftPath;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
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

        if (!Directory.Exists(_settings.LeftPath))
        {
            _settings.LeftPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (!Directory.Exists(_settings.RightPath))
        {
            _settings.RightPath = _settings.LeftPath;
        }
    }

    private void SaveSettings()
    {
        _settings.LeftPath = _leftPath;
        _settings.RightPath = _rightPath;
        _settings.ShowPreview = PreviewColumn.Width.Value > 0;
        _settings.ShowSplit = RightPaneColumn.Width.Value > 0;
        _settings.ShowHidden = ShowHidden;
        _settings.PinnedFolders = _pinned.ToList();
        _settings.VisibleFileColumns = _fileColumns
            .Where(column => column.Left.Visibility == Visibility.Visible)
            .Select(column => column.Id)
            .ToList();
        _settings.FileColumnOrder = _fileColumns.Select(c => c.Id).ToList();
        _settings.Width = Width;
        _settings.Height = Height;

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

        SetSplitVisible(_settings.ShowSplit);
        SetPreviewVisible(_settings.ShowPreview);
        HiddenButton.IsChecked = _settings.ShowHidden;
        ApplyColumnVisibility();
        ApplyColumnOrder();
        ApplyViewMode();
    }

    private void UpdateStatus()
    {
        var grid = _activeGrid;
        var path = GetCurrentPath(grid);
        var source = grid == LeftGrid ? LeftItems : RightItems;
        var totalCount = source.Count(i => !i.IsParent);
        var selected = ActiveSelectedItems().Where(i => !i.IsParent).ToList();

        if (selected.Count == 0)
        {
            SetStatus($"{path}  {totalCount} items");
        }
        else
        {
            var totalSize = selected.Where(i => !i.IsDirectory).Sum(i => i.Size);
            var sizeText = totalSize > 0 ? $" ({FileItem.FormatSize(totalSize)})" : "";
            SetStatus($"{path}  {selected.Count} of {totalCount} selected{sizeText}");
        }

        FreeSpaceText.Text = GetFreeSpaceText(path);
    }

    private static string GetFreeSpaceText(string path)
    {
        try
        {
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

            return $"{drive.Name}  {FileItem.FormatSize(drive.AvailableFreeSpace)} free of {FileItem.FormatSize(drive.TotalSize)}";
        }
        catch
        {
            return "";
        }
    }

    private IEnumerable<FileItem> ActiveSelectedItems()
    {
        var icons = _settings.ViewMode == ViewMode.Icons;
        var listBox = _activeGrid == LeftGrid ? LeftIconView : RightIconView;
        return icons
            ? listBox.SelectedItems.OfType<FileItem>()
            : SelectedItems(_activeGrid);
    }

    private string GetCurrentPath(DataGrid grid) => grid == LeftGrid ? _leftPath : _rightPath;

    private IEnumerable<FileItem> SelectedItems(DataGrid grid) => grid.SelectedItems.Cast<FileItem>();

    private void SetStatus(string text) => StatusText.Text = text;

    private void Window_Closing(object? sender, CancelEventArgs e) => SaveSettings();
}
