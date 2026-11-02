using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReleaseDock;

public sealed class PreparedZip : IDisposable
{
    private readonly string root, repo, tag, url, hash;
    public string Archive { get; }
    public string Staging { get; }
    private PreparedZip(string root, TrackedApp app, Asset asset, string archive, string staging, string hash) { this.root = root; repo = app.Repo; tag = app.Latest; url = asset.Url; Archive = archive; Staging = staging; this.hash = hash; }
    public static async Task<PreparedZip> Create(TrackedApp app, Asset asset, ReleaseClient client, Action<string> status, CancellationToken cancel, Func<string, string>? allocate = null)
    {
        if (!asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a ZIP download first.");
        var root = (allocate ?? AppPaths.NewWork)("staging");
        try
        {
            var archive = Path.Combine(root, "contents.zip");
            status("Downloading ZIP…"); await client.Download(asset, archive, new Progress<double>(p => status($"Downloading ZIP… {p:F0}%")), cancel);
            status("Inspecting ZIP contents…"); var staging = await Task.Run(() => FileUpdates.StageZip(archive, Path.Combine(root, "contents"), cancel));
            using var file = File.OpenRead(archive); var hash = Framework.Hex(await Framework.HashAsync(file, cancel));
            return new(root, app, asset, archive, staging, hash);
        }
        catch { StorageCleanup.DeleteFolder(root, Framework.GetDirectoryName(root)!); throw; }
    }
    public string? VerifiedPath(TrackedApp app, Asset asset)
    {
        if (repo != app.Repo || tag != app.Latest || url != asset.Url || !File.Exists(Archive)) return null;
        using var stream = File.OpenRead(Archive);
        if (Framework.Hex(Framework.Hash(stream)) != hash) throw new IOException("The inspected ZIP changed. Choose ZIP contents again.");
        return Archive;
    }
    public void Dispose() => StorageCleanup.DeleteFolder(root, Framework.GetDirectoryName(root)!);
}

internal sealed record ZipChoice(string Path, bool IsFile);
internal static class ZipContentPicker
{
    internal static TreeView BuildTree(string staging, string current, bool isFile)
    {
        var tree = new TreeView { Background = new SolidColorBrush(Color.FromRgb(20, 32, 51)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 12) };
        var selectionBrush = new SolidColorBrush(Color.FromRgb(38, 126, 112));
        tree.Resources[SystemColors.HighlightBrushKey] = selectionBrush; tree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selectionBrush;
        tree.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White; tree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.White;
        var root = new TreeViewItem { Header = "Entire ZIP", Tag = new ZipChoice("", false), IsExpanded = true }; tree.Items.Add(root);
        void Add(TreeViewItem parent, string directory)
        {
            foreach (var folder in Directory.GetDirectories(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var relative = Framework.GetRelativePath(staging, folder).Replace('\\', '/');
                var item = new TreeViewItem { Header = Path.GetFileName(folder) + "/", ToolTip = relative, Tag = new ZipChoice(relative, false), IsExpanded = current.Equals(relative, StringComparison.OrdinalIgnoreCase) || current.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase) };
                parent.Items.Add(item); Add(item, folder); if (!isFile && relative.Equals(current, StringComparison.OrdinalIgnoreCase)) item.IsSelected = true;
            }
            foreach (var file in Directory.GetFiles(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var relative = Framework.GetRelativePath(staging, file).Replace('\\', '/');
                var item = new TreeViewItem { Header = Path.GetFileName(file), ToolTip = relative, Tag = new ZipChoice(relative, true) }; parent.Items.Add(item);
                if (isFile && relative.Equals(current, StringComparison.OrdinalIgnoreCase)) item.IsSelected = true;
            }
        }
        Add(root, staging); if (current.Length == 0) root.IsSelected = true; return tree;
    }
    internal static PreparedZip? Show(Window owner, TrackedApp app, Asset asset, ReleaseClient client, Action<Window>? ready = null)
    {
        var window = Ui.Window("Choose ZIP contents", 720, 680); window.Owner = owner;
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var title = Ui.Text("Choose what to install", 24); grid.Children.Add(title);
        var content = new ContentControl(); Grid.SetRow(content, 1); grid.Children.Add(content);
        var status = Ui.Text("Downloading ZIP…", muted: true); content.Content = status;
        var footer = new StackPanel(); Grid.SetRow(footer, 2); grid.Children.Add(footer);
        var explanation = Ui.Text("Nothing is copied to the app folder until you install. Your choice will be used for future updates.", muted: true); footer.Children.Add(explanation);
        var actions = new WrapPanel(); footer.Children.Add(actions);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        PreparedZip? prepared = null; ZipChoice? choice = null; var closed = false;
        var use = Ui.Button("Use selection", (_, _) => { if (choice != null) { app.ZipSubfolder = choice.Path; app.ZipSourceIsFile = choice.IsFile; window.DialogResult = true; } }); use.IsEnabled = false; actions.Children.Add(use);
        actions.Children.Add(Ui.Button("Cancel", (_, _) => window.DialogResult = false)); window.Content = grid;
        window.Closed += (_, _) => { closed = true; cancel.Cancel(); };
        window.Loaded += async (_, _) =>
        {
            try
            {
                prepared = await PreparedZip.Create(app, asset, client, text => { if (!closed) status.Text = text; }, cancel.Token);
                if (closed) { prepared.Dispose(); return; }
                var tree = BuildTree(prepared.Staging, app.ZipSubfolder, app.ZipSourceIsFile);
                void Selected()
                {
                    choice = (tree.SelectedItem as TreeViewItem)?.Tag as ZipChoice; use.IsEnabled = choice != null;
                    if (choice == null) return;
                    use.Content = choice.Path.Length == 0 ? "Use entire ZIP" : choice.IsFile ? "Use this file" : "Use this folder";
                    explanation.Text = choice.Path.Length == 0 ? "Install the whole ZIP into the app folder." : choice.IsFile ? "Copy only " + choice.Path + " into the app folder as " + Path.GetFileName(choice.Path) + "." : "Install only the contents of " + choice.Path + "/ directly into the app folder.";
                    explanation.Text += " Future updates use this same selection.";
                    if (InstalledVersions.UsesD2RLoaderManifest(app)) explanation.Text += " D2RLoader mod folders with a manifest are installed without their version suffix.";
                }
                tree.SelectedItemChanged += (_, _) => Selected(); content.Content = tree; Selected();
                window.UpdateLayout(); ready?.Invoke(window);
            }
            catch (OperationCanceledException) { if (!closed) status.Text = "ZIP download cancelled or timed out."; }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
        };
        if (window.ShowDialog() == true) return prepared;
        prepared?.Dispose(); return null;
    }
    internal static string Label(TrackedApp app) => app.ZipSubfolder.Length == 0 ? "Install the entire ZIP" : app.ZipSourceIsFile ? "Install file: " + app.ZipSubfolder : "Install folder contents: " + app.ZipSubfolder + "/";
}
