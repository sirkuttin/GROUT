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
            createJunction.ArgumentList.Add("-NoProfile");
            createJunction.ArgumentList.Add("-NonInteractive");
            createJunction.ArgumentList.Add("-Command");
            createJunction.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + junction.Replace("'", "''") + "' -Target '" + real.Replace("'", "''") + "' | Out-Null");
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
        }
        finally { Directory.Delete(root, true); }
    }
}
