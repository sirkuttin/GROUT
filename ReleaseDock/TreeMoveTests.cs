using System.IO;
using System.Windows.Controls;

namespace ReleaseDock;

public static class TreeMoveTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private static string[] Names(List<TrackedApp> nodes) { LibraryTree.Prepare(nodes, false, ""); return nodes.Where(n => n.TreeVisible).OrderBy(n => n.TreeOrder).Select(n => n.AppName).ToArray(); }
    public static void Run()
    {
        Assertions = 0;
        var alpha = new TrackedApp { IsGroup = true, CustomName = "Alpha" };
        var zulu = new TrackedApp { IsGroup = true, CustomName = "Zulu" };
        var parent = new TrackedApp { IsGroup = true, CustomName = "D2R", DefaultChildFolder = @"D:\Games\D2R" };
        var mod = new TrackedApp { Repo = "test/mod", CustomName = "Reimagined", ParentId = parent.Id, AppFolder = @"D:\Games\Reimagined", UpdateTarget = @"D:\Games\Reimagined", Installed = "3.1.1", Latest = "3.1.1", ZipSubfolder = "package/content" };
        var child = new TrackedApp { Repo = "test/plugin", CustomName = "Remote Stash", ParentId = mod.Id, AppFolder = @"D:\Games\plugins", Installed = "2.3.5" };
        var nodes = new List<TrackedApp> { zulu, parent, child, alpha, mod };
        Check(Names(nodes).SequenceEqual(new[] { "Alpha", "D2R", "Reimagined", "Remote Stash", "Zulu" }), "Old libraries initialize sibling order from existing alphabetical presentation");
        Check(LibraryTree.Move(nodes, zulu, alpha, TreeDropPosition.Before) && Names(nodes)[0] == "Zulu", "Dragging before a root overrides alphabetical order");
        zulu.CustomName = "ZZZ"; Check(Names(nodes)[0] == "ZZZ", "Renaming does not undo chosen order");
        Check(LibraryTree.Move(nodes, zulu, alpha, TreeDropPosition.After) && Names(nodes).Take(2).SequenceEqual(new[] { "Alpha", "ZZZ" }), "Moving down within siblings has correct insertion index");
        Check(LibraryTree.Move(nodes, zulu, parent, TreeDropPosition.After) && Names(nodes).Last() == "ZZZ", "After a parent means after its complete subtree");
        parent.Expanded = false;
        Check(LibraryTree.Move(nodes, zulu, parent, TreeDropPosition.Inside) && parent.Expanded && zulu.ParentId == parent.Id, "Dropping onto a closed node reparents and opens it");
        Check(Names(nodes).SequenceEqual(new[] { "Alpha", "D2R", "Reimagined", "Remote Stash", "ZZZ" }), "Inside appends after existing children, keeping their subtrees intact");
        Check(LibraryTree.Move(nodes, mod, alpha, TreeDropPosition.Inside) && child.ParentId == mod.Id && mod.ParentId == alpha.Id, "Moving a branch retains its children");
        Check(mod.AppFolder == @"D:\Games\Reimagined" && mod.UpdateTarget == @"D:\Games\Reimagined" && mod.Installed == "3.1.1" && mod.ZipSubfolder == "package/content" && parent.DefaultChildFolder == @"D:\Games\D2R", "Tree moves never change install paths, version, ZIP mapping or folder defaults");
        var before = Json.JsonSerializer.Serialize(nodes);
        Check(!LibraryTree.Move(nodes, mod, child, TreeDropPosition.Inside), "Cannot move a node inside its own descendant");
        Check(!LibraryTree.Move(nodes, mod, child, TreeDropPosition.Before) && !LibraryTree.Move(nodes, mod, mod, TreeDropPosition.After), "Cannot reorder into a descendant parent or onto itself");
        Check(Json.JsonSerializer.Serialize(nodes) == before, "Rejected drops leave all persisted state untouched");
        Check(!LibraryTree.Move(nodes, child, null, TreeDropPosition.Invalid), "Non-content drop zones are rejected");
        Check(LibraryTree.Move(nodes, child, null, TreeDropPosition.TopLevel) && child.ParentId == "" && Names(nodes).Last() == "Remote Stash", "Dropping in blank space moves an item to the end of top level");
        var root = Path.Combine(Path.GetTempPath(), "GROUT-tree-move-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(Path.Combine(root, "apps.json")); store.Save(nodes); var restored = store.Load();
            Check(Names(restored).SequenceEqual(Names(nodes)) && restored.Single(n => n.Id == child.Id).ParentId == "", "Manual order and parent changes survive library reload");
        }
        finally { Directory.Delete(root, true); }
        var promoted = new TrackedApp { IsGroup = true, CustomName = "Promoted", ParentId = alpha.Id };
        nodes.Add(promoted); var previousRoots = Names(nodes); LibraryTree.RemoveKeepingChildren(nodes, alpha);
        Check(Names(nodes).Take(3).SequenceEqual(new[] { "Reimagined", "Promoted", "D2R" }), "Removing a parent promotes children at its position without reshuffling other siblings");
        var preview = new MainWindow(true);
        try
        {
            preview.PreviewModLibrary(); var list = (ListBox)preview.FindName("Library"); var displayed = list.Items.Cast<TrackedApp>().ToList();
            preview.ShowActivated = false; preview.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual; preview.Left = -20000; preview.Top = -20000; preview.Show(); preview.UpdateLayout();
            var sibling = displayed.Single(n => n.AppName == "Auto Deposit");
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(sibling); var top = row.TranslatePoint(new System.Windows.Point(), list).Y;
            var beforeSibling = preview.ResolveTreeDrop(new System.Windows.Point(150, top + 3));
            Check(beforeSibling.Target == sibling && beforeSibling.Position == TreeDropPosition.Before, "Top padding of a real child row is a before-sibling drop, not empty top-level space");
            var afterSibling = preview.ResolveTreeDrop(new System.Windows.Point(150, top + row.ActualHeight - 3));
            Check(afterSibling.Target == sibling && afterSibling.Position == TreeDropPosition.After, "Bottom padding of a real child row is an after-sibling drop");
            var remote = displayed.Single(n => n.AppName == "Remote Stash");
            var up = preview.ResolveTreeDrop(new System.Windows.Point(150, top + row.ActualHeight * .4), remote);
            Check(up.Target == sibling && up.Position == TreeDropPosition.Before, "Dragging upward onto sibling text reorders before it instead of nesting");
            var remoteRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(remote); var remoteTop = remoteRow.TranslatePoint(new System.Windows.Point(), list).Y;
            var down = preview.ResolveTreeDrop(new System.Windows.Point(150, remoteTop + remoteRow.ActualHeight * .4), sibling);
            Check(down.Target == remote && down.Position == TreeDropPosition.After, "Dragging downward onto sibling text reorders after it instead of nesting");
            var inside = preview.ResolveTreeDrop(new System.Windows.Point(row.ActualWidth - 30, top + row.ActualHeight / 2), remote);
            Check(inside.Target == sibling && inside.Position == TreeDropPosition.Inside, "Explicit Inside area still supports making a leaf into a parent");
            var moved = displayed.Single(n => n.AppName == "Remote Stash"); var target = displayed.Single(n => n.AppName == "desktop-app");
            Check(preview.MoveLibraryNode(moved, target, TreeDropPosition.Before), "UI drop operation executes shared move logic");
            Check(ReferenceEquals(list.SelectedItem, moved) && moved.ParentId == "" && moved.TreeDepth == 0, "After a drop the moved node stays selected and visible at its new depth");
        }
        finally { preview.Close(); }
    }
}
