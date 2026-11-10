using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ReleaseDock;

internal static class SidebarPolishTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool ok, string reason) { Assertions++; if (!ok) throw new Exception(reason); }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var found in Visuals<T>(VisualTreeHelper.GetChild(root, i))) yield return found;
    }
    private static void Layout(MainWindow window)
    {
        var root = (FrameworkElement)window.Content; root.Measure(new Size(1100, 760)); root.Arrange(new Rect(0, 0, 1100, 760)); root.UpdateLayout();
    }
    private static DependencyObject? VisibleHit(Visual root, Point point)
    {
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(root,
            node => node is UIElement element && (element.Visibility != Visibility.Visible || !element.IsHitTestVisible) ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
        return hit;
    }
    public static void Run()
    {
        Assertions = 0;
        foreach (var button in new[] { MouseButton.Left, MouseButton.Right })
        {
            var gate = new MenuClickGate();
            Check(!gate.Consume(true, true, button, MouseButtonState.Pressed, out _) && !gate.WaitingForRelease, "Clicks inside menu keep their normal action");
            Check(gate.Consume(true, false, button, MouseButtonState.Pressed, out var dismiss) && dismiss && gate.WaitingForRelease, "Outside down dismisses and is consumed");
            Check(gate.Consume(false, false, button, MouseButtonState.Pressed, out _) && gate.WaitingForRelease, "Promoted/bubbling down cannot click through after dismissal");
            Check(gate.Consume(false, false, button, MouseButtonState.Released, out _) && !gate.WaitingForRelease, "Matching up is consumed to prevent the underlying control click");
            Check(!gate.Consume(false, false, button, MouseButtonState.Pressed, out _), "Second separate click works normally");
            gate.Consume(true, false, button, MouseButtonState.Pressed, out _); gate.Reset();
            Check(!gate.WaitingForRelease, "Window deactivation/disposal clears stale input capture state");
        }
        var window = new MainWindow(preview: true);
        try
        {
            window.PreviewModLibrary(); Layout(window);
            var list = (ListBox)window.FindName("Library"); var app = list.Items.Cast<TrackedApp>().First(n => n.AppName == "desktop-app");
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(app);
            var overflow = Visuals<Button>(row).Single(b => Equals(b.Content, "⋯"));
            Check(overflow.Opacity == 0 && !overflow.IsHitTestVisible, "Idle overflow is invisible and cannot intercept pointer input");
            var triggers = overflow.Style.Triggers.OfType<DataTrigger>().Select(t => (t.Binding as System.Windows.Data.Binding)?.Path.Path).ToArray();
            Check(triggers.Contains("IsMouseOver") && triggers.Contains("IsKeyboardFocusWithin") && triggers.Contains("ContextMenu.IsOpen"), "Overflow is revealed for row hover, keyboard access and its open menu");
            var title = Visuals<TextBlock>(row).Single(t => t.Text == app.AppName);
            var status = Visuals<TextBlock>(row).Single(t => t.Text == app.LibraryStatus);
            Check(Grid.GetRow(title) == 0 && Grid.GetColumnSpan(title) == 3 && Grid.GetRow(status) == 1, "Title spans card width while status occupies the lower line");
            var root = (FrameworkElement)window.Content; var add = (Button)window.FindName("AddAppButton"); var count = (TextBlock)window.FindName("CountLabel");
            var addRight = add.TransformToAncestor(root).Transform(new Point(add.ActualWidth, 0)).X;
            var countRight = count.TransformToAncestor(root).Transform(new Point(count.ActualWidth, 0)).X;
            Check(Math.Abs(addRight - countRight) < 1 && count.TextAlignment == TextAlignment.Right, "Header buttons and count text share the right edge");
            Check(window.FindName("SidebarSplitter") is GridSplitter, "Sidebar has keyboard-accessible resize divider");
            Check(((StackPanel)window.FindName("LibraryFilterRow")).Visibility == Visibility.Collapsed && ((Grid)window.FindName("LibraryLayout")).RowDefinitions[0].ActualHeight == 0, "Hidden search/filter controls reclaim their entire row including the margin");
            var another = list.Items.Cast<TrackedApp>().Single(n => n.AppName == "Remote Stash");
            var preparedMenu = window.PrepareOverflowMenu(another);
            Layout(window);
            var currentRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(another);
            var currentOptions = Visuals<Button>(currentRow).Single(b => Equals(b.Content, "⋯"));
            Check(ReferenceEquals(list.SelectedItem, another) && ReferenceEquals(preparedMenu.PlacementTarget, list), "Overflow selects the target and anchors to a control that survives row refresh");
            Check(ReferenceEquals(currentOptions.ContextMenu, preparedMenu) && preparedMenu.PlacementRectangle.Width > 0 && preparedMenu.Items.Count > 0, "Menu is attached to the refreshed overflow control with a valid placement rectangle");
            var group = list.Items.Cast<TrackedApp>().Single(n => n.IsGroup);
            var groupRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(group);
            var groupTitle = Visuals<TextBlock>(groupRow).Single(t => t.Text == group.AppName);
            var dismissal = (MenuDismissal)typeof(MainWindow).GetField("menuDismissal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            var menuOpen = true; dismissal.Track(() => menuOpen = false);
            Layout(window);
            var shield = (Border)window.FindName("MenuClickShield");
            var headerPoint = groupTitle.TranslatePoint(new Point(5, 5), root);
            Check(shield.Visibility == Visibility.Visible && ReferenceEquals(VisibleHit(root, headerPoint), shield), "Open menu physically shields the header from hit testing, not just a simulated click filter");
            void HeaderMouse(bool down)
            {
                groupTitle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = down ? Mouse.PreviewMouseDownEvent : Mouse.PreviewMouseUpEvent });
            }
            var expandedBefore = group.Expanded;
            HeaderMouse(true);
            Check(!menuOpen && shield.Visibility == Visibility.Visible && group.Expanded == expandedBefore, "Dismissal keeps the barrier in place between outside down and up");
            HeaderMouse(false);
            Check(!menuOpen && group.Expanded == expandedBefore, "Routed outside click closes the tracked menu without changing the actual header expansion state");
            Check(shield.Visibility == Visibility.Collapsed && !ReferenceEquals(VisibleHit(root, headerPoint), shield), "Shield is removed only after release so the next click can reach controls");
            HeaderMouse(true); HeaderMouse(false);
            Check(group.Expanded != expandedBefore, "Next routed header click toggles normally after menu dismissal");
        }
        finally { window.Close(); }
        var folder = Path.Combine(Path.GetTempPath(), "GROUT-sidebar-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json"); new Preferences { SidebarWidth = 410, CheckHours = 0, SelfUpdateChecks = false }.Save(path);
            var saved = new MainWindow(dataDirectory: folder);
            try
            {
                Check(((ColumnDefinition)saved.FindName("SidebarColumn")).Width.Value == 410, "Saved sidebar width is restored at startup");
                var column = (ColumnDefinition)saved.FindName("SidebarColumn"); column.Width = new GridLength(470); Layout(saved); saved.SaveSidebarWidth();
                Check(Math.Abs(Preferences.Load(path).SidebarWidth - 470) < 1, "Completed resize persists only the chosen width");
            }
            finally { saved.StopWindow(); }
            File.WriteAllText(path, "{\"SidebarWidth\":-10}"); Check(Preferences.Load(path).SidebarWidth == 330, "Invalid saved width restores usable default");
        }
        finally { Directory.Delete(folder, true); }
    }
}
