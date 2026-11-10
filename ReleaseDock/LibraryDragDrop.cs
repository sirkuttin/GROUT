using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ReleaseDock;

public partial class MainWindow
{
    private const string TreeDragFormat = "GROUT.LibraryNode";
    private Point treeDragStart;
    private TrackedApp? treeDragCandidate, treeHover;
    private DateTime treeHoverSince, treeLastScroll;
    private bool treeDragging;
    private TreeDropAdorner? treeDropCue;
    private void TreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        treeDragCandidate = null;
        if (busy || e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        for (var current = source; current != null && current is not ListBoxItem; current = TreeParent(current))
            if (current is System.Windows.Controls.Primitives.ButtonBase) return;
        treeDragCandidate = (ItemsControl.ContainerFromElement(Library, source) as ListBoxItem)?.DataContext as TrackedApp;
        treeDragStart = e.GetPosition(Library);
        if (treeDragCandidate?.IsGroup == true) e.Handled = true;
    }
    private void TreeMouseUp(object sender, MouseButtonEventArgs e)
    {
        var node = treeDragCandidate; treeDragCandidate = null;
        if (e.ChangedButton != MouseButton.Left || treeDragging || node?.IsGroup != true || e.OriginalSource is not DependencyObject source) return;
        for (var current = source; current != null && current is not ListBoxItem; current = TreeParent(current))
            if (current is System.Windows.Controls.Primitives.ButtonBase) return;
        var point = e.GetPosition(Library);
        var releasedNode = (ItemsControl.ContainerFromElement(Library, source) as ListBoxItem)?.DataContext as TrackedApp;
        if (CompleteGroupHeaderClick(node, releasedNode, treeDragStart, point)) e.Handled = true;
    }
    internal bool CompleteGroupHeaderClick(TrackedApp node, TrackedApp? releasedNode, Point pressed, Point released)
    {
        if (!node.IsGroup || releasedNode != node || Math.Abs(released.X - pressed.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(released.Y - pressed.Y) >= SystemParameters.MinimumVerticalDragDistance) return false;
        ToggleGroupHeader(node); return true;
    }
    private void TreeMouseMove(object sender, MouseEventArgs e)
    {
        if (busy || treeDragging || treeDragCandidate is not { } node || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(Library);
        if (Math.Abs(point.X - treeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - treeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        treeDragging = true;
        try { DragDrop.DoDragDrop(Library, new DataObject(TreeDragFormat, node.Id), DragDropEffects.Move); }
        finally { treeDragging = false; treeDragCandidate = treeHover = null; ClearTreeDropCue(); }
    }
    private TrackedApp? DraggedNode(DragEventArgs e) => treeDragging && (e.AllowedEffects & DragDropEffects.Move) != 0 && e.Data.GetDataPresent(TreeDragFormat, false) && e.Data.GetData(TreeDragFormat, false) is string id ? apps.FirstOrDefault(n => n.Id == id) : null;
    internal (TrackedApp? Target, TreeDropPosition Position) ResolveTreeDrop(Point point, TrackedApp? source = null) { var drop = TreeDropAt(point, source); return (drop.Target, drop.Position); }
    private (TrackedApp? Target, TreeDropPosition Position, ListBoxItem? Row) TreeDropAt(Point point, TrackedApp? source = null)
    {
        if (point.X < 0 || point.Y < 0 || point.X > Library.ActualWidth || point.Y > Library.ActualHeight) return (null, TreeDropPosition.Invalid, null);
        if (Library.InputHitTest(point) is DependencyObject original)
            for (var current = original; current != null && current != Library; current = TreeParent(current))
                if (current is System.Windows.Controls.Primitives.ScrollBar) return (null, TreeDropPosition.Invalid, null);
        if (Library.InputHitTest(point) is DependencyObject hit && ItemsControl.ContainerFromElement(Library, hit) is ListBoxItem row && row.DataContext is TrackedApp target)
        {
            var local = Library.TranslatePoint(point, row); source ??= treeDragCandidate;
            var where = local.Y < row.ActualHeight * .25 ? TreeDropPosition.Before : local.Y > row.ActualHeight * .75 ? TreeDropPosition.After
                : target.IsGroup || target.HasChildren || local.X >= row.ActualWidth - 74 ? TreeDropPosition.Inside
                : source != null ? source.TreeOrder < target.TreeOrder ? TreeDropPosition.After : TreeDropPosition.Before
                : local.Y < row.ActualHeight / 2 ? TreeDropPosition.Before : TreeDropPosition.After;
            return (target, where, row);
        }
        return (null, TreeDropPosition.TopLevel, null);
    }
    private bool ValidTreeDrop(TrackedApp source, TrackedApp? target, TreeDropPosition position)
    {
        if (position == TreeDropPosition.Invalid || source == target) return false;
        var parentId = position == TreeDropPosition.TopLevel ? "" : position == TreeDropPosition.Inside ? target?.Id : target?.ParentId;
        return LibraryTree.CanParent(source, apps.FirstOrDefault(n => n.Id == parentId), apps);
    }
    private void TreeDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        var source = DraggedNode(e); var point = e.GetPosition(Library);
        if (busy || source == null) { ClearTreeDropCue(); return; }
        AutoScrollTree(point);
        var drop = TreeDropAt(point);
        if (!ValidTreeDrop(source, drop.Target, drop.Position)) { ClearTreeDropCue(); treeHover = null; return; }
        e.Effects = DragDropEffects.Move;
        if (drop.Position == TreeDropPosition.Inside && drop.Target is { HasChildren: true, Expanded: false } closed)
        {
            if (treeHover != closed) { treeHover = closed; treeHoverSince = DateTime.UtcNow; }
            else if (DateTime.UtcNow - treeHoverSince > TimeSpan.FromMilliseconds(700)) { closed.Expanded = true; Render(); }
        }
        else treeHover = null;
        ShowTreeDropCue(drop.Target, drop.Position, drop.Row);
    }
    private void AutoScrollTree(Point point)
    {
        if (DateTime.UtcNow - treeLastScroll > TimeSpan.FromMilliseconds(100))
        {
            var scroll = TreeVisualChild<ScrollViewer>(Library);
            if (point.Y < 24) scroll?.LineUp(); else if (point.Y > Library.ActualHeight - 24) scroll?.LineDown();
            treeLastScroll = DateTime.UtcNow;
        }
    }
    private void TreeDrop(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None; ClearTreeDropCue();
        if (busy || DraggedNode(e) is not { } source) return;
        var drop = TreeDropAt(e.GetPosition(Library));
        if (MoveLibraryNode(source, drop.Target, drop.Position)) e.Effects = DragDropEffects.Move;
    }
    internal bool MoveLibraryNode(TrackedApp source, TrackedApp? target, TreeDropPosition position)
    {
        if (busy || !LibraryTree.Move(apps, source, target, position)) return false;
        SafePersist(); Render(); Library.SelectedItem = source;
        (Library.ItemContainerGenerator.ContainerFromItem(source) as ListBoxItem)?.BringIntoView();
        return true;
    }
    private void TreeDragLeave(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(Library);
        if (point.X < 0 || point.Y < 0 || point.X > Library.ActualWidth || point.Y > Library.ActualHeight) { ClearTreeDropCue(); treeHover = null; }
    }
    private static T? TreeVisualChild<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var child = VisualTreeHelper.GetChild(node, i); if (child is T found) return found; var next = TreeVisualChild<T>(child); if (next != null) return next; }
        return null;
    }
    private static DependencyObject? TreeParent(DependencyObject node) => node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    internal void PreviewTreeDropCue()
    {
        var node = apps.First(n => n.AppName == "Reimagined"); Library.UpdateLayout();
        ShowTreeDropCue(node, TreeDropPosition.Inside, Library.ItemContainerGenerator.ContainerFromItem(node) as ListBoxItem);
    }
    private void ShowTreeDropCue(TrackedApp? target, TreeDropPosition position, ListBoxItem? row)
    {
        var layer = AdornerLayer.GetAdornerLayer(Library); if (layer == null) return;
        if (treeDropCue == null) { treeDropCue = new TreeDropAdorner(Library); layer.Add(treeDropCue); }
        var marker = row;
        if (position == TreeDropPosition.After && target != null)
        {
            var index = Library.Items.IndexOf(target);
            for (var i = index + 1; i < Library.Items.Count && ((TrackedApp)Library.Items[i]).TreeDepth > target.TreeDepth; i++)
                if (Library.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem descendant) marker = descendant;
        }
        if (marker == null && Library.Items.Count > 0) marker = Library.ItemContainerGenerator.ContainerFromIndex(Library.Items.Count - 1) as ListBoxItem;
        var top = marker?.TranslatePoint(new Point(), Library).Y ?? 8;
        var bottom = Math.Min(Library.ActualHeight - 3, top + (marker?.ActualHeight ?? 0));
        var indent = Math.Max(3, Math.Min(3 + (target?.TreeDepth ?? 0) * 16, Library.ActualWidth - 24));
        var bounds = new Rect(indent, Math.Max(2, top), Math.Max(1, Library.ActualWidth - 6 - indent), Math.Max(2, bottom - Math.Max(2, top)));
        Rect? childZone = null;
        if (target is { IsGroup: false, HasChildren: false } && row != null)
        {
            var origin = row.TranslatePoint(new Point(Math.Max(0, row.ActualWidth - 74), row.ActualHeight * .25), Library);
            childZone = new Rect(origin.X, origin.Y, 70, row.ActualHeight * .5);
        }
        treeDropCue.Update(bounds, position, position == TreeDropPosition.TopLevel ? "Move to top level" : position == TreeDropPosition.Inside ? "Move into " + target!.AppName : (position == TreeDropPosition.Before ? "Before " : "After ") + target!.AppName, childZone);
    }
    private void ClearTreeDropCue()
    {
        if (treeDropCue == null) return; AdornerLayer.GetAdornerLayer(Library)?.Remove(treeDropCue); treeDropCue = null;
    }
}

internal sealed class TreeDropAdorner(UIElement element) : Adorner(element)
{
    private Rect bounds; private TreeDropPosition position; private string label = ""; private Rect? childZone;
    public void Update(Rect area, TreeDropPosition where, string text, Rect? inside = null) { bounds = area; position = where; label = text; childZone = inside; IsHitTestVisible = false; InvalidateVisual(); }
    protected override void OnRender(DrawingContext context)
    {
        var color = new SolidColorBrush(Color.FromRgb(118, 220, 198)); var pen = new Pen(color, 2);
        var y = position == TreeDropPosition.Before ? bounds.Top : bounds.Bottom;
        if (position == TreeDropPosition.Inside) context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(45, 118, 220, 198)), pen, bounds, 4, 4);
        else context.DrawLine(pen, new Point(bounds.Left, y), new Point(bounds.Right, y));
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1, bounds.Width - 12), Trimming = TextTrimming.CharacterEllipsis };
        var captionY = Math.Max(2, (position == TreeDropPosition.Inside ? bounds.Top + 2 : y - text.Height - 6));
        var caption = new Rect(bounds.Left + 3, captionY, Math.Max(1, Math.Min(bounds.Width - 3, text.Width + 12)), text.Height + 5);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(25, 86, 76)), null, caption, 3, 3); context.DrawText(text, new Point(caption.Left + 5, caption.Top + 2));
        if (childZone is { } zone)
        {
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(25, 86, 76)), pen, zone, 3, 3);
            var inside = new FormattedText("Inside", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(inside, new Point(zone.Left + (zone.Width - inside.Width) / 2, zone.Top + (zone.Height - inside.Height) / 2));
        }
    }
}
