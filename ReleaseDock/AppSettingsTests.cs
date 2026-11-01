using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class AppSettingsTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T typed) yield return typed;
            foreach (var nested in Find<T>(child)) yield return nested;
        }
    }
    private static TextBox Input(AppSettingsPage page, string name) => Find<TextBox>(page.Panel).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == name);
    public static void Run()
    {
        Assertions = 0;
        var root = Path.Combine(Path.GetTempPath(), "GROUT-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var owner = Ui.Window("Test");
        try
        {
            var exe = Path.Combine(root, "MultiverseVRHub.exe");
            var shared = Path.Combine(root, "apps"); Directory.CreateDirectory(shared);
            File.WriteAllBytes(Path.Combine(shared, "MultiverseVRHub.exe"), PortableExeFixture.Bytes());
            var coderFolder = Path.Combine(shared, "coder"); Directory.CreateDirectory(coderFolder);
            File.WriteAllBytes(Path.Combine(coderFolder, "Coder.exe"), PortableExeFixture.Bytes());
            var naming = new TrackedApp { Repo = "coder/coder", InstallLocation = shared };
            var namingPage = new AppSettingsPage(owner, naming);
            Check(Input(namingPage, "App name").Text == "coder", "Shared parent cannot supply unrelated EXE name");
            Input(namingPage, "App folder").Text = coderFolder;
            Check(Input(namingPage, "App name").Text == "Coder", "Automatic name refreshes for new folder");
            namingPage.Save(); Check(naming.CustomName == "" && naming.AppName == "Coder", "Suggested name not saved as explicit override");
            Input(namingPage, "App name").Text = "My Coder"; Input(namingPage, "App folder").Text = shared; namingPage.Save();
            Check(naming.CustomName == "My Coder", "Deliberate name survives location changes");
            Input(namingPage, "App folder").Text = coderFolder;
            Find<Button>(namingPage.Panel).Single(b => Equals(b.Content, "Use detected name")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            namingPage.Save(); Check(naming.CustomName == "" && naming.AppName == "Coder", "Detected-name action clears old override");
            var movedSource = new AppSettingsPage(owner, naming, allowRepositoryEdit: true);
            Check(Input(movedSource, "Repository URL").Text == "https://github.com/coder/coder", "Full canonical URL shown in settings");
            naming.AssetName = "old.zip"; naming.Latest = "old"; naming.DownloadedPath = "old-cache";
            Input(movedSource, "Repository URL").Text = "https://gitlab.com/group/subgroup/coder/-/releases"; movedSource.Save();
            Check(naming.Repo == "https://gitlab.com/group/subgroup/coder" && naming.InstallLocation == coderFolder, "Cross-provider source move preserves install path");
            Check(naming.AssetName == "" && naming.Latest == "" && naming.DownloadedPath == "", "Old source download/cache state cleared");
            Input(movedSource, "Repository URL").Text = "https://untrusted.example/coder/coder";
            try { movedSource.Save(); throw new Exception("Unsupported repository accepted"); } catch (ArgumentException) { Assertions++; }
            Check(naming.Repo.StartsWith("https://gitlab.com/"), "Invalid source change preserves saved repository");
            var app = new TrackedApp { Repo = "test/app", Installed = "1.0.36.0", Latest = "hub-1.0.36", InstallLocation = exe, Release = new("hub-1.0.36", "", "", [new("MultiverseVRHub.exe", "", 3, null)]), AssetName = "MultiverseVRHub.exe" };
            var page = new AppSettingsPage(owner, app);
            Check(Input(page, "App name").Text == "MultiverseVRHub", "Settings suggest the installed EXE name");
            Input(page, "App name").Text = "My VR Hub";
            Check(Input(page, "App folder").Text == root && Input(page, "App file").Text == "MultiverseVRHub.exe", "Existing EXE splits into folder and filename");
            Check(Find<TextBox>(page.Panel).Count() == 4, "Name, folder, filename and advanced manual version inputs exist");
            Check(!Find<TextBlock>(page.Panel).Any(t => t.Text.Contains("Where is it installed?") || t.Text.Contains("Destination for file replacement")), "Duplicate path labels removed");
            Check(ReleaseFiles.Label("Manual") == "Download only", "Clear download-only label");
            var method = Find<ComboBox>(page.Panel).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Update method"); method.SelectedIndex = 1;
            Check(Find<TextBlock>(page.Panel).Any(t => t.Text.Contains("Updates will replace:") && t.Text.Contains(exe)), "Replacement preview uses existing exact file, without extra destination");
            page.Save();
            Check(app.AppName == "My VR Hub", "Edited app name saved");
            Check(app.UpdateTarget == exe && app.InstallLocation == exe && app.AppFolder == root, "Save synchronizes update target and version source from one folder");
            var moved = Path.Combine(root, "moved"); Input(page, "App folder").Text = moved; page.Save();
            Check(app.UpdateTarget == Path.Combine(moved, "MultiverseVRHub.exe") && app.InstallLocation == app.UpdateTarget, "Changing one folder updates both derived paths");
            Input(page, "App file").Text = "subdir/other.exe";
            try { page.Save(); throw new Exception("Filename accepted another path"); } catch (InvalidOperationException) { Assertions++; }
            var manifest = Path.Combine(root, "release-manifest.json"); File.WriteAllText(manifest, "{\"release_tag\":\"v2\"}");
            var auto = new TrackedApp { Repo = "test/app", InstallLocation = root, AutoReconcile = true, Installed = "old", LocalStatus = "Manual version tracking" };
            var autoPage = new AppSettingsPage(owner, auto);
            Check(auto.Installed == "v2", "Opening automatic settings rereads actual source");
            Check(Find<TextBlock>(autoPage.Panel).Any(t => t.Text.Contains("v2 · Automatic detection") && t.Text.Contains("release-manifest.json")), "Accurate automatic status and source replace reset clone default");
            Check(((StackPanel)Input(autoPage, "Manual installed version").Parent).Visibility == Visibility.Collapsed, "Manual version entry hidden in automatic mode");
            Input(autoPage, "App folder").Text = Path.Combine(root, "not-checked");
            Check(Find<TextBlock>(autoPage.Panel).Any(t => t.Text.Contains("Not checked for this location")), "Changing path does not retain stale detected-version claim");
            Input(autoPage, "App folder").Text = "\"" + root + "\"";
            Find<Button>(autoPage.Panel).Single(b => Equals(b.Content, "Check version")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Find<TextBlock>(autoPage.Panel).Any(t => t.Text.Contains("v2 · Automatic detection")), "Quoted pasted folder checks consistently with saving");
            var jsonApp = new TrackedApp { InstallLocation = manifest, UpdateMethod = "Zip", Release = new("v2", "", "", [new("app.zip", "", 3, null)]), AssetName = "app.zip" };
            var jsonPage = new AppSettingsPage(owner, jsonApp); jsonPage.Save();
            Check(jsonApp.AppFolder == root && jsonApp.UpdateTarget == root && jsonApp.InstallLocation == manifest, "JSON version source retained separately while ZIP uses single app folder");
            var conflicting = new TrackedApp { InstallLocation = exe, UpdateTarget = Path.Combine(root, "other", "different.exe"), UpdateMethod = "ReplaceExe" };
            var draft = AppLocationDraft.From(conflicting);
            Check(draft.Conflict, "Conflicting legacy folders are detected");
            try { draft.Apply(conflicting, "ReplaceExe"); throw new Exception("Unresolved conflict saved"); } catch (InvalidOperationException) { Assertions++; }
            Check(conflicting.UpdateTarget.EndsWith(@"other\different.exe"), "Unresolved legacy destination preserved");
            draft.Resolve(true); draft.Apply(conflicting, "ReplaceExe");
            Check(conflicting.UpdateTarget == exe, "Explicit source choice consolidates to source EXE");
            conflicting = new() { InstallLocation = exe, UpdateTarget = Path.Combine(root, "other", "different.exe"), UpdateMethod = "ReplaceExe" };
            draft = AppLocationDraft.From(conflicting); draft.Resolve(false); draft.Apply(conflicting, "ReplaceExe");
            Check(conflicting.UpdateTarget == Path.Combine(root, "other", "different.exe") && conflicting.InstallLocation == exe, "Explicit target choice preserves target and advanced version-source exception");
            var store = new Store(Path.Combine(root, "library.json")); store.Save([conflicting]); var loaded = store.Load().Single();
            Check(loaded.AppFolder == conflicting.AppFolder && loaded.VersionSourceOverride == exe, "Consolidated locations survive persistence");
            var incomplete = new TrackedApp { Repo = "test/app" };
            var incompletePage = new AppSettingsPage(owner, incomplete);
            Find<ComboBox>(incompletePage.Panel).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Update method").SelectedIndex = 1;
            Input(incompletePage, "App folder").Text = "unfinished-relative-path";
            incompletePage.Save(false);
            Check(incomplete.AppFolder == "unfinished-relative-path" && incomplete.UpdateMethod == "ReplaceExe", "Back captures incomplete replacement draft without validation");
            var conflictApp = new TrackedApp { InstallLocation = exe, UpdateTarget = Path.Combine(root, "different", "Other.exe"), UpdateMethod = "ReplaceExe" };
            var conflictPage = new AppSettingsPage(owner, conflictApp); conflictPage.Save(false);
            Check(conflictApp.PendingLocationConflict && AppLocationDraft.From(conflictApp).Conflict, "Back preserves unresolved location conflict for return");
            Find<Button>(conflictPage.Panel).Single(b => Equals(b.Content, "Keep update destination and separate version source")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Input(conflictPage, "App file").Text == "Other.exe", "Conflict choice retains selected target filename despite textbox events");
            var checkApp = new TrackedApp { InstallLocation = root, Installed = "old", AutoReconcile = false };
            var checkPage = new AppSettingsPage(owner, checkApp);
            Find<Button>(checkPage.Panel).Single(b => Equals(b.Content, "Check version")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(checkApp.Installed == "v2" && !checkApp.AutoReconcile && Find<TextBlock>(checkPage.Panel).Any(t => t.Text.Contains("Checked now (manual tracking)")), "Check version performs one-time read in manual mode");
            using var github = new ReleaseClient();
            var modalApp = new TrackedApp { Repo = "test/app", Installed = "1.0.36.0", Latest = "hub-1.0.36", InstallLocation = exe, AssetName = "MultiverseVRHub.exe", Release = new("hub-1.0.36", "", "", [new("MultiverseVRHub.exe", "", 3, null)]) };
            var settings = new SetupWindow(github, [], edit: modalApp) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            try
            {
                Find<ComboBox>(settings).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Update method").SelectedIndex = 1;
                _ = settings.Dispatcher.InvokeAsync(() => Find<Button>(settings).Single(b => Equals(b.Content, "Save settings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; timeout.Tick += (_, _) => { timeout.Stop(); settings.Close(); }; timeout.Start();
                try { Check(settings.ShowDialog() == true && settings.Result.UpdateTarget == exe, "Real Save settings click completes modal with derived EXE destination"); }
                finally { timeout.Stop(); }
                Check(modalApp.UpdateTarget == "", "Editing settings preserves original until dialog accepted");
            }
            finally { settings.Close(); }
            var advancedConflict = new TrackedApp { AppFolder = root, AppFile = "Foo.exe", VersionSourceOverride = "release-manifest.json", InstallLocation = manifest, UpdateTarget = Path.Combine(root, "Bar.exe"), UpdateMethod = "ReplaceExe" };
            var advancedDraft = AppLocationDraft.From(advancedConflict);
            Check(advancedDraft.Conflict, "Advanced manifest source cannot hide conflicting EXE target names");
            try { advancedDraft.Apply(advancedConflict, "ReplaceExe"); throw new Exception("Advanced conflict silently redirected target"); } catch (InvalidOperationException) { Assertions++; }
            Check(advancedConflict.UpdateTarget == Path.Combine(root, "Bar.exe"), "Unresolved advanced conflict preserves original replacement target");
        }
        finally { owner.Close(); Directory.Delete(root, true); }
    }
}
