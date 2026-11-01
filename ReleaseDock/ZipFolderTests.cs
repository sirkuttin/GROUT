using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
namespace ReleaseDock;
internal static class ZipFolderTests
{
    public static int Assertions { get; private set; }
    private sealed class Handler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }
    public static async Task Run()
    {
        Assertions = 0;
        void Check(bool ok, string reason) { Assertions++; if (!ok) throw new Exception(reason); }
        var root = Path.Combine(Path.GetTempPath(), "GROUT-zipfolder-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var parent = Path.Combine(root, "apps");
            var target = SetupPaths.PortableZip(parent, "My App");
            Check(target == Path.Combine(parent, "My App"), "New ZIP destination nests app name under chosen parent");
            Check(SetupPaths.PortableZip(parent, "App/Name:Windows") == Path.Combine(parent, "App_Name_Windows"), "Invalid name characters sanitized");
            Check(SetupPaths.PortableZip(parent, "CON") == Path.Combine(parent, "App-CON"), "Windows reserved name handled");
            Check(SetupPaths.PortableZip(parent, "..") == Path.Combine(parent, "App"), "Traversal name cannot escape parent");
            var rejected = false; try { SetupPaths.PortableZip("relative", "App"); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "Relative parent rejected");
            var archive = Path.Combine(root, "app.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) using (var writer = new StreamWriter(zip.CreateEntry("App.exe").Open())) writer.Write("payload");
            var stage = FileUpdates.StageZip(archive, Path.Combine(root, "stage"));
            FileUpdates.Apply(FileUpdates.Plan(stage, target, false), target, Path.Combine(root, "backup"));
            Check(File.Exists(Path.Combine(target, "App.exe")) && !File.Exists(Path.Combine(parent, "App.exe")), "Extraction creates app folder and leaves parent free of payload files");
            var package = Path.Combine(root, "package.zip");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("App.exe").Open())) writer.Write("new app");
                using (var writer = new StreamWriter(zip.CreateEntry("settings.ini").Open())) writer.Write("new settings");
            }
            foreach (var scenario in new[] { (New: true, Existing: false), (New: true, Existing: true), (New: false, Existing: true) })
            {
                var scenarioRoot = Path.Combine(root, Guid.NewGuid().ToString("N")); var destination = Path.Combine(scenarioRoot, "app");
                if (scenario.Existing) { Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "App.exe"), "old"); File.WriteAllText(Path.Combine(destination, "settings.ini"), "old settings"); File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep"); }
                var work = Path.Combine(scenarioRoot, "work");
                string Allocate(string category) { var folder = Path.Combine(work, category, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
                var payload = File.ReadAllBytes(package); using var client = new ReleaseClient(new Handler(payload));
                var app = new TrackedApp { Repo = "test/zip", UpdateMethod = "Zip", UpdateTarget = destination, InstallLocation = destination, Installed = "v1", Latest = "v2", PartialUpdate = true };
                var reviews = 0;
                var prompts = new UpdatePrompts { ReviewFiles = (_, _, _, _) => { reviews++; throw new Exception("ZIP review must not be shown"); } };
                var owner = Ui.Window("Isolated ZIP test");
                try
                {
                    var result = await UpdateRunner.Run(owner, app, new Asset("app.zip", "https://github.com/test/zip/releases/download/v2/app.zip", payload.Length, null), client, (_, _) => { }, () => { }, newInstall: scenario.New, prompts: prompts, newWork: Allocate);
                    Check(result == UpdateOutcome.FilesApplied && reviews == 0, "Fresh/conflicting/update ZIP extraction completes without review");
                    Check(File.ReadAllText(Path.Combine(destination, "App.exe")) == "new app" && File.ReadAllText(Path.Combine(destination, "settings.ini")) == "new settings", "ZIP overwrites all included files, including settings");
                    Check(!app.PartialUpdate && app.LastBackup == "" && !Directory.GetFiles(work, "*", SearchOption.AllDirectories).Any(), "Temporary rollback/download/staging cleaned without a user step");
                    if (scenario.Existing) Check(File.ReadAllText(Path.Combine(destination, "keep.txt")) == "keep", "Files absent from ZIP remain unchanged");
                }
                finally { owner.Close(); }
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
