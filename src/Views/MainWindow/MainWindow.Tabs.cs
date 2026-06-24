using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.IO.Path;

namespace Tfx;

public partial class MainWindow
{
    // Per-pane tab model. Each pane owns an ordered list of PaneTab plus the
    // index of the active one. The active tab's Path mirrors _leftPath /
    // _rightPath (the existing source of truth used everywhere else), and the
    // active tab's Back / Forward lists replace the former global _back /
    // _forward history (which had the latent bug of sharing one history across
    // both panes).
    private readonly List<PaneTab> _leftTabs = [];
    private readonly List<PaneTab> _rightTabs = [];
    private int _leftActiveTabIndex;
    private int _rightActiveTabIndex;

    private List<PaneTab> TabsOf(Pane pane) => pane == Pane.Left ? _leftTabs : _rightTabs;

    private int ActiveTabIndexOf(Pane pane) => pane == Pane.Left ? _leftActiveTabIndex : _rightActiveTabIndex;

    private void SetActiveTabIndex(Pane pane, int index)
    {
        if (pane == Pane.Left)
        {
            _leftActiveTabIndex = index;
        }
        else
        {
            _rightActiveTabIndex = index;
        }
    }

    /// <summary>
    /// The active tab of a pane, lazily seeding one tab from the pane's current
    /// path if the list is somehow empty (defensive — startup seeds explicitly).
    /// </summary>
    private PaneTab ActiveTab(Pane pane)
    {
        var tabs = TabsOf(pane);
        if (tabs.Count == 0)
        {
            tabs.Add(new PaneTab(PathOf(pane)));
            SetActiveTabIndex(pane, 0);
        }
        var idx = Math.Clamp(ActiveTabIndexOf(pane), 0, tabs.Count - 1);
        SetActiveTabIndex(pane, idx);
        return tabs[idx];
    }

    /// <summary>
    /// Seed startup tabs. Tab layout is not restored from the previous session,
    /// but explicit [startup] folder lists in config.toml are honored.
    /// </summary>
    private void InitializeTabs(bool explicitLeftStart = false)
    {
        if (!explicitLeftStart && _config.Startup.LeftFolders.Count > 0)
        {
            SeedPaneTabs(Pane.Left, _config.Startup.LeftFolders, _config.Startup.LeftActiveTab ?? 0, _leftPath);
        }
        else
        {
            SeedPaneTabs(Pane.Left, _leftPath);
        }

        if (_config.Startup.RightFolders.Count > 0)
        {
            SeedPaneTabs(Pane.Right, _config.Startup.RightFolders, _config.Startup.RightActiveTab ?? 0, _rightPath);
        }
        else
        {
            SeedPaneTabs(Pane.Right, _rightPath);
        }
    }

    private void SeedPaneTabs(Pane pane, string fallbackPath)
    {
        var tabs = TabsOf(pane);
        tabs.Clear();
        tabs.Add(new PaneTab(fallbackPath));
        SetActiveTabIndex(pane, 0);
        RebuildTabStrip(pane);
    }

    private void SeedPaneTabs(Pane pane, IReadOnlyCollection<string> configuredPaths, int activeIndex, string fallbackPath)
    {
        var tabs = TabsOf(pane);
        tabs.Clear();

        foreach (var path in configuredPaths)
        {
            if (IsPathRestorable(path))
            {
                tabs.Add(new PaneTab(path));
            }
        }

        if (tabs.Count == 0)
        {
            tabs.Add(new PaneTab(fallbackPath));
        }

        SetActiveTabIndex(pane, Math.Clamp(activeIndex, 0, tabs.Count - 1));
        var active = tabs[ActiveTabIndexOf(pane)];
        var previousPath = PathOf(pane);
        SetPathOf(pane, active.Path);

        if (!string.Equals(previousPath, active.Path, StringComparison.OrdinalIgnoreCase))
        {
            Reload(GridOf(pane), active.SelectedName ?? "..");
        }

        RebuildTabStrip(pane);
    }

    private void NewTabClick(object sender, System.Windows.RoutedEventArgs e) => NewTabInActivePane();

    /// <summary>
    /// Opens a new tab in the active pane at the same folder as the current
    /// tab and switches to it.
    /// </summary>
    private void NewTabInActivePane()
    {
        var pane = ActivePane;
        OpenNewTab(pane, PathOf(pane));
    }

    private void OpenNewTab(Pane pane, string path)
    {
        RememberActiveTabSelection(pane);
        var tabs = TabsOf(pane);
        var insertAt = ActiveTabIndexOf(pane) + 1;
        tabs.Insert(insertAt, new PaneTab(path));
        SetActiveTabIndex(pane, insertAt);
        ActivateTab(pane, insertAt);
    }

    /// <summary>
    /// Closes the tab at <paramref name="index"/> in <paramref name="pane"/>.
    /// When the last tab of a pane is closed, the pane is hidden (split off);
    /// the left pane never fully disappears — it always keeps one tab.
    /// </summary>
    private void CloseTab(Pane pane, int index)
    {
        var tabs = TabsOf(pane);
        if (index < 0 || index >= tabs.Count)
        {
            return;
        }

        if (tabs.Count == 1)
        {
            // Last tab. The right pane collapses to single-pane view; the left
            // pane has nowhere to go, so closing its last tab is a no-op.
            if (pane == Pane.Right && RightPaneColumn.Width.Value > 0)
            {
                SetSplitVisible(false);
                SaveSettings();
            }
            return;
        }

        tabs.RemoveAt(index);
        var newActive = Math.Clamp(ActiveTabIndexOf(pane) > index
            ? ActiveTabIndexOf(pane) - 1
            : ActiveTabIndexOf(pane), 0, tabs.Count - 1);
        SetActiveTabIndex(pane, newActive);
        ActivateTab(pane, newActive);
    }

    private void CloseActiveTab()
    {
        var pane = ActivePane;
        CloseTab(pane, ActiveTabIndexOf(pane));
    }

    private void CycleTab(int direction)
    {
        var pane = ActivePane;
        var tabs = TabsOf(pane);
        if (tabs.Count <= 1)
        {
            return;
        }
        var next = (ActiveTabIndexOf(pane) + direction + tabs.Count) % tabs.Count;
        SetActiveTabIndex(pane, next);
        ActivateTab(pane, next);
    }

    /// <summary>
    /// Switches the pane to the tab at <paramref name="index"/>: persists the
    /// outgoing tab's selection, points the pane path at the incoming tab, and
    /// reloads the listing restoring the remembered selection.
    /// </summary>
    private void ActivateTab(Pane pane, int index)
    {
        var tabs = TabsOf(pane);
        if (index < 0 || index >= tabs.Count)
        {
            return;
        }

        SetActiveTabIndex(pane, index);
        var tab = tabs[index];
        SetPathOf(pane, tab.Path);

        var grid = GridOf(pane);
        Reload(grid, tab.SelectedName ?? "..");
        UpdatePathText();
        if (pane == ActivePane)
        {
            QueueFolderTreeSyncToActivePane();
        }
        UpdateWatcherForPane(pane);
        RefreshGitStatusForPane(pane);
        RebuildTabStrip(pane);
        SaveSettings();
    }

    /// <summary>
    /// Saves the current selection's name into the active tab so switching back
    /// later restores it. Called before leaving a tab.
    /// </summary>
    private void RememberActiveTabSelection(Pane pane)
    {
        var grid = GridOf(pane);
        if (grid.SelectedItem is FileItem item && !item.IsParent)
        {
            ActiveTab(pane).SelectedName = item.Name;
        }
    }

    // ─── Tab strip UI ─────────────────────────────────────────────────────

    private ItemsControl TabStripOf(Pane pane) => pane == Pane.Left ? LeftTabStrip : RightTabStrip;

    /// <summary>
    /// Rebuilds the tab chips for a pane. The strip is hidden entirely when the
    /// pane only has one tab, matching the "show only when 2+ tabs" decision.
    /// </summary>
    private void RebuildTabStrip(Pane pane)
    {
        var strip = TabStripOf(pane);
        var tabs = TabsOf(pane);
        strip.Items.Clear();

        if (tabs.Count < 2)
        {
            strip.Visibility = Visibility.Collapsed;
            return;
        }

        strip.Visibility = Visibility.Visible;
        var activeIndex = ActiveTabIndexOf(pane);
        for (var i = 0; i < tabs.Count; i++)
        {
            strip.Items.Add(BuildTabChip(pane, tabs[i], i, i == activeIndex));
        }
    }

    private Border BuildTabChip(Pane pane, PaneTab tab, int index, bool active)
    {
        var fg = (Brush)FindResource("TfxForeground");
        var muted = (Brush)FindResource("TfxMuted");
        var border = (Brush)FindResource("TfxBorder");
        var activeBg = (Brush)FindResource("TfxPanelActive");
        var inactiveBg = (Brush)FindResource("TfxPanel");

        var label = new TextBlock
        {
            Text = TabTitle(tab.Path),
            Foreground = active ? fg : muted,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 140,
            Margin = new Thickness(8, 0, 4, 0)
        };

        var close = new Button
        {
            Content = "", // Segoe Fluent "ChromeClose"
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 9,
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = muted,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.T("Close tab (Ctrl+W)")
        };
        close.Click += (_, e) =>
        {
            e.Handled = true;
            CloseTab(pane, index);
        };

        var content = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        content.Children.Add(close);
        DockPanel.SetDock(label, Dock.Left);
        content.Children.Add(label);

        var chip = new Border
        {
            Background = active ? activeBg : inactiveBg,
            BorderBrush = border,
            BorderThickness = new Thickness(1, 1, 1, active ? 0 : 1),
            CornerRadius = new CornerRadius(5, 5, 0, 0),
            Margin = new Thickness(0, 0, 3, 0),
            Padding = new Thickness(2, 3, 2, 3),
            Cursor = Cursors.Hand,
            Child = content,
            ToolTip = tab.Path
        };
        chip.MouseLeftButtonUp += (_, _) =>
        {
            if (index != ActiveTabIndexOf(pane))
            {
                RememberActiveTabSelection(pane);
                ActivateTab(pane, index);
            }
            UpdateActivePane(GridOf(pane));
        };
        // Middle-click closes the tab, like a browser.
        chip.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                e.Handled = true;
                CloseTab(pane, index);
            }
        };
        return chip;
    }

    private static string TabTitle(string path)
    {
        if (ArchivePath.TryParse(path, out var archive, out var inner))
        {
            var leaf = string.IsNullOrEmpty(inner)
                ? Path.GetFileName(archive)
                : inner.TrimEnd('/').Split('/')[^1];
            return string.IsNullOrEmpty(leaf) ? Path.GetFileName(archive) : leaf;
        }
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
