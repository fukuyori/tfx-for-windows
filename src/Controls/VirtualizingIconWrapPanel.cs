using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Tfx;

/// <summary>
/// UI-virtualizing replacement for the icon view's plain <see cref="WrapPanel"/>,
/// which realized a container for every item — opening a folder with tens of
/// thousands of entries in icon mode froze the window while all tiles were
/// built. Only the visible rows (±1 buffer row) get containers.
///
/// Assumes uniform tile size: the slot grows to the largest desired size seen
/// among realized containers (so it adapts to the configured font) and every
/// item is arranged into that slot, matching a fixed-tile Explorer-style grid.
/// Scrolling is pixel-based via <see cref="IScrollInfo"/>; the ListBox's
/// ScrollIntoView works for unrealized items through
/// <see cref="BringIndexIntoView"/>.
/// </summary>
public sealed class VirtualizingIconWrapPanel : VirtualizingPanel, IScrollInfo
{
    private const double WheelScrollDelta = 96;

    private Size _itemSize = new(120, 96);   // provisional until a container is measured
    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private int _columns = 1;

    /// <summary>Items per row in the current layout (≥ 1). Used by keyboard
    /// navigation to move the selection a full row per Up/Down press.</summary>
    public int ColumnCount => _columns;

    /// <summary>Fully visible rows in the viewport (≥ 1). Used for PageUp/Down.</summary>
    public int RowsPerViewport =>
        Math.Max(1, (int)(_viewport.Height / Math.Max(1.0, _itemSize.Height)));

    public ScrollViewer? ScrollOwner { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }

    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;

    protected override Size MeasureOverride(Size availableSize)
    {
        var itemsControl = ItemsControl.GetItemsOwner(this);
        var itemCount = itemsControl?.Items.Count ?? 0;

        // InternalChildren must be touched before the generator is used — WPF
        // wires the panel's ItemContainerGenerator up lazily on first access.
        var children = InternalChildren;
        IItemContainerGenerator generator = ItemContainerGenerator;

        var viewportWidth = double.IsInfinity(availableSize.Width) ? _itemSize.Width : availableSize.Width;
        var viewportHeight = double.IsInfinity(availableSize.Height) ? _itemSize.Height * 8 : availableSize.Height;

        if (itemCount == 0 || generator is null)
        {
            if (children.Count > 0)
            {
                RemoveInternalChildRange(0, children.Count);
            }
            _columns = 1;
            UpdateScrollInfo(new Size(viewportWidth, viewportHeight), new Size(0, 0));
            return new Size(viewportWidth, 0);
        }

        _columns = Math.Max(1, (int)(viewportWidth / _itemSize.Width));
        var rowCount = (itemCount + _columns - 1) / _columns;

        // Visible row window ±1 buffer row.
        var firstRow = Math.Max(0, (int)(_offset.Y / _itemSize.Height) - 1);
        var lastRow = Math.Min(rowCount - 1, (int)((_offset.Y + viewportHeight) / _itemSize.Height) + 1);
        var firstIndex = firstRow * _columns;
        var lastIndex = Math.Min(itemCount - 1, (lastRow + 1) * _columns - 1);

        var grew = false;
        var startPos = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;
        using (generator.StartAt(startPos, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
        {
            for (var i = firstIndex; i <= lastIndex; i++, childIndex++)
            {
                if (generator.GenerateNext(out var newlyRealized) is not UIElement child)
                {
                    break;
                }
                if (newlyRealized)
                {
                    if (childIndex >= children.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }
                    generator.PrepareItemContainer(child);
                }

                // Width comes from the fixed-width tile template; height is
                // free so the slot can learn the real (font-dependent) tile
                // height. Grow-only: a taller tile enlarges the uniform slot
                // once and the layout re-runs.
                child.Measure(new Size(_itemSize.Width, double.PositiveInfinity));
                var desired = child.DesiredSize;
                if (desired.Width > _itemSize.Width + 0.5 || desired.Height > _itemSize.Height + 0.5)
                {
                    _itemSize = new Size(
                        Math.Max(_itemSize.Width, desired.Width),
                        Math.Max(_itemSize.Height, desired.Height));
                    grew = true;
                }
            }
        }

        CleanUpItemsOutside(firstIndex, lastIndex);

        if (grew)
        {
            // Slot size changed → columns/rows shift. Re-run the layout with
            // the new uniform size (converges: the size only ever grows).
            Dispatcher.BeginInvoke(InvalidateMeasure, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        UpdateScrollInfo(
            new Size(viewportWidth, viewportHeight),
            new Size(_columns * _itemSize.Width, rowCount * _itemSize.Height));

        return new Size(viewportWidth, Math.Min(viewportHeight, rowCount * _itemSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0)
            {
                continue;
            }
            var row = itemIndex / _columns;
            var column = itemIndex % _columns;
            child.Arrange(new Rect(
                column * _itemSize.Width - _offset.X,
                row * _itemSize.Height - _offset.Y,
                _itemSize.Width,
                _itemSize.Height));
        }
        return finalSize;
    }

    private void CleanUpItemsOutside(int firstIndex, int lastIndex)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex >= firstIndex && itemIndex <= lastIndex)
            {
                continue;
            }
            generator.Remove(position, 1);
            RemoveInternalChildRange(i, 1);
        }
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        switch (args.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                if (InternalChildren.Count > 0)
                {
                    RemoveInternalChildRange(0, InternalChildren.Count);
                }
                // A new listing starts back at the top.
                SetVerticalOffset(0);
                break;
        }
        InvalidateMeasure();
    }

    /// <summary>
    /// Called by ListBox.ScrollIntoView for items that have no container yet:
    /// scroll so the index's row is inside the viewport, which makes the next
    /// measure pass realize it.
    /// </summary>
    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || _columns < 1)
        {
            return;
        }
        var top = index / _columns * _itemSize.Height;
        var bottom = top + _itemSize.Height;
        if (top < _offset.Y)
        {
            SetVerticalOffset(top);
        }
        else if (bottom > _offset.Y + _viewport.Height)
        {
            SetVerticalOffset(bottom - _viewport.Height);
        }
    }

    // ─── IScrollInfo ─────────────────────────────────────────────────────

    private void UpdateScrollInfo(Size viewport, Size extent)
    {
        if (viewport != _viewport || extent != _extent)
        {
            _viewport = viewport;
            _extent = extent;
            _offset.Y = Clamp(_offset.Y, 0, Math.Max(0, _extent.Height - _viewport.Height));
            _offset.X = Clamp(_offset.X, 0, Math.Max(0, _extent.Width - _viewport.Width));
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    public void LineUp() => SetVerticalOffset(_offset.Y - _itemSize.Height / 3);
    public void LineDown() => SetVerticalOffset(_offset.Y + _itemSize.Height / 3);
    public void LineLeft() => SetHorizontalOffset(_offset.X - 16);
    public void LineRight() => SetHorizontalOffset(_offset.X + 16);
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void PageLeft() => SetHorizontalOffset(_offset.X - _viewport.Width);
    public void PageRight() => SetHorizontalOffset(_offset.X + _viewport.Width);
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - WheelScrollDelta);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + WheelScrollDelta);
    public void MouseWheelLeft() => LineLeft();
    public void MouseWheelRight() => LineRight();

    public void SetVerticalOffset(double offset)
    {
        offset = Clamp(offset, 0, Math.Max(0, _extent.Height - _viewport.Height));
        if (offset != _offset.Y)
        {
            _offset.Y = offset;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }
    }

    public void SetHorizontalOffset(double offset)
    {
        offset = Clamp(offset, 0, Math.Max(0, _extent.Width - _viewport.Width));
        if (offset != _offset.X)
        {
            _offset.X = offset;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateArrange();
        }
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is UIElement element)
        {
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                if (ReferenceEquals(InternalChildren[i], element)
                    || (element is FrameworkElement fe && IsAncestorOfChild(InternalChildren[i], fe)))
                {
                    IItemContainerGenerator generator = ItemContainerGenerator;
                    var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
                    BringIndexIntoView(itemIndex);
                    break;
                }
            }
        }
        return rectangle;
    }

    private static bool IsAncestorOfChild(UIElement candidateAncestor, FrameworkElement descendant) =>
        candidateAncestor is Visual ancestor && descendant.IsDescendantOf(ancestor);
}
