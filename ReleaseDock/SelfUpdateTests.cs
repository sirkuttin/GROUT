using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using ReleaseDock.Json;
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
        if (!string.Equals(Framework.GetDirectoryName(Framework.ProcessPath), root, StringComparison.OrdinalIgnoreCase) || !(Path.GetFileName(Framework.ProcessPath!).Equals("GROUT.exe", StringComparison.OrdinalIgnoreCase) || SelfUpdateMigration.Destination(Framework.ProcessPath!) != Framework.ProcessPath)) throw new IOException("Not the isolated test executable.");
        var folder = AppPaths.NewWork("self-update"); var package = Path.Combine(folder, "update.exe");
        File.Copy(Framework.ProcessPath!, package);
        string hash; using (var file = File.OpenRead(package)) hash = Framework.Hex(Framework.Hash(file));
        using var helper = await SelfUpdateHandoff.Start(package, hash, folder, Path.Combine(root, "restarted.txt"), File.Exists(Path.Combine(root, "simulate-startup-failure")));
        File.WriteAllText(Path.Combine(root, "helper.json"), JsonSerializer.Serialize(new { helper.Id, Folder = folder, Plan = File.ReadAllText(Path.Combine(folder, "plan.json")) }));
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
            Check(notices.Any(n => n.EndsWith("GROUT-LICENSE.txt")) && notices.Any(n => n.EndsWith("WINDOWS-FRAMEWORK.txt")), "Licenses embedded in portable EXE");
            var release = SelfUpdater.Parse(Metadata(), new Version(1, 0, 0, 0))!;
            Check(release.Version == new Version(9, 0, 0), "New stable release selected");
            var stableMetadata = Metadata().Replace("\"assets\":[", "\"assets\":[{\"name\":\"GROUT.exe\",\"id\":3,\"state\":\"uploaded\"},");
            Check(SelfUpdater.Parse(stableMetadata, new Version(1, 0, 0, 0))?.ExeName == "GROUT.exe", "Stable filename preferred over legacy compatibility asset");
            Check(release.ExeName == "GROUT-9.0.0-win-x64.exe", "Older releases remain supported");
            Check(SelfUpdater.Parse(Metadata(), new Version(9, 0, 0, 0)) == null && SelfUpdater.Parse(Metadata(), new Version(10, 0, 0, 0)) == null, "Current/older releases never offered");
            Check(SelfUpdater.Parse(Metadata(preview: true), new Version(1, 0, 0, 0)) == null, "Preview excluded");
            Fails(() => SelfUpdater.Parse(Metadata("v9-beta"), new Version(1, 0, 0, 0)), "Unrecognized tag rejected");
            var bytes = System.Text.Encoding.UTF8.GetBytes("MZnew-version"); var hash = Framework.Hex(Framework.Hash(bytes)).ToLowerInvariant();
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
            using var corrupt = new SelfUpdater(new Handler(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith("/2") ? new StringContent(sums) : new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("MZcorrupt")) }));
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
            var legacy = Path.Combine(root, "GROUT-0.5.0-win-x64.exe"); File.WriteAllText(legacy, "old");
            var stable = SelfUpdateMigration.Destination(legacy);
            Check(stable == Path.Combine(root, "GROUT.exe") && SelfUpdateMigration.Destination(target) == target, "Versioned name migrates; custom filenames preserved");
            Check(SelfUpdateMigration.RepointStartup("\"" + legacy + "\" --tray", legacy, stable) == "\"" + stable + "\" --tray", "Startup path migrates while preserving arguments");
            Check(SelfUpdateMigration.RepointStartup("\"" + target + "\" --tray", legacy, stable) == "\"" + target + "\" --tray", "Unrelated startup entry unchanged");
            var oldPlan = JsonSerializer.Deserialize<SelfUpdatePlan>(JsonSerializer.Serialize(new { ParentPid = 1, ParentStart = 1L, Target = legacy, Package = downloaded.Path, Hash = hash, TestReport = "" }))!;
            Check(oldPlan.OriginalTarget == "" && !oldPlan.TestFailStartup, "GROUT 0.5.0 handoff plans remain compatible");
            var shortcutRoots = new[] { "Desktop", "StartMenu", "UserPinned" }.Select(n => Path.Combine(root, n)).ToArray();
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            try
            {
                foreach (var shortcutRoot in shortcutRoots)
                {
                    var linkFolder = shortcutRoot.EndsWith("UserPinned") ? Path.Combine(shortcutRoot, "TaskBar") : shortcutRoot; Directory.CreateDirectory(linkFolder);
                    dynamic link = shell.CreateShortcut(Path.Combine(linkFolder, "GROUT.lnk"));
                    try { link.TargetPath = legacy; link.Arguments = "--tray"; link.Save(); } finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); }
                }
                SelfUpdateMigration.UpdateShortcuts(legacy, stable, shortcutRoots);
                foreach (var shortcutRoot in shortcutRoots)
                {
                    var linkFolder = shortcutRoot.EndsWith("UserPinned") ? Path.Combine(shortcutRoot, "TaskBar") : shortcutRoot;
                    dynamic link = shell.CreateShortcut(Path.Combine(linkFolder, "GROUT.lnk"));
                    try { Check((string)link.TargetPath == stable && (string)link.Arguments == "--tray", "Matching desktop/start-menu/taskbar shortcut migrated with arguments intact"); } finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); }
                }
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
            Fails(() => SelfUpdateMigration.Install(downloaded.Path, legacy, stable, hash, () => throw new IOException("Launch failed")), "Migration launch failure reported");
            Check(File.ReadAllText(legacy) == "old" && !File.Exists(stable), "Migration failure preserves original and removes incomplete destination");
            File.WriteAllText(stable, "other install");
            Fails(() => SelfUpdateMigration.CheckDestination(legacy, stable), "Existing stable copy not overwritten");
            Check(File.ReadAllText(stable) == "other install", "Existing copy unchanged"); File.Delete(stable);
            var oldHash = Framework.Hex(Framework.Hash(File.ReadAllBytes(legacy)));
            SelfUpdateMigration.Install(downloaded.Path, legacy, stable, hash, () => { });
            var migration = new SelfUpdatePlan(0, 0, stable, downloaded.Path, hash, OriginalTarget: legacy, OriginalHash: oldHash);
            SelfUpdateMigration.RemoveOriginal(migration); Check(!File.Exists(legacy) && File.Exists(stable), "Old filename removed only after successful migration");
            File.WriteAllText(legacy, "changed"); SelfUpdateMigration.RemoveOriginal(migration); Check(File.Exists(legacy), "Changed original copy is not deleted");
            File.Delete(legacy); File.Delete(stable);
            if (File.Exists(Framework.ProcessPath))
            {
                foreach (var (hostName, failStartup) in new[] { ("GROUT.exe", false), ("GROUT-0.5.0-win-x64.exe", false), ("GROUT.exe", true), ("GROUT-0.5.0-win-x64.exe", true) })
                {
                var scenario = Path.Combine(root, (failStartup ? "fail-" : "") + hostName.Replace(".exe", "")); Directory.CreateDirectory(scenario);
                if (failStartup) File.WriteAllText(Path.Combine(scenario, "simulate-startup-failure"), "test");
                var host = Path.Combine(scenario, hostName); File.Copy(Framework.ProcessPath!, host);
                var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true };
                start.AddArgument("--self-update-test-host"); start.AddArgument(scenario);
                using var parent = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await parent.WaitForExitAsync(timeout.Token); Check(parent.ExitCode == 0, "Isolated parent hands off successfully");
                using var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(scenario, "helper.json")));
                helperFolder = info.RootElement.GetProperty("Folder").GetString();
                if (failStartup)
                {
                    try { using var failedHelper = Process.GetProcessById(info.RootElement.GetProperty("Id").GetInt32()); await failedHelper.WaitForExitAsync(timeout.Token); } catch (ArgumentException) { }
                    Check(File.Exists(Path.Combine(helperFolder!, "error.txt")) && File.Exists(host), "Child startup failure restores original EXE");
                    Check(hostName == "GROUT.exe" || !File.Exists(Path.Combine(scenario, "GROUT.exe")), "Failed migration removes unconfirmed new filename");
                    StorageCleanup.DeleteFolder(helperFolder!, SelfUpdateHandoff.WorkRoot); helperFolder = null;
                    continue;
                }
                var restarted = Path.Combine(scenario, "restarted.txt");
                for (var i = 0; i < 150 && !File.Exists(restarted); i++) await Task.Delay(100);
                Check(File.Exists(restarted), "Real helper replaces stopped EXE and restarts new copy without loading personal data");
                var restartPid = int.Parse(File.ReadAllLines(restarted)[1]);
                try { using var restartedProcess = Process.GetProcessById(restartPid); await restartedProcess.WaitForExitAsync(timeout.Token); } catch (ArgumentException) { }
                try { using var helper = Process.GetProcessById(info.RootElement.GetProperty("Id").GetInt32()); await helper.WaitForExitAsync(timeout.Token); } catch (ArgumentException) { }
                var completedPlan = JsonSerializer.Deserialize<SelfUpdatePlan>(info.RootElement.GetProperty("Plan").GetString()!)!;
                SelfUpdateMigration.RemoveOriginal(completedPlan);
                Check(File.Exists(Path.Combine(scenario, "GROUT.exe")) && (hostName == "GROUT.exe" || !File.Exists(host)), "Real helper restarts at stable filename and removes old versioned copy");
                Check(!Directory.GetFiles(scenario, "*.grout-*").Any(), "Real helper cleans temporary replacement files");
                StorageCleanup.DeleteFolder(helperFolder!, SelfUpdateHandoff.WorkRoot); helperFolder = null;
                }
            }
        }
        finally
        {
            if (helperFolder != null) StorageCleanup.DeleteFolder(helperFolder, SelfUpdateHandoff.WorkRoot);
            Directory.Delete(root, true);
        }
    }
}
