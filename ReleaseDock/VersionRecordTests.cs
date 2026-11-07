using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Windows;

namespace ReleaseDock;

public static class VersionRecordTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private sealed class Handler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }
    public static async Task Run()
    {
        Assertions = 0;
        var root = Path.Combine(Path.GetTempPath(), "GROUT-record-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var owner = new Window();
        string Work(string category) { var path = Path.Combine(root, "work", category, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        try
        {
            var zip = Path.Combine(root, "plugin.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("plugins/remote-stash.dll").Open()); writer.Write("plugin payload"); }
            var payload = File.ReadAllBytes(zip); using var client = new ReleaseClient(new Handler(payload));
            var target = Path.Combine(root, "remote-stash"); Directory.CreateDirectory(target);
            var manifest = Path.Combine(target, "manifest.json"); File.WriteAllText(manifest, """{"name":"remote-stash-v2.3.4"}""");
            var app = new TrackedApp { Repo = "https://d2rloader.net/hub/plugins/remote-stash", UpdateMethod = "Zip", UpdateTarget = target, InstallLocation = manifest, Installed = "2.3.4", Latest = "2.3.5", AutoReconcile = true };
            var asset = new Asset("plugin.zip", "https://d2rloader.net/api/v1/hub/releases/example/download", payload.Length, null, "d2rloader.net");
            await UpdateRunner.Run(owner, app, asset, client, (_, _) => { }, () => { }, newWork: Work);
            Check(app.IsCurrent && app.Installed == "2.3.5" && app.VersionRecord != null && app.UpdateState == "", "ZIP missing manifest records the actual applied release instead of trusting leftover old manifest");
            Check(File.ReadAllText(manifest).Contains("2.3.4") && Directory.GetFiles(target).Length == 1, "Publisher manifest is untouched and no GROUT version file is added to app folder");
            var store = new Store(Path.Combine(root, "data", "apps.json")); store.Save([app]);
            var recordPath = InstalledReleaseRecords.FilePath(Path.Combine(root, "data"), app.VersionRecordId);
            Check(File.Exists(recordPath) && new FileInfo(recordPath).Length < 4096, "Small dedicated version JSON stored alongside GROUT app data");
            app = store.Load().Single(); InstalledVersions.Reconcile(app);
            Check(app.IsCurrent && !app.LocalError && app.VersionRecord != null, "Fallback survives restart and ignores unchanged stale native manifest");
            app.Latest = "2.3.6"; await UpdateRunner.Run(owner, app, asset, client, (_, _) => { }, () => { }, newWork: Work); store.Save([app]);
            Check(app.Installed == "2.3.6" && Directory.GetFiles(Path.GetDirectoryName(recordPath)!, "*.json").Length == 1, "Next ZIP update replaces version record without accumulating history");
            var before = app.VersionRecord;
            using (var broken = new ReleaseClient(new Handler([1, 2, 3])))
            {
                app.Latest = "2.3.7";
                try { await UpdateRunner.Run(owner, app, asset with { Size = 3 }, broken, (_, _) => { }, () => { }, newWork: Work); throw new Exception("Broken ZIP accepted"); }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { }
            }
            Check(app.VersionRecord == before && app.Installed == "2.3.6", "Failed extraction preserves previous applied release record");
            File.WriteAllText(manifest, """{"name":"remote-stash-v2.3.7"}"""); InstalledVersions.Reconcile(app); store.Save([app]);
            Check(app.VersionRecord?.Evidence == "Applied" && app.Installed == "2.3.6" && app.VersionRecord.NativeVersion == "2.3.7" && File.Exists(recordPath), "An old manifest outside the applied payload updates supporting evidence without overriding the known applied release");
            var exe = Path.Combine(root, "portable.exe"); File.WriteAllBytes(exe, PortableExeFixture.Bytes());
            var portable = new TrackedApp { Repo = "test/portable", Latest = "v8.0", UpdateMethod = "ReplaceExe", UpdateTarget = exe };
            InstalledVersions.RecordAppliedExe(portable); store.Save([portable]);
            Check(portable.IsCurrent && portable.VersionRecord != null && portable.AutoReconcile, "Metadata-free portable EXE gets persistent fallback tracking");
            File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, exe, true); InstalledVersions.Reconcile(portable); store.Save([portable]);
            Check(portable.VersionRecord?.Evidence == "Detected" && InstalledVersions.Matches(portable.Installed, SelfUpdater.CurrentLabel) && Directory.GetFiles(Path.Combine(root, "data", "versions")).Length == 1, "Real EXE version updates the persistent record and retains the prior applied-release evidence");
            File.WriteAllBytes(exe, PortableExeFixture.Bytes()); InstalledVersions.RecordAppliedExe(portable); File.Delete(exe); InstalledVersions.Reconcile(portable);
            Check(portable.InstallationMissing && portable.LocalError, "A fallback record cannot hide removed app files");
            IReadOnlyList<InstalledRegistration> registered = [];
            var folder = Path.Combine(root, "Tool"); var toolExe = Path.Combine(folder, "Tool.exe");
            var installer = new TrackedApp { Repo = "test/tool", Latest = "v3.0", AssetName = "ToolSetup-3.0.exe", UpdateMethod = "Installer" };
            using var installerClient = new ReleaseClient(new Handler([1, 2, 3]));
            var prompts = new UpdatePrompts { RunInstaller = (_, _) => true, ExecuteInstaller = _ => { Directory.CreateDirectory(folder); File.WriteAllBytes(toolExe, PortableExeFixture.Bytes()); registered = [new("test-tool-registration", "Tool", "", folder, toolExe, "")]; return Task.CompletedTask; } };
            await UpdateRunner.Run(owner, installer, new("ToolSetup-3.0.exe", "https://github.com/test/tool/setup.exe", 3, null), installerClient, (_, _) => { }, () => { }, newInstall: true, prompts: prompts, newWork: Work, discovery: new InstallDiscovery(() => registered));
            Check(installer.IsCurrent && installer.VersionRecord != null && InstallDiscovery.CanFinish(installer), "Successful installer with located files but no version records release and completes setup");
            var failed = new TrackedApp { Repo = "test/tool", Latest = "v4.0", AssetName = installer.AssetName, UpdateMethod = "Installer", PendingInstallerRelease = "v4.0" };
            try { await UpdateRunner.Run(owner, failed, new("ToolSetup-3.0.exe", "https://github.com/test/tool/setup.exe", 3, null), installerClient, (_, _) => { }, () => { }, prompts: new UpdatePrompts { RunInstaller = (_, _) => true, ExecuteInstaller = _ => throw new IOException("Cancelled installer") }, newWork: Work); throw new Exception("Failed installer accepted"); } catch (IOException) { }
            Check(failed.VersionRecord == null && failed.PendingInstallerRelease.Length == 0 && failed.Installed.Length == 0, "Failed installer never creates a version claim");
            store.Save([installer]); var installerRecordPath = InstalledReleaseRecords.FilePath(Path.Combine(root, "data"), installer.VersionRecordId);
            var loaded = store.Load().Single(); Check(loaded.VersionRecord?.Method == "Installer", "Installer record survives restart");
            store.Save([]); Check(!File.Exists(installerRecordPath), "Stopping tracking/uninstalling removes orphan version record");
            var noInstall = new TrackedApp { Repo = "test/tool", Latest = "v4.0", InstallLocation = folder };
            InstalledVersions.RecordAppliedInstaller(noInstall);
            Check(noInstall.VersionRecord == null, "Tracking an existing app alone never fabricates an installation record");
            var nativeTarget = Path.Combine(root, "Reimagined"); Directory.CreateDirectory(nativeTarget);
            var info = Path.Combine(nativeTarget, "modinfo.json"); File.WriteAllText(info, """{"name":"Reimagined","version":"3.1.0"}""");
            var nativeZip = Path.Combine(root, "stale-native.zip");
            using (var archive = ZipFile.Open(nativeZip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("modinfo.json").Open())) writer.Write("""{"name":"Reimagined","version":"3.1.0"}""");
                using (var writer = new StreamWriter(archive.CreateEntry("data.txt").Open())) writer.Write("3.1.1 release payload");
            }
            var nativePayload = File.ReadAllBytes(nativeZip); using var nativeClient = new ReleaseClient(new Handler(nativePayload));
            var nativeApp = new TrackedApp { Repo = "test/reimagined", UpdateMethod = "Zip", UpdateTarget = nativeTarget, InstallLocation = info, VersionSourceOverride = "modinfo.json", Installed = "3.1.0", Latest = "3.1.1", AutoReconcile = true };
            await UpdateRunner.Run(owner, nativeApp, new("mod.zip", "https://github.com/test/reimagined/mod.zip", nativePayload.Length, null), nativeClient, (_, _) => { }, () => { }, newWork: Work);
            Check(nativeApp.IsCurrent && nativeApp.VersionRecord?.AppliedVersion == "3.1.1" && nativeApp.VersionRecord.NativeVersion == "3.1.0" && nativeApp.UpdateState.Length == 0, "Reimagined-style stale version inside the new ZIP cannot override the release actually applied");
            store.Save([nativeApp]); nativeApp = store.Load().Single(); InstalledVersions.Reconcile(nativeApp);
            Check(nativeApp.IsCurrent && nativeApp.VersionRecord != null && File.ReadAllText(info).Contains("3.1.0"), "Applied-release truth survives restart without editing publisher metadata");
            File.WriteAllText(info, """{"name":"Reformatted publisher manifest","version":"3.1.0"}"""); InstalledVersions.Reconcile(nativeApp);
            Check(nativeApp.Installed == "3.1.1" && nativeApp.VersionRecord?.Evidence == "Applied", "Reformatting stale metadata does not downgrade the applied release");
            File.WriteAllText(info, """{"version":"3.1.1"}"""); nativeApp.Latest = "3.1.1";
            InstalledVersions.RecordAppliedZip(nativeApp, [new UpdateFile { Target = info, Relative = "modinfo.json", Source = info }]); store.Save([nativeApp]);
            Check(nativeApp.VersionRecord?.Version == "3.1.1" && nativeApp.VersionRecord.NativeVersion == "3.1.1" && File.Exists(InstalledReleaseRecords.FilePath(Path.Combine(root, "data"), nativeApp.VersionRecordId)), "Even readable matching metadata receives a permanent installation record");
        }
        finally { owner.Close(); Directory.Delete(root, true); }
    }
}
