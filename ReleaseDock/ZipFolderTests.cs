using System.IO;
using System.IO.Compression;
namespace ReleaseDock;
internal static class ZipFolderTests
{
    public static int Assertions { get; private set; }
    public static void Run()
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
        }
        finally { Directory.Delete(root, true); }
    }
}
