using System.IO;
using System.Net;
using System.Net.Http;
using ReleaseDock.Json;

namespace ReleaseDock;

internal static class NexusTests
{
    private static IEnumerable<T> Find<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent).OfType<System.Windows.DependencyObject>()) { if (child is T found) yield return found; foreach (var descendant in Find<T>(child)) yield return descendant; }
    }
    public static int Assertions { get; private set; }
    private static void Check(bool value, string message) { Assertions++; if (!value) throw new Exception(message); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(respond(request)); }
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json") };
    private const string Mod = """{"name":"Test Tool","version":"2.0.0"}""";
    private const string Files = """{"files":[{"file_id":11,"file_name":"tool-old.zip","version":"1.0.0","category_id":4},{"file_id":22,"file_name":"tool-169-2-0-0-123.zip","version":"2.0.0","category_id":1,"is_primary":true,"size":1024,"uploaded_timestamp":100,"changelog_html":"<p>Fixed &amp; improved</p>"},{"file_id":33,"file_name":"optional.zip","version":"9.0","category_id":3,"size_in_bytes":3,"uploaded_timestamp":200},{"file_id":44,"file_name":"removed.zip","version":"99","category_id":6},{"file_id":55,"file_name":"archived.zip","version":"99","category_id":7},{"file_id":66,"file_name":"../unsafe.zip","version":"1","category_id":1},{"file_id":77,"file_name":"manual.rar","version":"1","category_id":5}],"file_updates":[{"old_file_id":11,"new_file_id":20,"uploaded_timestamp":90},{"old_file_id":20,"new_file_id":22,"uploaded_timestamp":100}]}""";
    internal static async Task Run()
    {
        Assertions = 0;
#if !NEXUS_TESTING
        NexusMods.TestMode = false;
        try
        {
            var requests = new Handler(_ => throw new Exception("Disabled integration must not contact Nexus"));
            using var disabled = new NexusMods(requests, () => new NexusAccount("test", "Test", true));
            var blocked = false;
            try { await disabled.Verify(Repositories.Resolve("https://nexusmods.com/game/mods/1")); } catch (InvalidOperationException ex) { blocked = ex.Message.Contains("not available"); }
            Check(blocked && requests.Calls == 0 && !disabled.CanDownload, "Public build disables Nexus before credentials/network access");
            Check(Repositories.Parse("https://nexusmods.com/game/mods/1").Length > 0, "Disabled Nexus entries still load without breaking the library");
        }
        finally { NexusMods.TestMode = true; }
#endif
        const string link = "https://www.nexusmods.com/diablo2resurrected/mods/169?tab=files&file_id=22";
        var source = Repositories.Resolve(link);
        Check(source.Host == "nexusmods.com" && source.Path == "diablo2resurrected/mods/169", "Nexus browser URLs normalize to mod identity");
        Check(Repositories.Parse("nexusmods.com/diablo2resurrected/mods/169") == source.Key && Repositories.Parse(source.WebUrl) == source.Key, "Nexus hostname variants deduplicate");
        Check(Repositories.Parse("https://www.nexusmods.com/games/diablo2resurrected/mods/169") == source.Key, "Nexus games-prefixed links share the same identity");
        Check(source.ApiUrl == "https://api.nexusmods.com/v1/games/diablo2resurrected/mods/169" && source.ReleasesUrl.EndsWith("?tab=files"), "Nexus API and files links resolve");
        foreach (var invalid in new[] { "https://www.nexusmods.com/game/mods/0", "https://www.nexusmods.com/game/mods/-1", "https://www.nexusmods.com/game/mods/1/other", "https://www.nexusmods.com/game/mods/999999999999999999999999", "https://www.nexusmods.com.evil.org/game/mods/1", "https://x@nexusmods.com/game/mods/1" })
        { var failed = false; try { Repositories.Parse(invalid); } catch (ArgumentException) { failed = true; } Check(failed, "Malformed or untrusted Nexus URLs rejected"); }
        using var mod = JsonDocument.Parse(Mod); using var files = JsonDocument.Parse(Files);
        var release = NexusMods.Parse(source, mod.RootElement, files.RootElement, 0);
        Check(release.Tag == "2.0.0" && release.SelectedFileId == 22 && release.Title == "Test Tool", "Primary file determines latest version instead of newer optional files");
        Check(release.Assets.Count == 3 && release.Assets.All(a => a.FileId is 22 or 33 or 77), "Old, deleted, archived and unsafe files excluded");
        Check(release.Assets.Single(a => a.FileId == 22).Size == -1 && release.Assets.Single(a => a.FileId == 33).Size == 3, "Rounded KB is never treated as exact download bytes");
        Check(release.Notes.Contains("Fixed & improved") && !release.Notes.Contains("<p>"), "File changelog rendered as plain text");
        Check(NexusMods.Parse(source, mod.RootElement, files.RootElement, 11).SelectedFileId == 22, "Transitive file update chains followed");
        Check(NexusMods.Parse(source, mod.RootElement, files.RootElement, 33).Tag == "9.0", "Selected optional file keeps its own version");
        var missing = NexusMods.Parse(source, mod.RootElement, files.RootElement, 999);
        Check(missing.SelectedFileId == 0 && missing.Notes.Contains("no longer available"), "Missing file is not silently switched to primary");
        using var circular = JsonDocument.Parse("""{"files":[],"file_updates":[{"old_file_id":1,"new_file_id":2},{"old_file_id":2,"new_file_id":1}]}""");
        var cycle = false; try { NexusMods.Parse(source, mod.RootElement, circular.RootElement, 1); } catch (IOException) { cycle = true; } Check(cycle, "Circular update chain rejected");
        var app = new TrackedApp { Repo = source.Key, Release = release, Latest = release.Tag, NexusFileId = 22, Installed = "1.0.0" };
        Check(ReleaseFiles.Match(app)?.FileId == 22 && app.AppName == "Test Tool", "Nexus file matching and display name use source metadata");
        ReleaseFiles.Select(app, release.Assets.Single(a => a.FileId == 33));
        Check(app.NexusFileId == 33 && app.Latest == "9.0" && app.Release!.Tag == "9.0" && ReleaseFiles.Match(app)?.FileId == 33 && app.CanUpdate, "Choosing another file updates matching and version atomically");
        var restored = JsonSerializer.Deserialize<TrackedApp>(JsonSerializer.Serialize(app))!;
        Check(restored.NexusFileId == 33 && ReleaseFiles.Match(restored)?.FileId == 33, "File selection survives saving and restart");
        var root = Path.Combine(Path.GetTempPath(), "GROUT-nexus-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var keyPath = Path.Combine(root, "credentials.bin"); var account = new NexusAccount("test-key-not-real", "Test user", true);
            NexusCredentials.Save(account, keyPath);
            Check(NexusCredentials.Load(keyPath) == account && !System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(keyPath)).Contains(account.Key), "API key is DPAPI encrypted and recoverable for current user");
            File.WriteAllText(keyPath, "corrupt"); Check(NexusCredentials.Load(keyPath) == null, "Unreadable credentials fail closed");
            var api = new Handler(request =>
            {
                Check(request.RequestUri!.Host == "api.nexusmods.com" && request.Headers.GetValues("apikey").Single() == account.Key, "Credentials sent only on Nexus API requests");
                Check(request.Headers.GetValues("Application-Name").Single() == "GROUT" && request.Headers.GetValues("Application-Version").Single() == SelfUpdater.CurrentLabel, "Application identifies itself to Nexus");
                var path = request.RequestUri.AbsolutePath;
                if (path.EndsWith("validate.json")) return Json("""{"name":"Test user","is_premium":true}""");
                if (path.EndsWith("download_link.json")) return Json("""[{"URI":"https://cdn.example.org/tool.zip?token=temporary"}]""");
                return Json(path.EndsWith("/files.json") ? Files : Mod);
            });
            var download = new Handler(request => { Check(!request.Headers.Contains("apikey") && !request.Headers.Contains("Application-Name"), "CDN never receives Nexus credentials"); return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }; });
            using var client = new ReleaseClient(download, new NexusMods(api, () => account));
            await client.VerifyRepository(source.Key); var fetched = await client.Latest(source.Key, nexusFileId: 11);
            Check(fetched.SelectedFileId == 22, "Release client routes Nexus verification and update retrieval");
            var asset = fetched.Assets.Single(a => a.FileId == 22);
            var pathName = Path.Combine(root, "tool.zip"); await client.Download(asset, pathName, new Progress<double>(), CancellationToken.None);
            Check(File.ReadAllBytes(pathName).Length == 3 && !JsonSerializer.Serialize(fetched).Contains("temporary"), "Signed CDN URLs are resolved only for download and never persisted");
            using var free = new NexusMods(new Handler(_ => Json("""{"name":"Free","is_premium":false}""")), () => account with { Premium = false });
            var freeBlocked = false; try { await free.DownloadUrl(asset, CancellationToken.None); } catch (InvalidOperationException ex) { freeBlocked = ex.Message.Contains("Premium"); } Check(freeBlocked, "Free accounts use browser downloads rather than bypassing Premium");
            var redirect = new Handler(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example.org/") } });
            using var redirected = new NexusMods(redirect, () => account);
            var refused = false; try { await redirected.Verify(source); } catch (IOException) { refused = true; } Check(refused && redirect.Calls == 1, "API redirects rejected without forwarding the key");
            using var unauthenticated = new NexusMods(new Handler(_ => throw new Exception("Request should not run")), () => null);
            var disconnected = false; try { await unauthenticated.Verify(source); } catch (InvalidOperationException ex) { disconnected = ex.Message.Contains("Settings"); } Check(disconnected, "Disconnected accounts receive actionable guidance without a request");
            foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
            {
                using var failing = new NexusMods(new Handler(_ => new(code)), () => account);
                var caught = false; try { await failing.Verify(source); } catch (InvalidOperationException) { caught = true; } Check(caught, "Account/access/missing failures become app warnings");
            }
            var limitedHandler = new Handler(_ => { var response = new HttpResponseMessage((HttpStatusCode)429); response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(2)); return response; });
            using var limited = new ReleaseClient(nexus: new NexusMods(limitedHandler, () => account));
            for (var i = 0; i < 2; i++) { try { await limited.VerifyRepository(source.Key); } catch (InvalidOperationException) { } }
            Check(limitedHandler.Calls == 1 && limited.Backoffs["nexusmods.com"] > DateTimeOffset.UtcNow.AddHours(1), "Nexus rate limits pause further requests and persist source backoff");
            using var resumed = new ReleaseClient(nexus: new NexusMods(limitedHandler, () => account)); resumed.RestoreBackoffs(limited.Backoffs);
            try { await resumed.VerifyRepository(source.Key); } catch (InvalidOperationException) { }
            Check(limitedHandler.Calls == 1, "Restored backoff avoids an immediate retry after restart");
            using var privateApi = new NexusMods(new Handler(r => Json(r.RequestUri!.AbsolutePath.EndsWith("validate.json") ? """{"name":"Test","is_premium":true}""" : """[{"URI":"https://127.0.0.1/private.zip"}]""")), () => account);
            var privateDownload = new Handler(_ => throw new Exception("Private host must not be contacted"));
            using var guarded = new ReleaseClient(privateDownload, privateApi);
            var privateBlocked = false; try { await guarded.Download(asset, pathName, new Progress<double>(), CancellationToken.None); } catch (InvalidOperationException) { privateBlocked = true; }
            Check(privateBlocked && privateDownload.Calls == 0, "Private download targets rejected before any CDN request");
            var forged = false; var callsBefore = api.Calls;
            try { await client.Nexus.DownloadUrl(asset with { Url = "https://evil.example.org/files/22.json" }, CancellationToken.None); } catch (InvalidOperationException) { forged = true; }
            Check(forged && api.Calls == callsBefore, "Forged metadata endpoint never receives credentials");
            var freeApp = new TrackedApp { Repo = source.Key, Release = fetched, Installed = "1.0.0", Latest = fetched.Tag, NexusFileId = fetched.SelectedFileId, UpdateMethod = "Zip", UpdateTarget = Path.Combine(root, "installed") };
            var freeNexus = new NexusMods(new Handler(_ => Json("""{"name":"Free","is_premium":false}""")), () => account with { Premium = false });
            using var freeClient = new ReleaseClient(new Handler(_ => throw new Exception("Free accounts should not use the CDN")), freeNexus);
            var freeWindow = new SetupWindow(freeClient, [], preview: true, previewStage: "first") { ShowActivated = false };
            // Editor must retain Nexus file selection/version without contacting the account service.
            try
            {
                var page = new AppSettingsPage(freeWindow, freeApp); page.Save();
                Check(freeApp.NexusFileId == 22 && freeApp.Latest == "2.0.0", "App settings retain the Nexus file-specific version");
            }
            finally { freeWindow.Close(); }
            using var wizardClient = new ReleaseClient(nexus: new NexusMods(new Handler(r => Json(r.RequestUri!.AbsolutePath.EndsWith("/files.json") ? Files : Mod)), () => account with { Premium = false }));
            var wizard = new SetupWindow(wizardClient, []);
            try
            {
                Find<System.Windows.Controls.RadioButton>(wizard).Single(b => Equals(b.Content, "Install this app")).IsChecked = true;
                Find<System.Windows.Controls.TextBox>(wizard).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text = link;
                Find<System.Windows.Controls.Button>(wizard).Single(b => Equals(b.Content, "Next →")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                for (var i = 0; i < 100 && !Find<System.Windows.Controls.Button>(wizard).Any(b => Equals(b.Content, "Open Nexus download")); i++) await Task.Delay(20);
                Check(Find<System.Windows.Controls.Button>(wizard).Any(b => Equals(b.Content, "Open Nexus download")), "Free-account install flow opens the browser instead of requesting a direct download");
                Check(!Find<System.Windows.Controls.TextBox>(wizard).Any() && wizard.Result.NexusFileId == 22, "Browser install flow hides irrelevant destination fields and retains primary file");
            }
            finally { wizard.Close(); }
            byte[] zipBytes;
            using (var buffer = new MemoryStream())
            {
                using (var archive = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    using var writer = new StreamWriter(archive.CreateEntry("release-manifest.json").Open()); writer.Write("{\"version\":\"2.0.0\"}");
                }
                zipBytes = buffer.ToArray();
            }
            var pipelineApi = new Handler(r => Json(r.RequestUri!.AbsolutePath.EndsWith("validate.json") ? """{"name":"Test","is_premium":true}""" : """[{"URI":"https://cdn.example.org/package.zip"}]"""));
            using var pipeline = new ReleaseClient(new Handler(r => { Check(!r.Headers.Contains("apikey"), "ZIP install CDN request has no API key"); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) }; }), new NexusMods(pipelineApi, () => account));
            var installed = Path.Combine(root, "portable");
            var installApp = new TrackedApp { Repo = source.Key, Installed = "1.0.0", Latest = "2.0.0", NexusFileId = 22, Release = release, UpdateMethod = "Zip", InstallLocation = installed, UpdateTarget = installed, AutoReconcile = true };
            var owner = new System.Windows.Window();
            var outcome = await UpdateRunner.Run(owner, installApp, release.Assets.Single(a => a.FileId == 22), pipeline, (_, _) => { }, () => { }, newInstall: true,
                prompts: new UpdatePrompts { ReviewFiles = (_, _, _, _) => throw new Exception("No review screen expected") },
                newWork: category => { var dir = Path.Combine(root, category, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); return dir; });
            Check(outcome == UpdateOutcome.FilesApplied && installApp.IsCurrent && File.Exists(Path.Combine(installed, "release-manifest.json")), "Premium Nexus ZIP uses the existing validated install pipeline");
            Check(installApp.DownloadedPath == "" && installApp.LastBackup == "", "Nexus ZIP install cleans completed downloads and transient rollback");
        }
        finally { Directory.Delete(root, true); }
    }
}
