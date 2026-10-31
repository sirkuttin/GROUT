using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace ReleaseDock;

public static class FeatureTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string message) { Assertions++; if (!value) throw new Exception(message); }
    private static void Fails(Action action, string message)
    {
        try { action(); } catch (IOException) { Assertions++; return; }
        throw new Exception(message);
    }
    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(respond()); }
    }
    private sealed class CancellingStream(Action cancel) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 3;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            buffer.Span[0] = 1; buffer.Span[1] = 2; buffer.Span[2] = 3; cancel(); return ValueTask.FromResult(3);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    public static async Task Run()
    {
        Assertions = 0;
        Check(InstalledVersions.Matches("1.0.36.0", "hub-1.0.36"), "Project tag prefix matches EXE version");
        Check(InstalledVersions.Matches("1.0.36", "release-v1.0.36"), "Project prefix and v prefix normalize");
        Check(InstalledVersions.Matches("1.0.36+build.42", "hub-1.0.36"), "Build metadata does not change numeric release");
        Check(!InstalledVersions.Matches("1.0.35.0", "hub-1.0.36"), "Different numeric releases remain different");
        Check(!InstalledVersions.Matches("1.0.36", "hub-1.0.36-beta"), "Prerelease suffix is not stripped");
        Check(!InstalledVersions.Matches("1.0.36-beta", "hub-1.0.36"), "Installed prerelease is not a stable release");
        Check(!InstalledVersions.Matches("1.0.36", "runtime-2.0-app-1.0.36"), "Ambiguous multiple-version tag is not guessed");
        var sameRelease = new TrackedApp { Installed = "1.0.36.0", Latest = "hub-1.0.36", Release = new("hub-1.0.36", "", "", []) };
        Check(sameRelease.Status == "Up to date" && !sameRelease.CanUpdate && !ReleaseFiles.TakeNotification(sameRelease, false), "Prefix-only mismatch causes neither Update nor notification");
        var now = DateTimeOffset.UtcNow;
        var downgrade = new TrackedApp { Installed = "2.0.0", Latest = "v1.9.0", Release = new("v1.9.0", "", "", []) };
        Check(downgrade.IsOlderRelease && !downgrade.CanUpdate && downgrade.Status == "Installed version is newer", "Numeric downgrade is never presented as an ordinary Update");
        downgrade.Installed = "v2.0.0-beta";
        Check(!downgrade.CanUpdate, "Switching from newer preview to older stable cannot silently downgrade");
        var unknownOrder = new TrackedApp { Installed = "nightly-blue", Latest = "nightly-green", Release = new("nightly-green", "", "", []) };
        Check(unknownOrder.Status == "Different release available", "Unknown release ordering is not described as newer");
        var partial = new TrackedApp { Installed = "v2", Latest = "v2", Release = new("v2", "", "", [new("app.exe", "", 3, null)]), PartialUpdate = true, AssetName = "app.exe" };
        Check(partial.Status == "Partial update — review files" && partial.CanUpdate, "Matching version source cannot hide partial application and retry remains available");
        var missingAsset = new TrackedApp { Installed = "1.0", Latest = "2.0", Release = new("2.0", "", "", []), AssetName = "old.exe" };
        Check(missingAsset.DisplayStatus == "No app downloads found", "Empty asset release explains unavailable update");
        missingAsset.Release.Assets.Add(new("new-name.exe", "", 3, null));
        Check(missingAsset.DisplayStatus == "Choose release download", "Unmatched configured download has actionable status");
        var settings = new Preferences();
        Check(settings.CheckHours == 24 && settings.IsDue(now), "New preferences default daily and due");
        settings.LastAttempt = now;
        Check(!settings.IsDue(now.AddHours(23)) && settings.IsDue(now.AddHours(24)), "Daily schedule boundaries");
        settings.CheckHours = 1;
        Check(!settings.IsDue(now.AddMinutes(59)) && settings.IsDue(now.AddHours(1)), "Hourly schedule boundaries");
        settings.CheckHours = 6;
        Check(!settings.IsDue(now.AddHours(5)) && settings.IsDue(now.AddHours(6)), "Six-hour schedule boundaries");
        settings.CheckHours = 0;
        Check(settings.NextCheck == null && !settings.IsDue(now.AddYears(1)), "Manual mode never auto-polls");
        settings.CheckHours = 1; settings.RetryAfter = now.AddHours(2);
        Check(!settings.IsDue(now.AddMinutes(90)), "Backoff postpones scheduled checks");
        var tracked = new TrackedApp { Repo = "a/b", Installed = "v1", Latest = "v2", Release = new("v2", "", "", []) };
        Check(ReleaseFiles.TakeNotification(tracked, false), "Notify first discovery");
        Check(!ReleaseFiles.TakeNotification(tracked, false), "No repeated notification");
        tracked.Latest = "v3"; tracked.Release = tracked.Release with { Tag = "v3" };
        Check(!ReleaseFiles.TakeNotification(tracked, true) && tracked.LastNotifiedTag == "v3", "Quiet mode records release without notification");
        tracked.Installed = "v3";
        Check(!ReleaseFiles.TakeNotification(tracked, false), "No notification when up to date");
        tracked.AssetName = "App-1.2.3-x64.exe";
        tracked.Release = new("v2.0.0", "", "", [new("App-2.0.0-x64.exe", "", 0, null), new("App-2.0.0-arm64.exe", "", 0, null)]);
        Check(ReleaseFiles.Match(tracked)?.Name == "App-2.0.0-x64.exe", "Asset matching preserves architecture");
        tracked.Release.Assets.Add(new("App-2.0.1-x64.exe", "", 0, null));
        Check(ReleaseFiles.Match(tracked) == null, "Ambiguous asset family requires selection");

        var root = Path.Combine(Path.GetTempPath(), "GROUT-features-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var preferencesPath = Path.Combine(root, "settings.json"); settings.Save(preferencesPath);
            Check(Preferences.Load(preferencesPath).CheckHours == 1 && Preferences.Load(preferencesPath).RetryAfter == settings.RetryAfter, "Schedule survives restart");
            File.WriteAllText(preferencesPath, "{\"CheckHours\":17}");
            Check(Preferences.Load(preferencesPath).CheckHours == 24, "Invalid interval repaired");
            var persisted = new Store(Path.Combine(root, "apps.json"));
            persisted.Save([tracked]);
            Check(persisted.Load()[0].Release?.Assets.Count == 3 && persisted.Load()[0].LastNotifiedTag == "v3", "Cached releases and notification dedup survive restart");
            tracked.Error = "Last check failed"; persisted.Save([tracked]);
            Check(persisted.Load()[0].Error == "Last check failed" && !persisted.Load()[0].CanUpdate, "Failed-check state survives restart");
            var destination = Path.Combine(root, "installed"); Directory.CreateDirectory(destination);
            var cancelledDestination = Path.Combine(root, "cancel.exe"); File.WriteAllText(cancelledDestination, "original");
            using (var cancellation = new CancellationTokenSource())
            using (var download = new ReleaseClient(new Handler(() => new(HttpStatusCode.OK) { Content = new StreamContent(new CancellingStream(cancellation.Cancel)) })))
            {
                try { await download.Download(new("app.exe", "https://github.com/a/b/releases/download/v2/app.exe", 3, null), cancelledDestination, new Progress<double>(), cancellation.Token); throw new Exception("Cancelled download succeeded"); }
                catch (OperationCanceledException) { }
                Check(File.ReadAllText(cancelledDestination) == "original" && Directory.GetFiles(root, "*.partial").Length == 0, "Mid-download cancellation cleans partial and preserves destination");
            }
            File.WriteAllText(Path.Combine(destination, "app.exe"), "old");
            File.WriteAllText(Path.Combine(destination, "settings.ini"), "my settings");
            File.WriteAllText(Path.Combine(destination, "keep.txt"), "absent from zip");
            string Zip(string name, params (string Path, string Body)[] entries)
            {
                var path = Path.Combine(root, name);
                using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
                foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Path).Open()); writer.Write(entry.Body); }
                return path;
            }
            var archive = Zip("good.zip", ("App-2.0/app.exe", "new"), ("App-2.0/settings.ini", "default"), ("App-2.0/new.txt", "new file"));
            var staging = FileUpdates.StageZip(archive, Path.Combine(root, "staging"));
            using (var cancelledExtraction = new CancellationTokenSource())
            {
                cancelledExtraction.Cancel();
                try { FileUpdates.StageZip(archive, Path.Combine(root, "cancelled-extraction"), cancelledExtraction.Token); throw new Exception("Cancelled extraction proceeded"); }
                catch (OperationCanceledException) { Assertions++; }
                Check(!Directory.Exists(Path.Combine(root, "cancelled-extraction")), "Cancelled extraction does not start staging");
            }
            var plan = FileUpdates.Plan(staging, destination, true);
            Check(plan.Single(f => f.Relative == "settings.ini").Apply == false, "Existing config preserved by default");
            Check(plan.Single(f => f.Relative == "app.exe").Existed, "Strip outer folder maps to installed file");
            var backup = FileUpdates.Apply(plan, destination, Path.Combine(root, "backup"));
            Check(File.ReadAllText(Path.Combine(destination, "app.exe")) == "new", "ZIP replaces selected app file");
            Check(File.ReadAllText(Path.Combine(destination, "settings.ini")) == "my settings", "ZIP keeps unchecked config");
            Check(File.ReadAllText(Path.Combine(destination, "keep.txt")) == "absent from zip", "ZIP does not delete absent files");
            Check(File.Exists(Path.Combine(destination, "new.txt")), "ZIP adds new files");
            FileUpdates.Restore(backup);
            Check(File.ReadAllText(Path.Combine(destination, "app.exe")) == "old" && !File.Exists(Path.Combine(destination, "new.txt")), "Restore reverts replacements and additions");
            Fails(() => FileUpdates.Restore(backup), "Double restore should fail");
            plan = FileUpdates.Plan(staging, destination, true);
            using (var locked = new FileStream(Path.Combine(destination, "app.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
                Fails(() => FileUpdates.Apply(plan, destination, Path.Combine(root, "locked-backup")), "Locked app must not be overwritten");
            Check(!File.Exists(Path.Combine(destination, "new.txt")), "Locked target preflight prevents other changes");
            var broken = new List<UpdateFile>
            {
                new() { Relative = "app.exe", Target = Path.Combine(destination, "app.exe"), Source = Path.Combine(staging, "App-2.0", "app.exe") },
                new() { Relative = "failed.txt", Target = Path.Combine(destination, "failed.txt"), Source = Path.Combine(root, "missing-source") }
            };
            Fails(() => FileUpdates.Apply(broken, destination, Path.Combine(root, "rollback-backup")), "Failed second write must roll back");
            Check(File.ReadAllText(Path.Combine(destination, "app.exe")) == "old", "Rollback restored first applied file");
            foreach (var bad in new[] { "../escape.txt", "/root.txt", "a/../../escape", "a\\..\\evil", "C:/evil", "NUL.txt", "dir/file.", "file:stream" })
                Fails(() => FileUpdates.SafeRelative(bad), "Unsafe archive name accepted: " + bad);
            Fails(() => FileUpdates.StageZip(Zip("traversal.zip", ("../escape.txt", "bad")), Path.Combine(root, "bad-stage")), "Zip traversal accepted");
            Fails(() => FileUpdates.StageZip(Zip("duplicates.zip", ("App.exe", "one"), ("app.exe", "two")), Path.Combine(root, "dup-stage")), "Case-insensitive duplicate accepted");
            Fails(() => FileUpdates.StageZip(Zip("collision.zip", ("folder", "file"), ("folder/child", "child")), Path.Combine(root, "collision-stage")), "File/directory collision accepted");
            var linkZip = Path.Combine(root, "link.zip");
            using (var zip = ZipFile.Open(linkZip, ZipArchiveMode.Create)) { var entry = zip.CreateEntry("link"); entry.ExternalAttributes = unchecked((int)0xA1FF0000); }
            Fails(() => FileUpdates.StageZip(linkZip, Path.Combine(root, "link-stage")), "Archive symlink accepted");
            var traversalPlan = new List<UpdateFile> { new() { Relative = "../outside", Target = Path.Combine(root, "outside"), Source = archive } };
            Fails(() => FileUpdates.Apply(traversalPlan, destination, Path.Combine(root, "outside-backup")), "Out-of-root update target accepted");
            var replace = new List<UpdateFile> { new() { Relative = "app.exe", Target = Path.Combine(destination, "app.exe"), Source = Path.Combine(staging, "App-2.0", "app.exe") } };
            var exeBackup = FileUpdates.Apply(replace, destination, Path.Combine(root, "exe-backup"));
            Check(File.ReadAllText(Path.Combine(destination, "app.exe")) == "new", "Standalone EXE replacement"); FileUpdates.Restore(exeBackup);
            Check(File.ReadAllText(Path.Combine(destination, "app.exe")) == "old", "Standalone EXE restore");
            var trackedRestore = new TrackedApp { Installed = "v1", Latest = "v2", AutoReconcile = false, InstallLocation = Path.Combine(destination, "app.exe"), InstalledProvenance = "Saved from release v1" };
            var snapshot = InstallationSnapshot.Capture(trackedRestore);
            var trackedBackup = FileUpdates.Apply(replace, destination, Path.Combine(root, "tracked-restore"), snapshot);
            trackedRestore.UpdateTarget = trackedRestore.InstallLocation; InstalledVersions.RecordAppliedExe(trackedRestore);
            Check(trackedRestore.Installed == "v2", "Portable replacement records newly applied release");
            var restoredSnapshot = FileUpdates.Restore(trackedBackup); restoredSnapshot!.Apply(trackedRestore); InstalledVersions.Reconcile(trackedRestore);
            Check(File.ReadAllText(trackedRestore.InstallLocation) == "old" && trackedRestore.Installed == "v1" && !trackedRestore.IsCurrent && trackedRestore.InstalledProvenance.Contains("v1"), "Rollback restores both old bytes and manual release provenance");
            var mismatched = new List<UpdateFile> { new() { Relative = "different", Target = Path.Combine(destination, "app.exe"), Source = archive } };
            Fails(() => FileUpdates.Apply(mismatched, destination, Path.Combine(root, "mismatch")), "Inconsistent mapping accepted");
            var json = JsonSerializer.Deserialize<Recovery>(File.ReadAllText(Path.Combine(exeBackup, "recovery.json")))!;
            json.State = "Applied"; json.Files[0].Target = Path.Combine(root, "outside");
            File.WriteAllText(Path.Combine(exeBackup, "recovery.json"), JsonSerializer.Serialize(json));
            Fails(() => FileUpdates.Restore(exeBackup), "Tampered recovery target accepted");
        }
        finally { Directory.Delete(root, true); }
        var rateHandler = new Handler(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return response;
        });
        using var client = new ReleaseClient(rateHandler);
        try { await client.Latest("a/b"); throw new Exception("Expected backoff"); } catch (InvalidOperationException) { }
        Check(client.RetryAfter > DateTimeOffset.UtcNow.AddMinutes(9), "Retry-After honored");
        try { await client.Latest("a/b"); throw new Exception("Expected active backoff"); } catch (InvalidOperationException) { }
        Check(rateHandler.Calls == 1, "Backoff prevents repeated network requests");
    }
}
