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
        if (e.Args.Length > 0 && e.Args is not ["--tray"])
        {
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
            if (e.Args is ["--check-repo", var repo, var reportPath])
            {
                try
                {
                    using var github = new ReleaseClient();
                    var release = await github.Latest(repo, true);
                    File.WriteAllText(reportPath, $"{release.Tag}\n{string.Join('\n', release.Assets.Select(a => a.Name))}");
                    Shutdown(0);
                }
                catch (Exception ex) { File.WriteAllText(reportPath, ex.ToString()); Shutdown(1); }
                return;
            }
            if (e.Args is ["--self-test", var report])
            {
                try { await SelfTests.Run(); File.WriteAllText(report, $"PASS: regression suite; {StorageCleanupTests.Assertions} cleanup, {AppIdentityTests.Assertions} naming/uninstall, {AssetCompatibilityTests.Assertions} download compatibility, {FeatureTests.Assertions} feature, {WizardTests.Assertions} wizard/tray, {PortableInstallTests.Assertions} portable installation, {AppSettingsTests.Assertions} app settings, {SourceTests.Assertions} source, {SourceWizardTests.Assertions} source wizard, and {InstallDiscoveryTests.Assertions} install discovery assertions passed."); Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(report, ex.ToString()); Shutdown(1); }
                return;
            }
            if (e.Args is ["--preview", var output])
            {
                var window = new MainWindow(true);
                window.Show();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(output)) encoder.Save(file);
                window.Close(); Shutdown(); return;
            }
            if (e.Args is ["--preview-setup", var setupOutput])
            {
                using var github = new ReleaseClient();
                var window = new SetupWindow(github, [], preview: true);
                window.Show();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(setupOutput)) encoder.Save(file);
                window.Close(); Shutdown(); return;
            }
            if (e.Args is ["--preview-app-settings", var settingsOutput])
            {
                using var github = new ReleaseClient();
                var sample = new TrackedApp { Repo = "example/desktop-app", Installed = "1.0.36.0", Latest = "hub-1.0.36", InstallLocation = @"C:\Apps\MultiverseVRHub.exe", UpdateMethod = "ReplaceExe", AssetName = "MultiverseVRHub.exe", Release = new("hub-1.0.36", "", "", [new("MultiverseVRHub.exe", "", 5760000, null)]) };
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
            if (e.Args is ["--preview-files", var filesOutput])
            {
                var owner = new MainWindow(true); owner.Show();
                var files = new List<UpdateFile>
                {
                    new() { Relative = "DesktopApp.exe", Existed = true },
                    new() { Relative = "settings.ini", Existed = true, Apply = false },
                    new() { Relative = "release-manifest.json", Existed = true },
                    new() { Relative = @"assets\new-icon.png" }
                };
                _ = Dispatcher.InvokeAsync(() => { var dialog = owner.OwnedWindows.Cast<Window>().Single(); SavePreview(dialog, filesOutput); dialog.Close(); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                UpdateRunner.Review(owner, files, null, @"C:\Apps\DesktopApp");
                owner.Close(); Shutdown(); return;
            }
            var help = e.Args is ["--help"] or ["-h"];
            MessageBox.Show("GROUT\n\nUsage: GROUT.exe [--help | --tray | --self-test REPORT_PATH | --preview PNG_PATH | --preview-setup PNG_PATH | --preview-app-settings PNG_PATH | --preview-setup-first PNG_PATH | --preview-install PNG_PATH | --preview-portable PNG_PATH | --preview-files PNG_PATH | --check-repo PROJECT_LINK_OR_OWNER/REPO REPORT_PATH | --detect-installed LOCATION REPORT_PATH | --find-installation PROJECT_LINK REPORT_PATH]\n\nTray starts hidden. Preview commands use sample data. Check-repo diagnoses public releases including previews. Detect-installed reads local versions. Find-installation reads matching Windows installed-app registrations. Both leave your library unchanged. Launch without flags for the app. Unknown flags are rejected.", help ? "GROUT help" : "Invalid arguments");
            Shutdown(help ? 0 : 2); return;
        }
        instance = new Mutex(true, @"Local\ReleaseDock-" + Environment.UserName, out var first);
        if (!first) { MessageBox.Show("GROUT is already running. Open it from the Windows tray.", "GROUT"); Shutdown(); return; }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        new MainWindow(startHidden: e.Args is ["--tray"]).Show();
    }
    private static void SavePreview(Window window, string output)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(output); encoder.Save(file);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
