using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class InstallDiscoveryTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) { if (child is T value) yield return value; foreach (var item in Find<T>(child)) yield return item; }
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("/releases/download/")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("abc")) });
            var json = request.RequestUri.AbsolutePath.EndsWith("releases/latest") ? """{"tag_name":"v3.4.1","html_url":"https://codeberg.org/GramEditor/gram/releases/tag/v3.4.1","body":"","assets":[{"name":"GramSetup-3.4.1.exe","browser_download_url":"https://codeberg.org/GramEditor/gram/releases/download/v3.4.1/setup.exe","size":3}]}""" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
    }
    public static async Task Run()
    {
        Assertions = 0;
        var root = Path.Combine(Path.GetTempPath(), "GROUT-discovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "Programs", "Gram"); Directory.CreateDirectory(folder); var exe = Path.Combine(folder, "Gram.exe"); File.WriteAllText(exe, "app");
        var entry = new InstalledRegistration("HKCU/64/Gram", "Gram", "3.4.1", folder, $"\"{exe}\",0", $"\"{Path.Combine(folder, "unins000.exe")}\"");
        var app = new TrackedApp { Repo = "https://codeberg.org/GramEditor/gram", Latest = "v3.4.1", AssetName = "GramSetup-3.4.1.exe" };
        try
        {
            Check(InstallDiscovery.ExePath(entry.Icon) == exe, "Quoted DisplayIcon with icon index parsed without executing command");
            Check(InstallDiscovery.ExePath(exe + ",0") == exe, "Unquoted DisplayIcon containing directory spaces parsed");
            IReadOnlyList<InstalledRegistration> current = [entry];
            var discovery = new InstallDiscovery(() => current);
            var candidates = discovery.Find(app, []);
            Check(candidates.Count == 1 && candidates[0].Changed && candidates[0].AppFile == "Gram.exe", "New exact registration identifies Gram's folder and app file");
            Check(InstallDiscovery.ClearMatch(candidates) != null, "One exact new registration is a clear match");
            app.InstallCandidates = candidates; InstallDiscovery.Apply(app, candidates[0]);
            Check(app.AppFolder == folder && app.InstallLocation == exe && app.Installed == "3.4.1", "Registration populates exact source, folder and reported version");
            Check(!app.AutoReconcile && app.InstalledProvenance.Contains("Windows installed-app entry"), "Missing binary metadata uses explicit registry provenance rather than pretending file detection works");
            Check(InstallDiscovery.CanFinish(app), "Clear installed app can finish setup automatically");
            Check(app.InstallationRegistrationId == entry.Id, "Applying the selected installation saves its registration identity");
            current = [entry with { Name = "Renamed product", Icon = "", Uninstall = "uninstall-test" }];
            Check(InstallDiscovery.ClearMatch(discovery.Find(app))?.Entry.Id == entry.Id, "Saved registration is reused even when names and icon change");
            Check(AppUninstall.Registered(app, current)?.Id == entry.Id, "Uninstall uses the saved registration");
            current = [entry];
            var before = current;
            current = [entry, entry with { Id = "HKLM/32/Gram" }];
            Check(discovery.Find(app, before).Count == 1, "Registry-view duplicate entries collapse to one installation");
            var secondFolder = Path.Combine(root, "SecondGram"); Directory.CreateDirectory(secondFolder); var secondExe = Path.Combine(secondFolder, "Gram.exe"); File.WriteAllText(secondExe, "app");
            current = [entry, entry with { Id = "Second", Location = secondFolder, Icon = secondExe }];
            Check(InstallDiscovery.ClearMatch(discovery.Find(new TrackedApp { Repo = app.Repo, Latest = app.Latest, AssetName = app.AssetName }, [])) == null, "Two equally plausible installations require a choice");
            current = [entry with { Id = "other", Name = "Other App", Location = secondFolder, Icon = Path.Combine(secondFolder, "Other.exe") }];
            Check(discovery.Find(new TrackedApp { Repo = app.Repo, AssetName = app.AssetName }, []).Count == 0, "New unrelated app registration is never adopted just because it changed");
            var renamedApp = new TrackedApp { Repo = "bearstonem/d2r-pcvr", AssetName = "D2R-PCVR-Setup-0.3.30.exe", Latest = "v0.3.35-windows-preview", InstallLocation = folder.ToUpperInvariant() + Path.DirectorySeparatorChar };
            var renamedEntry = entry with { Name = "D2R VR Unleashed (Windows VR mod)", Version = "0.3.35", Icon = Path.Combine(folder, ".runtime", "python", "python.exe") };
            current = [renamedEntry];
            var renamedCandidates = discovery.Find(renamedApp);
            Check(renamedCandidates.Count == 1 && InstallDiscovery.ClearMatch(renamedCandidates) != null, "Configured folder recognizes a renamed app with a nested runtime icon");
            renamedApp.InstallCandidates = renamedCandidates; InstallDiscovery.Apply(renamedApp, renamedCandidates.Single());
            Check(renamedApp.Installed == "0.3.35" && renamedApp.InstallDiscoveryNote.Length == 0, "Matching folder uses the registered version without a success message");
            var freshFolder = Path.Combine(root, "D2R PCVR"); Directory.CreateDirectory(freshFolder);
            var freshApp = new TrackedApp { Repo = renamedApp.Repo, AssetName = renamedApp.AssetName, Latest = renamedApp.Latest };
            var runtime = Path.Combine(freshFolder, ".runtime", "python", "python.exe"); Directory.CreateDirectory(Path.GetDirectoryName(runtime)!); File.WriteAllText(runtime, "runtime");
            current = [renamedEntry with { Location = freshFolder, Icon = runtime }];
            var freshCandidates = discovery.Find(freshApp, []);
            Check(freshCandidates.Count == 1 && freshCandidates[0].Changed && InstallDiscovery.ClearMatch(freshCandidates) != null, "First installation is discovered by its folder name despite renamed registration and runtime icon");
            freshApp.InstallCandidates = freshCandidates; InstallDiscovery.Apply(freshApp, freshCandidates.Single());
            Check(freshApp.AppFolder == freshFolder && freshApp.Installed == "0.3.35", "First installation gets the registered folder and version");
            Check(InstallDiscovery.CanFinish(freshApp), "First installation can finish with a registered version and nested runtime executable");
            var boundStore = new Store(Path.Combine(root, "bound.json")); boundStore.Save([freshApp]);
            Check(boundStore.Load().Single().InstallationRegistrationId == renamedEntry.Id, "First installation retains its registration after restart");
            renamedApp.InstallationRegistrationId = "";
            current = [renamedEntry with { Location = secondFolder }];
            Check(discovery.Find(renamedApp).Count == 0, "A different folder cannot establish an unrelated name match");
            current = [entry with { Location = "" }];
            Check(discovery.Find(app).Single().Folder == folder, "DisplayIcon supplies folder when InstallLocation is absent");
            current = [entry with { Location = "", Icon = "" }];
            Check(discovery.Find(app).Single().Folder == folder, "Uninstaller path supplies directory only, never execution");
            var older = entry with { Version = "3.3.0" }; current = [older];
            var olderApp = new TrackedApp { Repo = app.Repo, Latest = app.Latest, AssetName = app.AssetName };
            olderApp.InstallCandidates = discovery.Find(olderApp, [older]); InstallDiscovery.Apply(olderApp, olderApp.InstallCandidates.Single());
            Check(!InstallDiscovery.CanFinish(olderApp), "Unchanged older registration does not close setup as a completed new install");
            olderApp.InstallCandidates = discovery.Find(olderApp, []); InstallDiscovery.Apply(olderApp, olderApp.InstallCandidates.Single());
            Check(!InstallDiscovery.CanFinish(olderApp), "Even changed registration cannot auto-finish while an older numeric version is still reported");
            var emptyFolder = Path.Combine(root, "EmptyGram"); Directory.CreateDirectory(emptyFolder);
            current = [entry with { Id = "empty", Location = emptyFolder, Icon = Path.Combine(emptyFolder, "Gram.exe") }];
            var emptyApp = new TrackedApp { Repo = app.Repo, Latest = app.Latest, AssetName = app.AssetName };
            emptyApp.InstallCandidates = discovery.Find(emptyApp, []); InstallDiscovery.Apply(emptyApp, emptyApp.InstallCandidates.Single());
            Check(!InstallDiscovery.CanFinish(emptyApp), "Registration without an app file/version source does not prematurely close setup");
            current = [];
            using var client = new ReleaseClient(new Handler());
            string Work(string category) { var path = Path.Combine(root, category, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
            var prompts = new UpdatePrompts
            {
                RunInstaller = (_, _) => true, LeaveSetup = _ => true,
                ExecuteInstaller = _ => { current = [entry]; return Task.CompletedTask; }
            };
            var wizard = new SetupWindow(client, [], updatePrompts: prompts, newWork: Work, discovery: discovery) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            try
            {
                Find<TextBox>(wizard).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text = app.Repo;
                Find<RadioButton>(wizard).Single(r => Equals(r.Content, "Install this app")).IsChecked = true;
                Find<Button>(wizard).Single(b => Equals(b.Content, "Next →")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 100 && !Find<Button>(wizard).Any(b => Equals(b.Content, "Choose how to use this EXE")); i++) await Task.Delay(20);
                Find<ComboBox>(wizard).Last().SelectedIndex = 2;
                _ = wizard.Dispatcher.InvokeAsync(() => Find<Button>(wizard).Single(b => Equals(b.Content, "Download and run installer")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) }; timeout.Tick += (_, _) => { timeout.Stop(); wizard.Close(); }; timeout.Start();
                try { Check(wizard.ShowDialog() == true, "Installer → registry discovery → setup closes without final directory screen"); } finally { timeout.Stop(); }
                Check(wizard.Result.AppFolder == folder && InstalledVersions.Matches(wizard.Result.Installed, "3.4.1") && wizard.Result.InstallationLocated, "Auto-completed wizard returns discovered installation for library addition");
                var store = new Store(Path.Combine(root, "library.json")); store.Save([wizard.Result]);
                Check(store.Load().Single().InstallLocation == exe, "Automatically located installation survives library persistence");
                Check(store.Load().Single().InstallationRegistrationId == entry.Id, "The installation registration survives library persistence");
            }
            finally { wizard.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }
}
