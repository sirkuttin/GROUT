using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;

namespace ReleaseDock;

internal static class GroupHeaderTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool ok, string reason) { Assertions++; if (!ok) throw new Exception(reason); }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    public static void Run()
    {
        Assertions = 0; var window = new MainWindow(preview: true);
        try
        {
            window.PreviewModLibrary(); var list = (ListBox)window.FindName("Library");
            var group = list.Items.Cast<TrackedApp>().Single(n => n.IsGroup);
            var selected = list.Items.Cast<TrackedApp>().Single(n => n.AppName == "desktop-app"); list.SelectedItem = selected;
            foreach (var x in new[] { 5d, 140d, 280d })
            {
                var wasExpanded = group.Expanded;
                Check(window.CompleteGroupHeaderClick(group, group, new Point(x, 12), new Point(x, 12)) && group.Expanded != wasExpanded, "Header click toggles at arrow, name and blank-space positions");
                Check(ReferenceEquals(list.SelectedItem, selected) && ((TextBlock)window.FindName("AppTitle")).Text == selected.AppName, "Clicking a folder header preserves unrelated app selection/details");
            }
            var state = group.Expanded;
            Check(!window.CompleteGroupHeaderClick(group, group, new Point(100, 12), new Point(120, 12)) && group.Expanded == state, "Drag-sized pointer movement cannot also toggle the folder");
            Check(!window.CompleteGroupHeaderClick(group, selected, new Point(100, 12), new Point(100, 12)) && group.Expanded == state, "Releasing on a different row cannot toggle the pressed folder");
            Check(!window.CompleteGroupHeaderClick(selected, selected, new Point(), new Point()), "Ordinary tracked apps retain selection behavior rather than whole-row folder toggling");
            var rightClick = window.CreateTreeMenu(group); var overflow = window.CreateTreeMenu(group);
            var labels = rightClick.Items.Cast<MenuItem>().Select(i => (string)i.Header).ToArray();
            Check(labels.SequenceEqual(overflow.Items.Cast<MenuItem>().Select(i => (string)i.Header)) && labels.Contains("Remove group") && labels.Contains("Name, parent and child folder…"), "Folder right-click and overflow offer identical existing organization actions");
            Check(ReferenceEquals(list.SelectedItem, selected), "Building folder actions does not navigate to a folder page");
            list.SelectedItem = group;
            Check(window.FindName("GroupDetails") == null && ((TextBlock)window.FindName("AppTitle")).Text != group.AppName && ((StackPanel)window.FindName("Details")).Visibility == Visibility.Collapsed, "Keyboard folder selection also has no folder detail page");
            ((TextBox)window.FindName("LibrarySearch")).Text = "Auto Deposit"; var saved = group.Expanded;
            window.ToggleGroupHeader(group);
            Check(group.TreeFilterExpanded && group.Expanded == saved, "Filtered branches preserve forced-open behavior and saved collapse preference");
            ((TextBox)window.FindName("LibrarySearch")).Text = "";
            group.Expanded = true; window.ToggleGroupHeader(group); window.ToggleGroupHeader(group);
            var content = (FrameworkElement)window.Content; content.Measure(new Size(1100, 760)); content.Arrange(new Rect(0, 0, 1100, 760)); content.UpdateLayout();
            var row = list.ItemContainerGenerator.ContainerFromItem(group) as ListBoxItem;
            Check(row != null && Visuals<Button>(row).Any(b => Equals(b.Content, "⋯") && AutomationProperties.GetName(b) == group.OptionsName), "Rendered header exposes a named overflow button");
            var appMenu = window.CreateTreeMenu(selected);
            Check(appMenu.Items.Cast<MenuItem>().Any(i => Equals(i.Header, "Stop tracking")) && !appMenu.Items.Cast<MenuItem>().Any(i => Equals(i.Header, "Remove group")), "Shared menu targets the row type rather than whichever item was previously selected");
            window.SelectMenuNode(selected);
            Check(ReferenceEquals(list.SelectedItem, selected), "Opening either row menu selects and highlights the targeted app");
            var expansion = group.Expanded; window.SelectMenuNode(group);
            Check(ReferenceEquals(list.SelectedItem, group) && group.Expanded == expansion, "Opening either group menu highlights its row without toggling it");
        }
        finally { window.Close(); }
    }
}
