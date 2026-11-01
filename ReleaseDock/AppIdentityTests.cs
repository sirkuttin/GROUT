using System.IO;
namespace ReleaseDock;
internal static class AppIdentityTests
{
    public static int Assertions { get; private set; }
    public static void Run()
    {
        Assertions = 0;
        void Check(bool condition, string reason) { Assertions++; if (!condition) throw new Exception(reason); }
        var root = Path.Combine(Path.GetTempPath(), "GROUT-identity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var app = new TrackedApp { Repo = "RaYRoD-TV/MVRH", InstallLocation = Path.Combine(root, "MultiverseVRHub.exe") };
            Check(app.AppName == "MultiverseVRHub", "Filename provides readable name without existing file");
            app.CustomName = " My Hub "; Check(app.AppName == "My Hub", "Custom name takes priority");
            var store = new Store(Path.Combine(root, "apps.json")); store.Save([app]); Check(store.Load().Single().AppName == "My Hub", "Custom name persists");
            app.CustomName = ""; app.InstallLocation = ""; Check(app.AppName == "MVRH", "Repository fallback excludes owner");
            Check(AppUninstall.PortableFolder(root) == root, "Specific portable folder validated");
            var junction = Path.Combine(root, "alias");
            var real = Path.Combine(root, "real");
            Directory.CreateDirectory(Path.Combine(real, "MyApp"));
            var createJunction = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            createJunction.AddArgument("-NoProfile");
            createJunction.AddArgument("-NonInteractive");
            createJunction.AddArgument("-Command");
            createJunction.AddArgument("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + junction.Replace("'", "''") + "' -Target '" + real.Replace("'", "''") + "' | Out-Null");
            using (var process = System.Diagnostics.Process.Start(createJunction)!)
            {
                process.WaitForExit();
                Check(process.ExitCode == 0, "Temporary junction fixture created: " + process.StandardError.ReadToEnd());
            }
            try
            {
                foreach (var linkedPath in new[] { junction, Path.Combine(junction, "MyApp") })
                {
                    var rejected = false; try { AppUninstall.PortableFolder(linkedPath); } catch (IOException) { rejected = true; }
                    Check(rejected, "Portable removal rejects junctions including ancestors");
                }
                Check(Directory.Exists(Path.Combine(real, "MyApp")), "Junction validation leaves target untouched");
            }
            finally { Directory.Delete(junction); }
            foreach (var path in new[] { Path.GetPathRoot(root)!, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.Combine(root, "apps"), "relative" })
            {
                var rejected = false; try { AppUninstall.PortableFolder(path); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Shared/root/relative deletion blocked: " + path);
            }
            var command = AppUninstall.Command("MsiExec.exe /I{12345678-1234-1234-1234-123456789012}");
            Check(command.Arguments.StartsWith("/X{"), "MSI install command converted to uninstall");
            Check(AppUninstall.Command("MsiExec.exe /I\"C:\\Fixture App\\app.msi\"").Arguments == "/X\"C:\\Fixture App\\app.msi\"", "Attached quoted MSI package converted to uninstall");
            Check(AppUninstall.Command("MsiExec.exe /i \"C:\\Fixture App\\app.msi\"").Arguments == "/X \"C:\\Fixture App\\app.msi\"", "Separated quoted MSI package converted to uninstall");
            Check(AppUninstall.Command("MsiExec.exe /invalid /quiet").Arguments == "/invalid /quiet", "Unrelated MSI argument is not rewritten");
            var unsupported = false; try { AppUninstall.Command("cmd.exe /c echo test"); } catch (InvalidOperationException) { unsupported = true; }
            Check(unsupported, "Bare shell command rejected");
            app.AppFolder = root;
            var entry = new InstalledRegistration("id", "MVRH", "1", root, "", "uninstall.exe");
            Check(AppUninstall.Registered(app, [entry]) == entry, "Folder and app identity corroborate registration");
            Check(AppUninstall.Registered(app, [entry, entry with { Id = "other", Uninstall = "different.exe" }]) == null, "Ambiguous uninstallers not chosen");
            Check(AppUninstall.Registered(app, [entry with { Location = root + "-other" }]) == null, "Similar directory not matched");
            var unrelated = entry with { Name = "Unrelated app", Icon = Path.Combine(root, "Other.exe") };
            Check(AppUninstall.Registered(app, [unrelated]) == null, "Shared folder alone cannot select an unrelated uninstaller");
            app.AppFile = "MultiverseVRHub.exe";
            var matchingExe = entry with { Icon = Path.Combine(root, app.AppFile) + ",0" };
            Check(AppUninstall.Registered(app, [matchingExe]) == matchingExe, "Known app executable matches its Windows display icon");
            Check(AppUninstall.Registered(app, [entry]) == null, "Known executable without icon corroboration does not guess");
            Check(AppUninstall.Registered(app, [unrelated, matchingExe]) == matchingExe, "Shared folder registrations are narrowed by exact executable identity");
            Check(AppUninstall.Registered(app, [matchingExe with { Icon = Path.Combine(root, "Other.exe") }]) == null, "Even matching names cannot override a known executable mismatch");
            var removalFolder = Path.Combine(root, "portable"); Directory.CreateDirectory(removalFolder);
            var removalFile = Path.Combine(removalFolder, "App.exe"); File.WriteAllText(removalFile, "fixture");
            var pending = new TrackedApp { Repo = "test/portable", CustomName = "Portable fixture", InstallLocation = removalFile, Installed = "v1", Latest = "v2", Release = new("v2", "", "", []) };
            var library = new List<TrackedApp> { pending }; var libraryStore = new Store(Path.Combine(root, "uninstall-library.json"));
            AppUninstall.Begin(pending, removalFile); libraryStore.Save(library);
            Check(!pending.CanUpdate && pending.Status == "Uninstall pending", "Pending removal blocks updates and exposes lifecycle state");
            Check(!AppUninstall.Complete(library, pending, [], () => libraryStore.Save(library)), "Existing EXE cannot count as successful removal");
            File.Delete(removalFile);
            Check(AppUninstall.Complete(library, pending, [], () => libraryStore.Save(library)) && library.Count == 0 && libraryStore.Load().Count == 0, "Successful portable removal deletes and persists the library entry");
            Check(!AppUninstall.Complete(library, pending, [], () => throw new Exception("Duplicate completion")), "Completion is idempotent");
            library.Add(pending);
            try { AppUninstall.Complete(library, pending, [], () => throw new IOException("Disk full")); throw new Exception("Save failure swallowed"); } catch (IOException) { Assertions++; }
            Check(library.Contains(pending) && pending.UninstallPending, "Failed persistence preserves pending entry for retry");
            libraryStore.Save(library); var resumed = libraryStore.Load().Single();
            Check(resumed.UninstallPending && AppUninstall.CanConfirm(resumed, []), "Pending uninstall confirmation survives restart");
            AppUninstall.KeepTracking(pending); Check(!AppUninstall.CanConfirm(pending, []), "Cancel/keep tracking never removes entry");
            AppUninstall.Begin(pending, removalFolder, "registered-id");
            var registration = entry with { Id = "registered-id" };
            Check(!AppUninstall.CanConfirm(pending, [registration]), "Registry entry still present prevents confirmation");
            Check(AppUninstall.CanConfirm(pending, []), "Removed registry entry confirms installer removal even if data folder remains");
            AppUninstall.Begin(pending, removalFile, "registered-id"); File.WriteAllText(removalFile, "still installed");
            Check(!AppUninstall.CanConfirm(pending, []), "Known EXE still present prevents premature installer completion");
            AppUninstall.KeepTracking(pending); pending.InstallationMissing = true;
            var prompt = AppUninstall.TrackingPrompt(pending);
            Check(prompt.Title == "Remove missing app" && prompt.Message.Contains("location was not found") && !prompt.Message.Contains("backups") && !prompt.Message.Contains("will stay"), "Missing-app confirmation describes removal of a missing entry");
            pending.InstallationMissing = false;
            Check(AppUninstall.TrackingPrompt(pending).Message.Contains("without changing app files"), "Present-app confirmation distinguishes tracking from uninstall");
            AppUninstall.Begin(pending, removalFile);
            Check(AppUninstall.TrackingPrompt(pending).Message.Contains("does not stop a running Windows uninstaller"), "Pending-uninstall stop tracking explains independent process");
            File.Delete(removalFile);
            var preview = new MainWindow(preview: true);
            try
            {
                var list = (System.Windows.Controls.ListBox)preview.FindName("Library");
                var visibleApps = (System.Collections.ObjectModel.ObservableCollection<TrackedApp>)list.ItemsSource;
                visibleApps.Add(pending); list.SelectedItem = pending;
                preview.CheckPendingUninstalls();
                Check(!visibleApps.Contains(pending) && list.Items.Count == 1, "Successful uninstall removes actual visible library item");
                Check(((System.Windows.Controls.TextBlock)preview.FindName("StatusLabel")).Text.Contains("uninstalled and removed"), "Visible completion confirms both uninstall and library removal");
            }
            finally { preview.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }
}
