using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class LibraryOrganization
{
    public static void ReadableCombo(ComboBox combo)
    {
        var text = new Style(typeof(TextBlock)); text.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Black)); combo.Resources[typeof(TextBlock)] = text; combo.Foreground = System.Windows.Media.Brushes.Black;
        var factory = new FrameworkElementFactory(typeof(TextBlock)); factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding()); factory.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Black);
        var template = new DataTemplate { VisualTree = factory }; combo.ItemTemplate = template;
        foreach (var item in combo.Items.OfType<ComboBoxItem>()) item.ContentTemplate = template;
    }
    public static string ModDestination(string folder)
    {
        folder = folder.Trim().Trim('"');
        if (!Framework.IsPathFullyQualified(folder) || File.Exists(folder)) throw new InvalidOperationException("Choose a full mod destination folder path.");
        return Path.GetFullPath(folder);
    }
    public static bool Matches(TrackedApp app, string section, string kind, string target, string query)
    {
        if (section == "Updates" && !app.CanUpdate) return false;
        if (kind == "Applications" && app.IsMod || kind == "Mods" && !app.IsMod) return false;
        if (target.Length > 0 && (!app.IsMod || app.TargetName != target)) return false;
        return query.Length == 0 || (app.AppName + " " + app.ProjectLabel + " " + app.TargetName).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    public static bool SameInstallation(TrackedApp a, TrackedApp b)
    {
        if (!a.Repo.Equals(b.Repo, StringComparison.OrdinalIgnoreCase)) return false;
        if (!a.IsMod || !b.IsMod) return true;
        var left = AppLocationDraft.From(a).Folder; var right = AppLocationDraft.From(b).Folder;
        return left.Length > 0 && right.Length > 0 && Path.GetFullPath(left).TrimEnd('\\', '/').Equals(Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }
}
