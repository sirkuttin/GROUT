using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public enum TreeDropPosition { Before, Inside, After, TopLevel, Invalid }

public static class LibraryTree
{
    private static IEnumerable<TrackedApp> Ordered(IEnumerable<TrackedApp> nodes) => nodes.OrderBy(n => n.SiblingOrder).ThenBy(n => n.AppName, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Id);
    public static bool Move(IList<TrackedApp> entries, TrackedApp item, TrackedApp? target, TreeDropPosition position)
    {
        if (position == TreeDropPosition.Invalid || !entries.Contains(item) || target == item || position != TreeDropPosition.TopLevel && (target == null || !entries.Contains(target))) return false;
        if (position == TreeDropPosition.TopLevel && target != null) return false;
        var parentId = position == TreeDropPosition.TopLevel ? "" : position == TreeDropPosition.Inside ? target!.Id : target!.ParentId;
        var parent = entries.FirstOrDefault(n => n.Id == parentId);
        if (!CanParent(item, parent, entries)) return false;
        var oldParent = item.ParentId;
        var siblings = Ordered(entries.Where(n => n != item && n.ParentId == parentId)).ToList();
        var index = position is TreeDropPosition.TopLevel or TreeDropPosition.Inside ? siblings.Count : siblings.IndexOf(target!) + (position == TreeDropPosition.After ? 1 : 0);
        if (index < 0) return false;
        item.ParentId = parentId; siblings.Insert(index, item);
        for (var i = 0; i < siblings.Count; i++) siblings[i].SiblingOrder = i;
        if (oldParent != parentId) { var previous = Ordered(entries.Where(n => n.ParentId == oldParent)).ToList(); for (var i = 0; i < previous.Count; i++) previous[i].SiblingOrder = i; }
        if (position == TreeDropPosition.Inside && parent != null) parent.Expanded = true;
        return true;
    }
    public static string Breadcrumb(TrackedApp node, IEnumerable<TrackedApp> entries)
    {
        var map = entries.ToDictionary(n => n.Id); var labels = new List<string>(); var seen = new HashSet<string>(); TrackedApp? current = node;
        while (current != null && seen.Add(current.Id)) { labels.Add(current.AppName); current = map.TryGetValue(current.ParentId, out var parent) ? parent : null; }
        labels.Reverse(); return string.Join(" › ", labels);
    }
    public static bool CanParent(TrackedApp item, TrackedApp? parent, IEnumerable<TrackedApp> nodes)
    {
        var map = nodes.ToDictionary(n => n.Id); var seen = new HashSet<string> { item.Id };
        while (parent != null) { if (!seen.Add(parent.Id)) return false; parent = map.TryGetValue(parent.ParentId, out var next) ? next : null; }
        return true;
    }
    public static void Prepare(IEnumerable<TrackedApp> entries, bool updatesOnly, string query)
    {
        var nodes = entries.ToList(); var map = nodes.ToDictionary(n => n.Id); var visited = new HashSet<string>(); var order = 0;
        foreach (var siblings in nodes.GroupBy(n => n.ParentId)) { var ordered = Ordered(siblings).ToList(); for (var i = 0; i < ordered.Count; i++) ordered[i].SiblingOrder = i; }
        bool Matches(TrackedApp n) => (!updatesOnly || n.CanUpdate) && (query.Length == 0 || (n.AppName + " " + n.ProjectLabel).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        bool Subtree(TrackedApp n, HashSet<string> seen) => seen.Add(n.Id) && (Matches(n) || nodes.Where(c => c.ParentId == n.Id).Any(c => Subtree(c, seen)));
        void Visit(TrackedApp n, int depth, bool parentVisible)
        {
            if (!visited.Add(n.Id)) throw new IOException("The library contains a parent cycle.");
            var children = Ordered(nodes.Where(c => c.ParentId == n.Id)).ToList();
            n.TreeDepth = depth; n.TreeOrder = order++; n.HasChildren = children.Count > 0;
            n.TreeFilterExpanded = n.HasChildren && (updatesOnly || query.Length > 0);
            n.TreeVisible = parentVisible && Subtree(n, new HashSet<string>());
            foreach (var child in children) Visit(child, depth + 1, n.TreeVisible && (n.Expanded || updatesOnly || query.Length > 0));
            n.BranchItemCount = (n.IsGroup ? 0 : 1) + children.Sum(c => c.BranchItemCount);
            n.BranchUpdateCount = (!n.IsGroup && n.CanUpdate ? 1 : 0) + children.Sum(c => c.BranchUpdateCount);
            var issue = !n.IsGroup && (n.Error.Length > 0 || n.LocalError || n.InstallationMissing || n.UninstallPending || n.Release?.SourceWarning.Length > 0 || !n.CanUpdate && !n.IsCurrent && !n.IsOlderRelease);
            n.BranchIssueCount = (issue ? 1 : 0) + children.Sum(c => c.BranchIssueCount);
        }
        foreach (var root in Ordered(nodes.Where(n => n.ParentId.Length == 0 || !map.ContainsKey(n.ParentId)))) Visit(root, 0, true);
        if (visited.Count != nodes.Count) throw new IOException("The library contains a parent cycle.");
    }
    public static void RemoveKeepingChildren(IList<TrackedApp> nodes, TrackedApp node)
    {
        var ordered = Ordered(nodes.Where(n => n.ParentId == node.ParentId)).ToList(); var index = ordered.IndexOf(node); ordered.Remove(node);
        var children = Ordered(nodes.Where(n => n.ParentId == node.Id)).ToList();
        foreach (var child in children) child.ParentId = node.ParentId;
        ordered.InsertRange(Math.Max(0, index), children);
        for (var i = 0; i < ordered.Count; i++) ordered[i].SiblingOrder = i;
        nodes.Remove(node);
    }
    public static bool OverlapsOtherInstallation(TrackedApp item, string folder, IEnumerable<TrackedApp> nodes)
    {
        var root = Path.GetFullPath(folder).TrimEnd('\\', '/');
        return nodes.Where(n => n != item && !n.IsGroup).Any(n =>
        {
            var path = AppLocationDraft.From(n).Folder;
            if (path.Length == 0) return false;
            path = Path.GetFullPath(path).TrimEnd('\\', '/');
            return path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });
    }
    public static bool Edit(Window owner, TrackedApp node, IEnumerable<TrackedApp> entries)
    {
        var nodes = entries.ToList(); var window = Ui.Window(node.IsGroup ? "Organize group" : "Organize item", 650, 620); window.Owner = owner;
        var panel = Ui.Panel(window); panel.Children.Add(Ui.Text(node.IsGroup ? "Group name" : "Name", 18)); var name = Ui.Input(node.AppName); panel.Children.Add(name);
        System.Windows.Automation.AutomationProperties.SetName(name, node.IsGroup ? "Group name" : "Name");
        panel.Children.Add(Ui.Text("Parent", 18)); var parent = new ComboBox { Padding = new Thickness(8), Margin = new Thickness(0,0,0,12) }; LibraryOrganization.ReadableCombo(parent);
        System.Windows.Automation.AutomationProperties.SetName(parent, "Parent");
        parent.Items.Add(new ComboBoxItem { Content = "Library (top level)", Tag = "" });
        foreach (var candidate in nodes.Where(n => CanParent(node, n, nodes)).OrderBy(n => n.AppName))
        {
            var trail = Breadcrumb(candidate, nodes); var destination = candidate.IsGroup ? candidate.DefaultChildFolder : AppLocationDraft.From(candidate).Folder;
            if (nodes.Count(n => Breadcrumb(n, nodes) == trail) > 1 && destination.Length > 0) trail += " — " + destination;
            parent.Items.Add(new ComboBoxItem { Content = trail, Tag = candidate.Id, ToolTip = destination });
        }
        parent.SelectedItem = parent.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, node.ParentId)) ?? parent.Items[0]; panel.Children.Add(parent);
        panel.Children.Add(Ui.Text("Changing the parent organizes this item. Installed files stay where they are.", muted: true));
        panel.Children.Add(Ui.Text("Default install folder for new children (optional)", 18)); var folder = Ui.Input(node.DefaultChildFolder); panel.Children.Add(folder);
        System.Windows.Automation.AutomationProperties.SetName(folder, "Default install folder for new children");
        panel.Children.Add(Ui.Button("Browse…", (_, _) => { var pick = new FolderPicker(); if (pick.ShowDialog(window) == true) folder.Text = pick.FolderName; }));
        panel.Children.Add(Ui.Text("This pre-fills new child installs only. Existing child folders are not changed. Tree names are never added to a physical path.", muted: true));
        var error = Ui.Text(""); panel.Children.Add(error); var actions = new WrapPanel(); panel.Children.Add(actions);
        actions.Children.Add(Ui.Button("Cancel", (_, _) => window.DialogResult = false));
        actions.Children.Add(Ui.Button("Save", (_, _) =>
        {
            try
            {
                var label = name.Text.Trim(); if (label.Length == 0 || label.Length > 120) throw new InvalidOperationException("Enter a name between 1 and 120 characters.");
                var path = folder.Text.Trim(); if (path.Length > 0 && (!Framework.IsPathFullyQualified(path) || File.Exists(path))) throw new InvalidOperationException("Choose a full folder path, or leave the default blank.");
                var id = (parent.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                if (!CanParent(node, nodes.FirstOrDefault(n => n.Id == id), nodes)) throw new InvalidOperationException("Choose a parent outside this item's children.");
                if (label != node.AppName) node.CustomName = label;
                if (node.ParentId != id) node.SiblingOrder = int.MaxValue;
                node.ParentId = id; node.DefaultChildFolder = path; window.DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }));
        return window.ShowDialog() == true;
    }
}
