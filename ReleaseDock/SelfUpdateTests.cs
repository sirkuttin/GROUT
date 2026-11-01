using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
namespace ReleaseDock;
internal static class SelfUpdateTests
{
    public static int Assertions { get; private set; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    private static string Metadata(string tag = "v9.0.0", bool preview = false) => JsonSerializer.Serialize(new { tag_name = tag, draft = false, prerelease = preview, assets = new[] { new { name = "GROUT-9.0.0-win-x64.exe", id = 1, state = "uploaded" }, new { name = "GROUT-9.0.0-SHA256SUMS.txt", id = 2, state = "uploaded" } } });
    public static void ValidateTestRoot(string root)
    {
        if (!Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "GROUT-selftest-"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid self-update test root.");
    }
    public static async Task TestHost(string root)
    {
        ValidateTestRoot(root);
        if (!string.Equals(Environment.ProcessPath, Path.Combine(root, "GROUT.exe"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Not the isolated test executable.");
        var folder = AppPaths.NewWork("self-update"); var package = Path.Combine(folder, "update.exe");
        File.Copy(Environment.ProcessPath!, package);
        string hash; using (var file = File.OpenRead(package)) hash = Convert.ToHexString(SHA256.HashData(file));
        using var helper = await SelfUpdateHandoff.Start(package, hash, folder, Path.Combine(root, "restarted.txt"));
        File.WriteAllText(Path.Combine(root, "helper.json"), JsonSerializer.Serialize(new { helper.Id, Folder = folder }));
    }
    public static async Task Run()
    {
        Assertions = 0;
        void Check(bool ok, string reason) { Assertions++; if (!ok) throw new Exception(reason); }
        void Fails(Action action, string reason) { var failed = false; try { action(); } catch { failed = true; } Check(failed, reason); }
        var root = Path.Combine(Path.GetTempPath(), "GROUT-selftest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string? helperFolder = null;
        try
        {
            var notices = typeof(SelfUpdater).Assembly.GetManifestResourceNames();
            Check(notices.Any(n => n.EndsWith("GROUT-LICENSE.txt")) && notices.Any(n => n.EndsWith("DOTNET-THIRD-PARTY-NOTICES.txt")), "Licenses embedded in portable EXE");
            var release = SelfUpdater.Parse(Metadata(), new Version(1, 0, 0, 0))!;
            Check(release.Version == new Version(9, 0, 0), "New stable release selected");
            Check(SelfUpdater.Parse(Metadata(), new Version(9, 0, 0, 0)) == null && SelfUpdater.Parse(Metadata(), new Version(10, 0, 0, 0)) == null, "Current/older releases never offered");
            Check(SelfUpdater.Parse(Metadata(preview: true), new Version(1, 0, 0, 0)) == null, "Preview excluded");
            Fails(() => SelfUpdater.Parse(Metadata("v9-beta"), new Version(1, 0, 0, 0)), "Unrecognized tag rejected");
            var bytes = "MZnew-version"u8.ToArray(); var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var sums = hash + "  " + release.ExeName + "\n";
            Check(SelfUpdater.Checksum(sums, release.ExeName) == hash, "Exact filename checksum read");
            Fails(() => SelfUpdater.Checksum(sums + sums, release.ExeName), "Duplicate checksum rejected");
            Fails(() => SelfUpdater.Checksum(sums, "other.exe"), "Unrelated checksum rejected");
            using var updater = new SelfUpdater(new Handler(request =>
            {
                Check(request.Headers.Authorization == null, "Public self-update uses no credentials");
                return new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith("/2") ? new StringContent(sums) : new ByteArrayContent(bytes) };
            }));
            var downloaded = await updater.Download(release, Path.Combine(root, "download"), CancellationToken.None);
            Check(File.ReadAllBytes(downloaded.Path).SequenceEqual(bytes), "Verified asset download");
            using var corrupt = new SelfUpdater(new Handler(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith("/2") ? new StringContent(sums) : new ByteArrayContent("MZcorrupt"u8.ToArray()) }));
            var rejected = false; try { await corrupt.Download(release, Path.Combine(root, "corrupt"), CancellationToken.None); } catch (IOException) { rejected = true; } Check(rejected, "Corrupt payload rejected before handoff");
            var target = Path.Combine(root, "target.exe"); File.WriteAllText(target, "old");
            SelfUpdateHandoff.Replace(downloaded.Path, target, hash, () => Check(File.ReadAllBytes(target).SequenceEqual(bytes), "New file installed before relaunch"));
            Check(!Directory.GetFiles(root, "*.grout-*").Any(), "Successful replacement retains no backup");
            File.WriteAllText(target, "old");
            Fails(() => SelfUpdateHandoff.Replace(downloaded.Path, target, hash, () => throw new IOException("Launch failed")), "Failed launch reported");
            Check(File.ReadAllText(target) == "old", "Failed launch rolls back executable");
            Fails(() => SelfUpdateHandoff.Replace(downloaded.Path, target, new string('0', 64), () => { }), "Bad helper checksum rejected");
            Check(File.ReadAllText(target) == "old", "Checksum failure preserves installed binary");
            Fails(() => SelfUpdateHandoff.ValidatePlanPath(Path.Combine(root, "plan.json")), "Out-of-workspace handoff rejected");
            if (new FileInfo(Environment.ProcessPath!).Length > 50 * 1024 * 1024)
            {
                var host = Path.Combine(root, "GROUT.exe"); File.Copy(Environment.ProcessPath!, host);
                var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--self-update-test-host"); start.ArgumentList.Add(root);
                using var parent = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await parent.WaitForExitAsync(timeout.Token); Check(parent.ExitCode == 0, "Isolated parent hands off successfully");
                var restarted = Path.Combine(root, "restarted.txt");
                for (var i = 0; i < 150 && !File.Exists(restarted); i++) await Task.Delay(100);
                Check(File.Exists(restarted), "Real helper replaces stopped EXE and restarts new copy without loading personal data");
                var restartPid = int.Parse(File.ReadAllLines(restarted)[1]);
                try { using var restartedProcess = Process.GetProcessById(restartPid); await restartedProcess.WaitForExitAsync(timeout.Token); } catch (ArgumentException) { }
                using var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "helper.json")));
                helperFolder = info.RootElement.GetProperty("Folder").GetString();
                try { using var helper = Process.GetProcessById(info.RootElement.GetProperty("Id").GetInt32()); await helper.WaitForExitAsync(timeout.Token); } catch (ArgumentException) { }
                Check(!Directory.GetFiles(root, "*.grout-*").Any(), "Real helper cleans temporary replacement files");
            }
        }
        finally
        {
            if (helperFolder != null) StorageCleanup.DeleteFolder(helperFolder, SelfUpdateHandoff.WorkRoot);
            Directory.Delete(root, true);
        }
    }
}
