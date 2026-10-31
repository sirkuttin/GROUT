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
            foreach (var path in new[] { Path.GetPathRoot(root)!, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.Combine(root, "apps"), "relative" })
            {
                var rejected = false; try { AppUninstall.PortableFolder(path); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Shared/root/relative deletion blocked: " + path);
            }
            var command = AppUninstall.Command("MsiExec.exe /I{12345678-1234-1234-1234-123456789012}");
            Check(command.Arguments.StartsWith("/X{"), "MSI install command converted to uninstall");
            var unsupported = false; try { AppUninstall.Command("cmd.exe /c echo test"); } catch (InvalidOperationException) { unsupported = true; }
            Check(unsupported, "Bare shell command rejected");
            app.AppFolder = root;
            var entry = new InstalledRegistration("id", "Hub", "1", root, "", "uninstall.exe");
            Check(AppUninstall.Registered(app, [entry]) == entry, "Exact folder registration matched");
            Check(AppUninstall.Registered(app, [entry, entry with { Id = "other", Uninstall = "different.exe" }]) == null, "Ambiguous uninstallers not chosen");
            Check(AppUninstall.Registered(app, [entry with { Location = root + "-other" }]) == null, "Similar directory not matched");
        }
        finally { Directory.Delete(root, true); }
    }
}
