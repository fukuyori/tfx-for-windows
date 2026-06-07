using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow : Window
{
    private const int WmSysCommand = 0x0112;
    private const int ScSize = 0xF000;
    // WM_SYSCOMMAND SC_SIZE direction codes (WMSZ_*).
    private const int WmszLeft = 1;
    private const int WmszRight = 2;
    private const int WmszTop = 3;
    private const int WmszTopLeft = 4;
    private const int WmszTopRight = 5;
    private const int WmszBottom = 6;
    private const int WmszBottomLeft = 7;
    private const int WmszBottomRight = 8;

    public ObservableCollection<FileItem> LeftItems { get; } = [];
    public ObservableCollection<FileItem> RightItems { get; } = [];

    private readonly ObservableCollection<string> _pinned = [];
    private readonly List<FileColumnDefinition> _fileColumns = [];
    private readonly string _settingsPath;
    private readonly string _configPath;
    private Brush _activeBrush = new SolidColorBrush(Color.FromRgb(30, 37, 43));
    private Brush _inactiveBrush = new SolidColorBrush(Color.FromRgb(23, 27, 31));

    private AppSettings _settings = new();
    private AppConfig _config = new();
    private readonly Dictionary<string, AppShortcut> _shortcuts = new(StringComparer.OrdinalIgnoreCase);
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
    private bool _suppressNextContextMenu;
    private bool _nativeRightDragInProgress;
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
    private bool _initialLeftFocusDone;
    private readonly StartupOptions _startupOptions;

    public MainWindow(StartupOptions? startup = null)
    {
        _startupOptions = startup ?? new StartupOptions();
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            WindowTheme.Apply(this);
            HookDeviceChange();
        };
        DataContext = this;
        _activeGrid = LeftGrid;
        ApplyLocalization();

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tfx");
        Directory.CreateDirectory(appData);
        _settingsPath = Path.Combine(appData, "settings.json");
        _configPath = Path.Combine(appData, "config.toml");

        LoadSettings();
        LoadConfig();
        ApplyLocalization();
        PerformanceTrace.SetEnabled(_settings.ShowPerformanceLogs);
        InitializeFileColumns();
        LoadPinned();
        FolderTree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(FolderTree_Expanded));
        LoadDrives();

        _suspendSettingsSave = true;
        var initial = ResolveInitialPath(out var explicitLeftStart);
        Navigate(LeftGrid, initial, false);
        Navigate(RightGrid, ResolveInitialRightPath(), false);
        InitializeTabs(explicitLeftStart);
        ApplyLayoutSettings();
        // Always land on the left pane at startup so the user opens onto
        // the left listing with the ".." row preselected (set by Navigate
        // above). The previously-active pane is intentionally not restored.
        UpdateActivePane(LeftGrid);
        QueueFolderTreeSyncToActivePane();
        _suspendSettingsSave = false;

        InitializeAutoRefresh();
        InitializeTerminalPane();

        // Reload from Navigate(LeftGrid, ...) is async; the ApplyPendingSelection
        // path focuses the ".." row when items finish loading. Add a belt-and-
        // braces follow-up at ApplicationIdle so focus is guaranteed to land on
        // the left pane even if WPF was still arranging the window.
        Loaded += (_, _) =>
        {
            if (_config.Errors.Count > 0)
            {
                SetStatus(Loc.F("Config warning: {0}", _config.Errors[0]));
            }
            Dispatcher.BeginInvoke(EnsureInitialLeftFocus, DispatcherPriority.ApplicationIdle);
        };
    }

    private int _initialFocusAttempts;

    private void EnsureInitialLeftFocus()
    {
        // Don't fight the user — if they already moved focus elsewhere, stop.
        if (_activeGrid != LeftGrid)
        {
            return;
        }

        // The initial DirectoryLoader runs on Task.Run; items may still be on
        // the way. Retry a few times before giving up.
        if (LeftGrid.Items.Count == 0)
        {
            if (++_initialFocusAttempts > 20)
            {
                return;
            }
            Dispatcher.BeginInvoke(EnsureInitialLeftFocus, DispatcherPriority.ApplicationIdle);
            return;
        }

        // Select the first row when nothing is selected. That row is "..".
        // if there is one (non-root path), otherwise the topmost entry.
        if (LeftGrid.SelectedItem is null)
        {
            LeftGrid.SelectedIndex = 0;
        }
        FocusPane(Pane.Left);
    }

    /// <summary>
    /// Resolves the left pane's startup folder. <paramref name="explicitStart"/>
    /// is set when the folder came from an explicit source — a command-line path
    /// argument or a meaningful current working directory (e.g. launching
    /// <c>Tfx.exe</c> from a terminal) — as opposed to falling back to the saved
    /// session path. Callers use it to decide whether to honour the startup
    /// folder over restoring the previously saved tab set.
    /// </summary>
    private string ResolveInitialPath(out bool explicitStart)
    {
        explicitStart = true;

        // A folder passed on the command line (supports ~ and %VARS%) wins.
        if (!string.IsNullOrWhiteSpace(_startupOptions.FolderPath)
            && TryResolveStartupFolder(_startupOptions.FolderPath, out var cliFolder))
        {
            return cliFolder;
        }

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

        explicitStart = false;

        if (IsPathRestorable(_settings.LeftPath))
        {
            return _settings.LeftPath;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// Expands a command-line folder argument (<c>~</c>, <c>~/sub</c>, and
    /// <c>%VARS%</c>) and returns its full path when it exists.
    /// </summary>
    private static bool TryResolveStartupFolder(string raw, out string fullPath)
    {
        fullPath = "";
        try
        {
            var expanded = raw.Trim();
            if (expanded == "~" || expanded.StartsWith("~/", StringComparison.Ordinal) || expanded.StartsWith("~\\", StringComparison.Ordinal))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                expanded = expanded.Length <= 1 ? home : Path.Combine(home, expanded[2..]);
            }

            expanded = Environment.ExpandEnvironmentVariables(expanded);
            if (Directory.Exists(expanded))
            {
                fullPath = Path.GetFullPath(expanded);
                return true;
            }
        }
        catch
        {
            // fall through
        }
        return false;
    }

    private string ResolveInitialRightPath()
    {
        if (_config.Startup.Layout == "split")
        {
            var configured = _config.Startup.RightFolders.FirstOrDefault(IsPathRestorable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }
        }

        return _settings.RightPath;
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
        BackButton.ToolTip = Loc.F("Back ({0})", ShortcutText("goBack"));
        ForwardButton.ToolTip = Loc.F("Forward ({0})", ShortcutText("goForward"));
        ParentButton.ToolTip = Loc.F("Up ({0} / Backspace)", ShortcutText("goUp"));
        TogglePinButton.ToolTip = Loc.T("Pin / unpin current folder");
        FocusSearchButton.ToolTip = Loc.F("Focus search ({0})", ShortcutText("focusSearch"));
        ViewModeButton.ToolTip = Loc.T("Switch view mode");
        HiddenButton.ToolTip = Loc.F("Toggle hidden files ({0})", ShortcutText("toggleHidden"));
        TerminalButton.ToolTip = Loc.F("Open Terminal here ({0})", ShortcutText("openTerminal"));
        ReloadButton.ToolTip = Loc.F("Reload ({0})", ShortcutText("reload"));
        FolderTreeButton.ToolTip = Loc.F("Toggle folder tree ({0})", ShortcutText("toggleFolderTree"));
        PreviewButton.ToolTip = Loc.F("Toggle preview ({0})", ShortcutText("togglePreview"));
        RenderedToggle.ToolTip = Loc.F("Show rendered preview ({0})", ShortcutText("toggleRendered"));
        LoadImagesButton.ToolTip = Loc.F("Load external images for this preview ({0})", ShortcutText("loadExternalImages"));
        SplitButton.ToolTip = Loc.F("Toggle split pane ({0})", ShortcutText("toggleSplit"));
        SwapPanesButton.ToolTip = Loc.F("Swap left and right panes ({0})", ShortcutText("swapPanes"));
        ColumnsButton.ToolTip = Loc.T("Columns");
        TerminalPaneButton.ToolTip = Loc.F("Toggle terminal pane ({0})", ShortcutText("toggleTerminal"));
        TerminalInterruptButton.ToolTip = Loc.T("Interrupt (send Ctrl+C)");
        TerminalQuitButton.ToolTip = Loc.T("Quit (send Ctrl+\\)");
        TerminalEofButton.ToolTip = Loc.T("EOF / suspend (send Ctrl+Z)");
        TerminalSyncCwdButton.ToolTip = Loc.T("Set the active file pane to the terminal's current folder");
        TerminalCloseButton.ToolTip = Loc.F("Close terminal ({0})", ShortcutText("toggleTerminal"));
        MinimizeButton.ToolTip = Loc.T("Minimize");
        MaximizeRestoreButton.ToolTip = Loc.T("Maximize / restore");
        CloseButton.ToolTip = Loc.T("Close");
        PinnedHeader.Text = Loc.T("PINNED");
        FoldersHeader.Text = Loc.T("FOLDERS");
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

    private void LoadConfig()
    {
        _config = AppConfig.LoadOrCreate(_configPath);

        if (!string.IsNullOrWhiteSpace(_config.Terminal.Command))
        {
            _settings.TerminalCommand = _config.Terminal.Command;
        }

        if (_config.Terminal.Arguments is not null)
        {
            _settings.TerminalArguments = _config.Terminal.Arguments;
        }

        InitializeShortcuts();
        ApplyConfigTheme();
    }

    private void InitializeShortcuts()
    {
        _shortcuts.Clear();
        foreach (var (action, text) in DefaultShortcutText)
        {
            if (AppShortcut.TryParse(text, out var shortcut, out _))
            {
                _shortcuts[action] = shortcut;
            }
        }

        var seen = _shortcuts.ToDictionary(pair => pair.Value, pair => pair.Key);
        foreach (var (action, shortcut) in _config.Shortcuts)
        {
            if (seen.TryGetValue(shortcut, out var existing) && !existing.Equals(action, StringComparison.OrdinalIgnoreCase))
            {
                _config.Errors.Add($"Shortcut conflict: {action} and {existing} both use {shortcut.DisplayText}");
                continue;
            }
            if (_shortcuts.TryGetValue(action, out var previous))
            {
                seen.Remove(previous);
            }
            _shortcuts[action] = shortcut;
            seen[shortcut] = action;
        }
    }

    private void ApplyConfigTheme()
    {
        if (!string.IsNullOrWhiteSpace(_config.FontUi))
        {
            FontFamily = new FontFamily(_config.FontUi);
        }

        if (_config.FontSize is { } fontSize)
        {
            FontSize = fontSize;
        }

        var backgroundOpacity = OpacityToken("background", 1.0);
        var inactivePaneOpacity = OpacityToken("inactivePane", backgroundOpacity);
        Opacity = 1.0;

        SetResourceBrush("TfxBackground", ColorToken("fileListBackground", "headerBackground", fallback: Color.FromRgb(16, 19, 22)), backgroundOpacity);
        SetResourceBrush("TfxPanel", ColorToken("fileListBackground", fallback: Color.FromRgb(23, 27, 31)), backgroundOpacity);
        SetResourceBrush("TfxPanelActive", ColorToken("titleBarBackgroundActive", "fileListRowSelected", fallback: Color.FromRgb(30, 37, 43)), backgroundOpacity);
        SetResourceBrush("TfxBorder", ColorToken("paneBorderInactive", fallback: Color.FromRgb(45, 53, 60)));
        SetResourceBrush("TfxForeground", ColorToken("fileForeground", fallback: Color.FromRgb(214, 222, 230)));
        SetResourceBrush("TfxMuted", ColorToken("secondaryForeground", "headerForeground", fallback: Color.FromRgb(143, 155, 168)));
        SetResourceBrush("TfxAccent", ColorToken("directoryForeground", "splitHandleActive", fallback: Color.FromRgb(125, 211, 252)));
        SetResourceBrush("TfxFocusBorder", ColorToken("paneBorderKeyboardTarget", "paneBorderActive", fallback: Color.FromRgb(74, 222, 128)));
        SetResourceBrush("TfxChrome", ColorToken("headerBackground", fallback: Color.FromRgb(11, 14, 16)), backgroundOpacity);
        SetResourceBrush("TfxInput", ColorToken("inputBackground", "headerBackground", fallback: Color.FromRgb(13, 16, 19)), backgroundOpacity);

        var hoverColor = ColorToken("fileListRowHovered", "fileListRowDropTarget", fallback: Color.FromRgb(32, 38, 43));
        var selectionColor = ColorToken("fileListRowSelected", fallback: Color.FromRgb(38, 56, 69));
        var selectionForeground = ColorToken("fileListRowSelectedForeground", fallback: ContrastText(selectionColor));

        SetResourceBrush("TfxHover", hoverColor);
        SetResourceBrush("TfxSelection", selectionColor);
        SetResourceBrush("TfxSelectionForeground", selectionForeground);
        SetResourceBrush("TfxInactiveSelection", ColorToken("folderTreeSelectedInactive", fallback: selectionColor));
        SetResourceBrush("TfxDisabledForeground", ColorToken("disabledForeground", "secondaryForeground", fallback: Color.FromRgb(89, 99, 110)));
        SetResourceBrush("TfxScrollThumb", ColorToken("scrollbarThumb", "paneBorderInactive", fallback: Color.FromRgb(58, 68, 77)));
        SetResourceBrush("TfxScrollThumbHover", ColorToken("scrollbarThumbHovered", "paneBorderActive", fallback: Color.FromRgb(74, 86, 97)));
        SetResourceBrush("TfxScrollThumbDragging", ColorToken("scrollbarThumbDragging", "paneBorderKeyboardTarget", fallback: Color.FromRgb(91, 104, 116)));
        SetResourceBrush("TfxAlternatingRow", ColorToken("fileListRowAlternate", "fileListBackground", fallback: Color.FromRgb(20, 25, 29)));
        SetResourceBrush("TfxSelectionOverlay", ColorToken("directoryForeground", "splitHandleActive", fallback: Color.FromRgb(125, 211, 252)), 0.2);
        SetResourceBrush("TfxHitSurface", ColorToken("headerBackground", fallback: Colors.White), 0.01);

        _activeBrush = new SolidColorBrush(ColorToken("titleBarBackgroundActive", fallback: Color.FromRgb(30, 37, 43)))
        {
            Opacity = backgroundOpacity,
        };
        _inactiveBrush = new SolidColorBrush(ColorToken("titleBarBackgroundInactive", fallback: Color.FromRgb(23, 27, 31)))
        {
            Opacity = inactivePaneOpacity,
        };

        WindowTheme.Apply(this);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (IsInteractiveTitleElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        DragMove();
    }

    private static bool IsInteractiveTitleElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or TextBox)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ResizeLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartEdgeResize(WmszLeft, e);
    private void ResizeRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartEdgeResize(WmszRight, e);
    private void ResizeTop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartEdgeResize(WmszTop, e);
    private void ResizeBottom_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartEdgeResize(WmszBottom, e);
    private void ResizeTopLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartEdgeResize(WmszTopLeft, e);
    private void ResizeTopRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartEdgeResize(WmszTopRight, e);
    private void ResizeBottomLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartEdgeResize(WmszBottomLeft, e);
    private void ResizeBottomRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartEdgeResize(WmszBottomRight, e);

    private void StartEdgeResize(int direction, MouseButtonEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        e.Handled = true;
        _ = ReleaseCapture();
        _ = SendMessage(handle, WmSysCommand, new IntPtr(ScSize + direction), IntPtr.Zero);
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private Color ColorToken(string key, string? alternate = null, Color? fallback = null)
    {
        if (_config.Colors.TryGetValue(key, out var color))
        {
            return color;
        }

        if (alternate is not null && _config.Colors.TryGetValue(alternate, out var alternateColor))
        {
            return alternateColor;
        }

        return fallback ?? Colors.Transparent;
    }

    private double OpacityToken(string key, double fallback)
    {
        if (_config.Opacity.TryGetValue(key, out var opacity))
        {
            return Math.Clamp(opacity, 0.0, 1.0);
        }

        return fallback;
    }

    private static Color ContrastText(Color background)
    {
        var luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);
        return luminance > 140 ? Colors.Black : Colors.White;
    }

    private void SetResourceBrush(string resourceKey, Color color, double opacity = 1.0)
    {
        if (Application.Current.Resources[resourceKey] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
            existing.Opacity = opacity;
        }
        else
        {
            Application.Current.Resources[resourceKey] = new SolidColorBrush(color)
            {
                Opacity = opacity,
            };
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

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
        _settings.LeftTabs = _leftTabs.Select(t => t.Path).ToList();
        _settings.RightTabs = _rightTabs.Select(t => t.Path).ToList();
        _settings.LeftActiveTab = _leftActiveTabIndex;
        _settings.RightActiveTab = _rightActiveTabIndex;
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
        var folderTreeVisible = SidebarColumn.Width.Value > 0;
        _settings.ShowFolderTree = folderTreeVisible;
        if (folderTreeVisible)
        {
            _settings.SidebarWidth = SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : SidebarColumn.Width.Value;
        }
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
        var savedShowSplit = _settings.ShowSplit;

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

        if (_config.Startup.Layout == "single")
        {
            _settings.ShowSplit = false;
        }
        else if (_config.Startup.Layout == "split")
        {
            _settings.ShowSplit = true;
        }

        if (_config.Startup.Preview == "show")
        {
            _settings.ShowPreview = true;
        }
        else if (_config.Startup.Preview == "hide")
        {
            _settings.ShowPreview = false;
        }

        if (_config.Startup.Terminal == "show")
        {
            _settings.ShowTerminalPane = true;
        }
        else if (_config.Startup.Terminal == "hide")
        {
            _settings.ShowTerminalPane = false;
        }

        if (_config.Startup.FolderTree == "show")
        {
            _settings.ShowFolderTree = true;
        }
        else if (_config.Startup.FolderTree == "hide")
        {
            _settings.ShowFolderTree = false;
        }

        // Command-line startup options override config.toml [startup] and the
        // saved session state.
        switch (_startupOptions.Layout)
        {
            case StartupOptions.LayoutMode.Single: _settings.ShowSplit = false; break;
            case StartupOptions.LayoutMode.Split: _settings.ShowSplit = true; break;
            case StartupOptions.LayoutMode.Restore: _settings.ShowSplit = savedShowSplit; break;
        }
        if (_startupOptions.Preview == StartupOptions.Toggle.On)
        {
            _settings.ShowPreview = true;
        }
        else if (_startupOptions.Preview == StartupOptions.Toggle.Off)
        {
            _settings.ShowPreview = false;
        }
        if (_startupOptions.Terminal == StartupOptions.Toggle.On)
        {
            _settings.ShowTerminalPane = true;
        }
        else if (_startupOptions.Terminal == StartupOptions.Toggle.Off)
        {
            _settings.ShowTerminalPane = false;
        }

        SetFolderTreeVisible(_settings.ShowFolderTree);
        SetSplitVisible(_settings.ShowSplit);
        SetPreviewVisible(_settings.ShowPreview);
        if (_settings.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        // Window geometry: command line (-g / --geometry) wins over config
        // [startup] geometry. Applied last so it overrides the saved bounds and
        // forces a normal (non-maximized) window.
        var geometry = _startupOptions.Geometry ?? _config.Startup.Geometry;
        if (geometry is not null)
        {
            ApplyGeometry(geometry);
        }

        HiddenButton.IsChecked = _settings.ShowHidden;
        ApplyColumnVisibility();
        ApplyColumnOrder();
        ApplyViewMode();
    }

    private void ApplyGeometry(WindowGeometry g)
    {
        // Geometry implies a specific normal window, so drop maximize.
        WindowState = WindowState.Normal;
        var work = SystemParameters.WorkArea;

        var width = g.Width ?? (Width > 0 ? Width : 1280);
        var height = g.Height ?? (Height > 0 ? Height : 760);
        width = Math.Clamp(width, MinWidth, Math.Max(MinWidth, work.Width));
        height = Math.Clamp(height, MinHeight, Math.Max(MinHeight, work.Height));
        if (g.Width.HasValue)
        {
            Width = width;
        }
        if (g.Height.HasValue)
        {
            Height = height;
        }

        if (g.HasPosition)
        {
            var left = g.FromRight ? work.Right - width - g.OffsetX!.Value : work.Left + g.OffsetX!.Value;
            var top = g.FromBottom ? work.Bottom - height - g.OffsetY!.Value : work.Top + g.OffsetY!.Value;
            // Keep the window reachable: at least a sliver stays on the work area.
            left = Math.Clamp(left, work.Left - width + 80, work.Right - 80);
            top = Math.Clamp(top, work.Top, work.Bottom - 40);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
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

    private static readonly Dictionary<string, (string Text, DateTime FetchedAt)> _freeSpaceCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan FreeSpaceCacheTtl = TimeSpan.FromSeconds(5);

    private string GetFreeSpaceText(string path)
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

            if (_freeSpaceCache.TryGetValue(root, out var cached) &&
                DateTime.UtcNow - cached.FetchedAt < FreeSpaceCacheTtl)
            {
                return cached.Text;
            }

            // Show the cached (possibly stale) value immediately, refresh in background.
            var staleText = cached.Text ?? "";
            _ = RefreshFreeSpaceAsync(root);
            return staleText;
        }
        catch
        {
            return "";
        }
    }

    private async Task RefreshFreeSpaceAsync(string root)
    {
        try
        {
            var fetched = await Task.Run(() =>
            {
                try
                {
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
            });

            _freeSpaceCache[root] = (fetched, DateTime.UtcNow);
            // Repaint the status bar if the active pane still points at the same root.
            var currentRoot = Path.GetPathRoot(GetCurrentPath(_activeGrid));
            if (string.Equals(currentRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                FreeSpaceText.Text = fetched;
            }
        }
        catch
        {
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

    // ─── USB / drive add-remove detection ─────────────────────────────────

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVNODES_CHANGED = 0x0007;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private DispatcherTimer? _driveRefreshDebounce;

    private void HookDeviceChange()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var src = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        src?.AddHook(DeviceChangeHook);
    }

    private IntPtr DeviceChangeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            // A borderless (WindowStyle=None) window maximizes to the full
            // monitor and covers the taskbar. Constrain the maximized
            // size/position to the monitor work area instead.
            ConstrainMaximizeToWorkArea(hwnd, lParam);
            return IntPtr.Zero;
        }
        if (msg != WM_DEVICECHANGE)
        {
            return IntPtr.Zero;
        }
        var ev = wParam.ToInt32();
        if (ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE || ev == DBT_DEVNODES_CHANGED)
        {
            // Debounce: Windows emits several events in quick succession when a
            // drive arrives. Refresh once after they settle.
            if (_driveRefreshDebounce is null)
            {
                _driveRefreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _driveRefreshDebounce.Tick += (_, _) =>
                {
                    _driveRefreshDebounce!.Stop();
                    LoadDrives();
                };
            }
            _driveRefreshDebounce.Stop();
            _driveRefreshDebounce.Start();
        }
        return IntPtr.Zero;
    }

    private static void ConstrainMaximizeToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var work = info.rcWork;
        var full = info.rcMonitor;
        // Positions are relative to the monitor's top-left.
        mmi.ptMaxPosition.X = work.left - full.left;
        mmi.ptMaxPosition.Y = work.top - full.top;
        mmi.ptMaxSize.X = work.right - work.left;
        mmi.ptMaxSize.Y = work.bottom - work.top;
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public WinPoint ptReserved;
        public WinPoint ptMaxSize;
        public WinPoint ptMaxPosition;
        public WinPoint ptMinTrackSize;
        public WinPoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public WinRect rcMonitor;
        public WinRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
        _leftReloadCts?.Cancel();
        _leftReloadCts?.Dispose();
        _leftReloadCts = null;
        _rightReloadCts?.Cancel();
        _rightReloadCts?.Dispose();
        _rightReloadCts = null;

        DisposeAutoRefresh();

        // Best-effort: cancel any in-flight navigation but do NOT call WebView2.Dispose()
        // synchronously here — the WPF wrapper sometimes blocks waiting for the underlying
        // CoreWebView2Controller to settle when a navigation is mid-flight, which produces
        // a "not responding" stall on close. Windows reclaims msedgewebview2.exe when the
        // Tfx process exits, so explicit teardown is not required for correctness.
        try
        {
            HtmlPreview?.CoreWebView2?.Stop();
        }
        catch
        {
        }
        try
        {
            Terminal?.CoreWebView2?.Stop();
        }
        catch
        {
        }

        // Tear down the terminal shell so no orphaned pseudo console lingers.
        ShutdownTerminal();

        CleanupArchiveTemp();
    }

    private void CleanupArchiveTemp()
    {
        if (string.IsNullOrEmpty(_archiveTempRoot))
        {
            return;
        }
        var path = _archiveTempRoot;
        _archiveTempRoot = null;
        _ = Task.Run(() =>
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        });
    }
}
