using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ReleaseDock.Json;

namespace ReleaseDock;

internal static class ZipSelectionTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string message) { Assertions++; if (!value) throw new Exception(message); }
    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
    }
    private static IEnumerable<TreeViewItem> Items(ItemsControl tree) => tree.Items.Cast<TreeViewItem>().SelectMany(item => new[] { item }.Concat(Items(item)));
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) { if (child is T found) yield return found; foreach (var next in Find<T>(child)) yield return next; }
    }
    internal static void Preview(string output)
    {
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
                foreach (var name in new[] { "README.txt", "ExampleTool/bin/x64/ExampleTool.exe", "ExampleTool/bin/x64/Helper.dll", "ExampleTool/bin/x86/ExampleTool.exe", "ExampleTool/docs/Guide.txt" })
                { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("Sample data"); }
            bytes = buffer.ToArray();
        }
        using var client = new ReleaseClient(new Handler(bytes));
        var app = new TrackedApp { Repo = "example/tool", Latest = "v1", ZipSubfolder = "ExampleTool/bin/x64" };
        var asset = new Asset("ExampleTool.zip", "https://github.com/example/tool/releases/download/v1/ExampleTool.zip", bytes.Length, null);
        var owner = Ui.Window("Preview owner"); owner.ShowActivated = false; owner.WindowStartupLocation = WindowStartupLocation.Manual; owner.Left = -20000; owner.Top = -20000; owner.Show();
        try { using var prepared = ZipContentPicker.Show(owner, app, asset, client, window => { App.SavePreview(window, output); window.DialogResult = false; }); }
        finally { owner.Close(); }
    }
    internal static async Task Run()
    {
        Assertions = 0;
        var root = Path.Combine(Path.GetTempPath(), "GROUT-zipselection-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "app.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                foreach (var pair in new[] { ("README.txt", "outside"), ("Package/bin/App.exe", "new"), ("Package/bin/helper.dll", "helper"), ("Package/bin/settings.ini", "new settings"), ("Package/bin/data/item.txt", "nested"), ("Package/other/skip.txt", "outside") })
                { using var writer = new StreamWriter(zip.CreateEntry(pair.Item1).Open()); writer.Write(pair.Item2); }
            var stage = FileUpdates.StageZip(archive, Path.Combine(root, "stage"));
            Check(FileUpdates.NormalizeZipSubfolder(@" Package\bin\ ") == "Package/bin", "Folder selection normalizes separators and trailing slash");
            foreach (var invalid in new[] { "../outside", "Package/../bin", "/bin", @"C:\bin", @"\\server\bin", "Package/bin.", "Package/CON", "Package//bin" })
            { var failed = false; try { FileUpdates.NormalizeZipSubfolder(invalid); } catch (IOException) { failed = true; } Check(failed, "Unsafe ZIP selection rejected"); }
            var selectedPlan = FileUpdates.PlanZip(stage, Path.Combine(root, "destination"), "Package/bin", false);
            Check(selectedPlan.Count == 4 && selectedPlan.Any(f => f.Relative == @"data\item.txt") && selectedPlan.All(f => !f.Relative.StartsWith("Package")), "Folder contents install directly, stripping only the selected prefix");
            var filePlan = FileUpdates.PlanZip(stage, Path.Combine(root, "file-destination"), "Package/bin/helper.dll", true);
            Check(filePlan.Count == 1 && filePlan.Single().Relative == "helper.dll", "Single-file selection copies only its basename");
            var app = new TrackedApp { Repo = "test/zip", ZipSubfolder = "Package/bin", ZipSourceIsFile = false };
            var copy = JsonSerializer.Deserialize<TrackedApp>(JsonSerializer.Serialize(app))!;
            Check(copy.ZipSubfolder == app.ZipSubfolder && !copy.ZipSourceIsFile && JsonSerializer.Deserialize<TrackedApp>("{}")!.ZipSubfolder == "", "Selection persists while old apps default to the whole ZIP");
            var tree = ZipContentPicker.BuildTree(stage, "Package/bin/helper.dll", true);
            Check(Items(tree).Count() == 11 && Items(tree).Any(i => i.IsSelected && i.Tag is ZipChoice { Path: "Package/bin/helper.dll", IsFile: true }), "Visual tree contains folders/files and restores saved file selection");
            foreach (var accept in new[] { true, false })
            {
                var handler = new Handler(File.ReadAllBytes(archive)); using var client = new ReleaseClient(handler);
                var asset = new Asset("app.zip", "https://github.com/test/zip/releases/download/v2/app.zip", new FileInfo(archive).Length, null);
                app = new() { Repo = "test/zip", Latest = "v2", ZipSubfolder = "Package/bin/helper.dll", ZipSourceIsFile = true, UpdateTarget = Path.Combine(root, "not-installed") };
                var owner = Ui.Window("Isolated ZIP picker test"); owner.ShowActivated = false; owner.WindowStartupLocation = WindowStartupLocation.Manual; owner.Left = -20000; owner.Top = -20000; owner.Show();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) }; var ticks = 0; var clicked = false;
                timer.Tick += (_, _) =>
                {
                    var picker = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "Choose ZIP contents");
                    if (picker == null) return;
                    var contents = Find<TreeView>(picker).FirstOrDefault();
                    if (contents == null) { if (++ticks > 200) picker.Close(); return; }
                    timer.Stop();
                    if (accept) { Items(contents).Single(i => i.Tag is ZipChoice { Path: "Package/bin", IsFile: false }).IsSelected = true; Find<Button>(picker).Single(b => Equals(b.Content, "Use this folder")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                    else Find<Button>(picker).Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    clicked = true;
                };
                timer.Start();
                try
                {
                    using var chosen = ZipContentPicker.Show(owner, app, asset, client);
                    Check(clicked && (chosen != null) == accept && !Directory.Exists(app.UpdateTarget), "Real picker confirms/cancels without installing anything");
                    Check(accept ? app.ZipSubfolder == "Package/bin" && !app.ZipSourceIsFile : app.ZipSubfolder == "Package/bin/helper.dll" && app.ZipSourceIsFile, "Confirm records selection; cancellation preserves the prior selection");
                }
                finally { timer.Stop(); owner.Close(); }
            }
            foreach (var scenario in new[] { (Path: "Package/bin", File: false), (Path: "Package/bin/helper.dll", File: true), (Path: "", File: false) })
            {
                var scenarioRoot = Path.Combine(root, Guid.NewGuid().ToString("N")); var destination = Path.Combine(scenarioRoot, "installed"); Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep"); File.WriteAllText(Path.Combine(destination, "settings.ini"), "old");
                var handler = new Handler(File.ReadAllBytes(archive)); using var client = new ReleaseClient(handler);
                var asset = new Asset("app.zip", "https://github.com/test/zip/releases/download/v2/app.zip", new FileInfo(archive).Length, null);
                app = new() { Repo = "test/zip", Installed = "v1", Latest = "v2", UpdateMethod = "Zip", InstallLocation = destination, UpdateTarget = destination, ZipSubfolder = scenario.Path, ZipSourceIsFile = scenario.File };
                string Allocate(string category) { var folder = Path.Combine(scenarioRoot, category, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
                var prepared = await PreparedZip.Create(app, asset, client, _ => { }, CancellationToken.None, Allocate);
                File.WriteAllText(Path.Combine(prepared.Staging, "Package", "bin", "App.exe"), "tampered preview");
                var owner = new Window();
                try
                {
                    var outcome = await UpdateRunner.Run(owner, app, asset, client, (_, _) => { }, () => { }, newInstall: true, prompts: new UpdatePrompts { ReviewFiles = (_, _, _, _) => throw new Exception("No review expected") }, newWork: Allocate, preparedZip: prepared);
                    Check(outcome == UpdateOutcome.FilesApplied && handler.Requests == 1, "Install reuses inspected ZIP without a second download or review");
                    Check(File.ReadAllText(Path.Combine(destination, "keep.txt")) == "keep" && !File.Exists(prepared.Archive), "Unrelated installed files remain and inspected ZIP is cleaned");
                    if (scenario.Path == "Package/bin") Check(File.ReadAllText(Path.Combine(destination, "settings.ini")) == "new settings" && File.ReadAllText(Path.Combine(destination, "App.exe")) == "new" && !Directory.Exists(Path.Combine(destination, "Package")) && !File.Exists(Path.Combine(destination, "README.txt")), "Only chosen folder contents from the verified ZIP overwrite into destination");
                    else if (scenario.File) Check(File.Exists(Path.Combine(destination, "helper.dll")) && !File.Exists(Path.Combine(destination, "App.exe")) && File.ReadAllText(Path.Combine(destination, "settings.ini")) == "old", "Single file leaves sibling files and settings untouched");
                    else Check(File.Exists(Path.Combine(destination, "README.txt")) && File.Exists(Path.Combine(destination, "Package", "bin", "App.exe")), "Whole ZIP retains original structure");
                }
                finally { owner.Close(); prepared.Dispose(); }
            }
            foreach (var choice in new[] { ("missing", false), ("Package/bin/missing.dll", true), ("Package/bin/App.exe", false) })
            { var failed = false; try { FileUpdates.PlanZip(stage, root, choice.Item1, choice.Item2); } catch (IOException) { failed = true; } Check(failed && !File.Exists(Path.Combine(root, "App.exe")), "Missing or wrong-type selection stops before installed files change"); }
            var empty = Path.Combine(stage, "empty"); Directory.CreateDirectory(empty);
            var emptyFailed = false; try { FileUpdates.PlanZip(stage, root, "empty", false); } catch (IOException) { emptyFailed = true; } Check(emptyFailed, "Empty selected folder is rejected");
        }
        finally { Directory.Delete(root, true); }
    }
}
