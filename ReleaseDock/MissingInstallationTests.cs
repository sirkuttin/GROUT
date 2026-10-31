using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace ReleaseDock;
internal static class MissingInstallationTests
{
    public static int Assertions { get; private set; }
    public static void Run()
    {
        Assertions = 0;
        void Check(bool ok, string reason) { Assertions++; if (!ok) throw new Exception(reason); }
        var root = Path.Combine(Path.GetTempPath(), "GROUT-missing-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "Gram.exe"); File.WriteAllText(exe, "test");
            var app = new TrackedApp { Repo = "GramEditor/gram", InstallLocation = exe, Installed = "3.4.1", Latest = "3.4.1", UpdateState = "Installed version detected" };
            InstalledVersions.Reconcile(app); Check(!app.InstallationMissing, "Existing manual installation present");
            File.Delete(exe); InstalledVersions.Reconcile(app);
            Check(app.InstallationMissing && app.Status == "App not found", "External uninstall detected during reconciliation even in manual mode");
            Check(app.UpdateState == "" && app.Installed == "3.4.1" && app.Versions.StartsWith("Last known version"), "Stale success cleared and historical version preserved");
            app.Latest = "3.5.0"; app.Release = new("3.5.0", "", "", []); Check(!app.CanUpdate, "Missing install cannot update");
            var window = new MainWindow(preview: true);
            try
            {
                ((ListBox)window.FindName("Library")).SelectedItem = null;
                var library = (ListBox)window.FindName("Library"); ((System.Collections.ObjectModel.ObservableCollection<TrackedApp>)library.ItemsSource).Add(app); library.SelectedItem = app;
                Check(((System.Windows.Controls.Border)window.FindName("MissingBanner")).Visibility == Visibility.Visible, "Prominent missing banner visible");
                Check(((Button)window.FindName("FindInstallationButton")).IsEnabled, "Find installation available");
                Check(!((Button)window.FindName("UninstallButton")).IsEnabled && !((Expander)window.FindName("VersionSection")).IsEnabled && !((Button)window.FindName("UpdateButton")).IsEnabled, "Unsafe/misleading actions disabled");
            }
            finally { window.Close(); }
            File.WriteAllText(exe, "test"); InstalledVersions.Reconcile(app); Check(!app.InstallationMissing && !app.LocalError, "Restored location clears missing state");
            app.AutoReconcile = true; InstalledVersions.Reconcile(app); Check(!app.InstallationMissing && app.LocalError, "Unreadable version differs from missing installation");
            app.InstallLocation = ""; app.AutoReconcile = false; InstalledVersions.Reconcile(app); Check(!app.InstallationMissing, "Tracking without a location does not imply missing app");
        }
        finally { Directory.Delete(root, true); }
    }
}
