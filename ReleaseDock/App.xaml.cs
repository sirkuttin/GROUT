using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReleaseDock;

public partial class App : Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (e.Args is ["--self-update-test-host", var testRoot])
        {
            try { await SelfUpdateTests.TestHost(testRoot); Shutdown(0); } catch { Shutdown(1); } return;
        }
        if (e.Args is ["--apply-self-update", "--plan", var applyPlan]) { Shutdown(await SelfUpdateHandoff.Apply(applyPlan)); return; }
        var selfRestart = e.Args is ["--finish-self-update", "--plan", _, "--helper-pid", _];
        if (selfRestart)
        {
            try
            {
                if (!int.TryParse(e.Args[4], out var helperPid)) throw new IOException("Invalid update helper PID.");
                var completed = SelfUpdateHandoff.Finish(e.Args[2], helperPid);
                if (completed.TestReport.Length > 0)
                {
                    SelfUpdateTests.ValidateTestRoot(Framework.GetDirectoryName(completed.Target)!);
                    if (completed.TestReport != Path.Combine(Framework.GetDirectoryName(completed.Target)!, "restarted.txt")) throw new IOException("Invalid smoke-test report.");
                    if (completed.TestFailStartup) { Shutdown(1); return; }
                    SelfUpdateHandoff.ConfirmStarted(e.Args[2], helperPid);
                    File.WriteAllText(completed.TestReport + ".tmp", "PASS: replacement restarted\n" + Framework.ProcessId); File.Move(completed.TestReport + ".tmp", completed.TestReport); Shutdown(0); return;
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "GROUT update restart"); Shutdown(1); return; }
        }
        if (e.Args.Length > 0 && e.Args is not ["--tray"] && !selfRestart)
        {
            if (e.Args is ["--preview-zip-selection", var zipOutput]) { ZipSelectionTests.Preview(zipOutput); Shutdown(); return; }
            if (e.Args is ["--check-self-update", var selfReport])
            {
                try { using var updater = new SelfUpdater(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var latest = await updater.Check(timeout.Token); File.WriteAllText(selfReport, latest != null ? $"GROUT {latest.Version} available\n{latest.Page}" : $"GROUT {SelfUpdater.CurrentLabel}: no newer stable release."); Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(selfReport, ex.ToString()); Shutdown(1); } return;
            }
            if (e.Args is ["--find-installation", var project, var discoveryReport])
            {
                try { var candidates = new InstallDiscovery().Find(new TrackedApp { Repo = Repositories.Parse(project) }); File.WriteAllText(discoveryReport, candidates.Count > 0 ? string.Join("\n\n", candidates.Select(c => c.Label)) : "No matching registered installation found."); Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(discoveryReport, ex.ToString()); Shutdown(1); }
                return;
            }
            if (e.Args is ["--detect-installed", var location, var detectionReport])
            {
                try { var found = InstalledVersions.Read(location); File.WriteAllText(detectionReport, $"{found.Version}\n{found.Source}"); Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(detectionReport, ex.ToString()); Shutdown(1); }
                return;
            }
            if (e.Args is ["--check-website", var websitePage, var websiteDownload, var websiteReport])
            {
                try { using var client = new ReleaseClient(); var found = await client.Latest(WebsiteSource.SourceKey(websitePage), websiteDownloadUrl: websiteDownload); File.WriteAllText(websiteReport, found.Tag + "\n" + found.Title + "\n" + string.Join("\n", found.Assets.Select(a => a.Url))); Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(websiteReport, ex.ToString()); Shutdown(1); }
                return;
            }
            if (e.Args is ["--check-repo", var repo, var reportPath])
            {
                try
                {
                    using var github = new ReleaseClient();
                    var release = await github.Latest(repo, true);
                    File.WriteAllText(reportPath, $"{release.Tag}\n{string.Join("\n", release.Assets.Select(a => a.Name))}");
                    Shutdown(0);
                }
                catch (Exception ex) { File.WriteAllText(reportPath, ex.ToString()); Shutdown(1); }
                return;
            }
            if (e.Args is ["--self-test", var report])
            {
                try { await SelfTests.Run(); File.WriteAllText(report, $"PASS: regression suite; {TreeMoveTests.Assertions} tree move, {ReleaseCheckTests.Assertions} release check cancellation, {InstallerWaitTests.Assertions} installer wait, {DataRecoveryTests.Assertions} data recovery, {MessageRoutingTests.Assertions} message routing, {AccessibilityTests.Assertions} accessibility/layout, {GroupHeaderTests.Assertions} group headers, {CloseBehaviorTests.Assertions} close behavior, {SidebarPolishTests.Assertions} sidebar/menu, {ManualVersionDraftTests.Assertions} manual version draft, {DownloadChoiceTests.Assertions} download recovery, {LibraryOrganizationTests.Assertions} library organization, {VersionRecordTests.Assertions} version record, {WebsiteSourceTests.Assertions} website, {D2RLoaderTests.Assertions} D2RLoader, {NexusTests.Assertions} Nexus, {FrameworkTests.Assertions} Framework compatibility, {SelfUpdateTests.Assertions} self-update, {ZipSelectionTests.Assertions} ZIP selection, {ZipFolderTests.Assertions} ZIP folder, {MissingInstallationTests.Assertions} missing installation, {StorageCleanupTests.Assertions} cleanup, {AppIdentityTests.Assertions} naming/uninstall, {AssetCompatibilityTests.Assertions} download compatibility, {FeatureTests.Assertions} feature, {WizardTests.Assertions} wizard/tray, {PortableInstallTests.Assertions} portable installation, {AppSettingsTests.Assertions} app settings, {SourceTests.Assertions} source, {SourceWizardTests.Assertions} source wizard, and {InstallDiscoveryTests.Assertions} install discovery assertions passed."); Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(report, ex.ToString()); Shutdown(1); }
                return;
            }
            if (e.Args is ["--preview" or "--preview-missing" or "--preview-nexus" or "--preview-mods" or "--preview-tree-drag" or "--preview-collapsed", var output])
            {
                var window = new MainWindow(true);
                if (e.Args[0] == "--preview-nexus")
                {
                    var library = (System.Windows.Controls.ListBox)window.FindName("Library");
                    var sample = (TrackedApp)library.SelectedItem;
                    sample.Repo = "https://nexusmods.com/examplegame/mods/123"; sample.Installed = "1.0.0"; sample.Latest = "2.0.0";
                    sample.NexusFileId = 22; sample.AssetName = "ExampleTool.zip"; sample.UpdateMethod = "Zip"; sample.UpdateTarget = @"C:\Apps\ExampleTool";
                    sample.Release = new("2.0.0", sample.RepositoryUrl + "?tab=files", "Sample Nexus release notes.", [new("ExampleTool.zip", "https://api.nexusmods.com/v1/games/examplegame/mods/123/files/22.json", -1, null, "nexusmods.com", 22, "2.0.0")], 22, "Example Nexus tool");
                    library.SelectedItem = null; library.SelectedItem = sample; library.Items.Refresh();
                }
                if (e.Args[0] == "--preview-missing")
                {
                    var library = (System.Windows.Controls.ListBox)window.FindName("Library");
                    var sample = (TrackedApp)library.SelectedItem;
                    sample.CustomName = "Gram"; sample.Repo = "https://codeberg.org/GramEditor/gram";
                    sample.InstallationMissing = true; sample.LocalError = true;
                    sample.LocalStatus = "The saved app location no longer exists: C:\\Apps\\Gram\\Gram.exe";
                    library.SelectedItem = null; library.SelectedItem = sample; library.Items.Refresh();
                }
                if (e.Args[0] is "--preview-mods" or "--preview-tree-drag") window.PreviewModLibrary();
                if (e.Args[0] == "--preview-collapsed") window.PreviewCollapsedGroups();
                if (e.Args[0] != "--preview-tree-drag")
                {
                    SaveHeadlessPreview(window, output); window.Close(); Shutdown(); return;
                }
                window.Show();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (e.Args[0] == "--preview-tree-drag") { window.PreviewTreeDropCue(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render); }
                SavePreview(window, output);
                window.Close(); Shutdown(); return;
            }
            if (e.Args is ["--tree-drag-test-host"])
            {
                var window = new MainWindow(true); window.PreviewModLibrary(); window.Title = "GROUT — isolated drag test";
                window.Closed += (_, _) => Shutdown(); window.Show(); return;
            }
            if (e.Args is ["--preview-menu", var menuOutput])
            {
                var window = Ui.Window("Menu preview", 600, 500); window.Show();
                var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = window };
                foreach (var label in new[] { "Add app here…", "Add mod here…", "New group here…", "Name, parent and child folder…", "Stop tracking" }) menu.Items.Add(new System.Windows.Controls.MenuItem { Header = label });
                ((System.Windows.Controls.MenuItem)menu.Items[2]).IsEnabled = false; menu.IsOpen = true;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); ((System.Windows.Controls.MenuItem)menu.Items[4]).Focus(); menu.UpdateLayout();
                SavePreview(menu, menuOutput); menu.IsOpen = false; window.Close(); Shutdown(); return;
            }
            if (e.Args is ["--preview-mod-setup", var modSetupOutput])
            {
                var group = new TrackedApp { IsGroup = true, CustomName = "D2R" }; var parent = new TrackedApp { ParentId = group.Id, CustomName = "Reimagined", Repo = "test/reimagined", DefaultChildFolder = @"D:\Games\D2R\d2rloader" };
                using var client = new ReleaseClient(); var window = new SetupWindow(client, [], preview: true, previewStage: "mod", trackedApps: [group, parent], parentId: parent.Id, childDefaultFolder: parent.DefaultChildFolder);
                window.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); SavePreview(window, modSetupOutput); window.Close(); Shutdown(); return;
            }
            if (e.Args is ["--preview-setup", var setupOutput])
            {
                using var github = new ReleaseClient();
                var window = new SetupWindow(github, [], preview: true);
                window.Show();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                SavePreview(window, setupOutput);
                window.Close(); Shutdown(); return;
            }
            if (e.Args is ["--preview-app-settings", var settingsOutput])
            {
                using var github = new ReleaseClient();
                var sample = new TrackedApp { Repo = "example/desktop-app", Installed = "1.0.0", Latest = "v1.1.0", InstallLocation = @"C:\Apps\DesktopApp\DesktopApp.exe", UpdateMethod = "ReplaceExe", AssetName = "DesktopApp.exe", Release = new("v1.1.0", "", "", [new("DesktopApp.exe", "", 5760000, null)]) };
                var window = new SetupWindow(github, [], edit: sample); window.Show();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                SavePreview(window, settingsOutput); window.Close(); Shutdown(); return;
            }
            if (e.Args is ["--preview-setup-first" or "--preview-install" or "--preview-portable", var otherOutput])
            {
                using var github = new ReleaseClient();
                var window = new SetupWindow(github, [], preview: true, previewStage: e.Args[0] == "--preview-portable" ? "portable" : e.Args[0] == "--preview-install" ? "install" : "first");
                window.Show();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                SavePreview(window, otherOutput);
                window.Close(); Shutdown(); return;
            }
            var help = e.Args is ["--help"] or ["-h"];
            MessageBox.Show("GROUT\n\nUsage: GROUT.exe [--help | --tray | --self-test REPORT_PATH | --preview PNG_PATH | --preview-menu PNG_PATH | --preview-mods PNG_PATH | --preview-tree-drag PNG_PATH | --preview-collapsed PNG_PATH | --tree-drag-test-host | --preview-mod-setup PNG_PATH | --preview-zip-selection PNG_PATH | --preview-nexus PNG_PATH | --preview-missing PNG_PATH | --preview-setup PNG_PATH | --preview-app-settings PNG_PATH | --preview-setup-first PNG_PATH | --preview-install PNG_PATH | --preview-portable PNG_PATH | --check-self-update REPORT_PATH | --check-website PAGE_URL DOWNLOAD_URL REPORT_PATH | --check-repo PROJECT_LINK_OR_OWNER/REPO REPORT_PATH | --detect-installed LOCATION REPORT_PATH | --find-installation PROJECT_LINK REPORT_PATH]\n\nTray starts hidden. Preview commands use sample data. Check-repo diagnoses public releases including previews. Detect-installed reads local versions. Find-installation reads matching Windows installed-app registrations. Both leave your library unchanged. Launch without flags for the app. Unknown flags are rejected.", help ? "GROUT help" : "Invalid arguments");
            Shutdown(help ? 0 : 2); return;
        }
        instance = new Mutex(true, @"Local\ReleaseDock-" + Environment.UserName, out var first);
        if (!first) { MessageBox.Show("GROUT is already running. Open it from the Windows tray.", "GROUT"); Shutdown(); return; }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var main = new MainWindow(startHidden: e.Args is ["--tray"]); main.Show();
        if (selfRestart)
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (main.LibraryReady && main.IsEnabled && main.IsLoaded) SelfUpdateHandoff.ConfirmStarted(e.Args[2], int.Parse(e.Args[4]));
            else Shutdown(1);
        }
    }
    internal static void SaveHeadlessPreview(Window window, string output)
    {
        var root = (FrameworkElement)window.Content;
        var size = new Size(window.Width, window.Height);
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(size));
            var offset = VisualTreeHelper.GetOffset(root);
            var brush = new VisualBrush(root) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(new Point(offset.X, offset.Y), root.RenderSize) };
            drawing.DrawRectangle(brush, null, new Rect(new Point(root.Margin.Left, root.Margin.Top), root.RenderSize));
        }
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(output); encoder.Save(file);
    }
    internal static void SavePreview(FrameworkElement window, string output)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(output); encoder.Save(file);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
