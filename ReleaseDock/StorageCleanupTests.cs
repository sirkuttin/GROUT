using System.IO;
namespace ReleaseDock;
internal static class StorageCleanupTests
{
    public static int Assertions { get; private set; }
    public static void Run()
    {
        Assertions = 0;
        void Check(bool ok, string reason) { Assertions++; if (!ok) throw new Exception(reason); }
        var root = Path.Combine(Path.GetTempPath(), "GROUT-clean-" + Guid.NewGuid().ToString("N"));
        string Make(string category, string name, string content = "test") { var dir = Path.Combine(root, category, name); Directory.CreateDirectory(dir); var file = Path.Combine(dir, category == "backups" ? "recovery.json" : "app.exe"); File.WriteAllText(file, content); return file; }
        try
        {
            var stale = Make("downloads", "old"); var pending = Make("downloads", "pending"); var current = Make("downloads", "current"); var staged = Make("staging", "zip");
            var safe = Make("backups", "done", "{\"State\":\"Applied\"}"); var interrupted = Make("backups", "interrupted", "{\"State\":\"Applying\"}");
            var app = new TrackedApp { Installed = "v1", Latest = "v2", DownloadedPath = pending };
            var done = new TrackedApp { Installed = "v2", Latest = "v2", DownloadedPath = current, LastBackup = Framework.GetDirectoryName(safe)! };
            StorageCleanup.Sweep([app, done], root);
            Check(!File.Exists(stale) && !File.Exists(current), "Obsolete/completed downloads removed");
            Check(File.Exists(pending), "Pending installation payload retained");
            Check(!File.Exists(staged), "Staging removed");
            Check(!File.Exists(safe) && done.LastBackup == "", "Completed backup removed and reference cleared");
            Check(File.Exists(interrupted), "Interrupted recovery preserved");
            Check(done.DownloadedPath == "", "Removed download reference cleared");
            StorageCleanup.DeleteFolder(root, Path.Combine(root, "staging")); Check(Directory.Exists(root), "Out-of-root removal blocked");
            var installed = Make("downloads", "installed");
            var versionSource = Make("staging", "version-source");
            var updateTarget = Make("backups", "live-update-target", "{\"State\":\"Applied\"}");
            var protectedApps = new[]
            {
                new TrackedApp { Installed = "v1", Latest = "v1", InstallLocation = installed, DownloadedPath = installed },
                new TrackedApp { Installed = "v1", Latest = "v1", AppFolder = Framework.GetDirectoryName(versionSource)!, VersionSourceOverride = Path.GetFileName(versionSource) },
                new TrackedApp { Installed = "v1", Latest = "v1", UpdateTarget = updateTarget }
            };
            StorageCleanup.Sweep(protectedApps, root);
            Check(File.Exists(installed), "Current app installed inside download cache is preserved");
            Check(File.Exists(versionSource), "Configured relative version source inside staging cache is preserved");
            Check(File.Exists(updateTarget), "Live update target inside a completed-backup directory is preserved");
            var protectedParent = Path.Combine(root, "downloads");
            var nested = Make("downloads", "app-child");
            StorageCleanup.Sweep([new TrackedApp { AppFolder = protectedParent }], root);
            Check(File.Exists(nested), "Cleanup does not remove a descendant of a tracked app folder");
            var relativeSource = Make("downloads", "relative-source");
            var previousDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Framework.GetDirectoryName(relativeSource)!);
                StorageCleanup.Sweep([new TrackedApp { InstallLocation = "app.exe", Installed = "v1", Latest = "v1" }], root);
                Check(File.Exists(relativeSource), "Legacy relative install path is resolved before cleanup protection");
            }
            finally { Directory.SetCurrentDirectory(previousDirectory); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
