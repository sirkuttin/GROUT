using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using ReleaseDock.Json;

namespace ReleaseDock;

internal static class D2RLoaderTests
{
    private static IEnumerable<T> Find<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent).OfType<System.Windows.DependencyObject>()) { if (child is T found) yield return found; foreach (var next in Find<T>(child)) yield return next; }
    }
    public static int Assertions { get; private set; }
    private static void Check(bool ok, string message) { Assertions++; if (!ok) throw new Exception(message); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(respond(request)); }
    }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, System.Text.Encoding.UTF8, "application/json") };
    internal static void Preview(string output)
    {
        const string sample = """{"page":1,"pageSize":18,"total":24,"items":[{"name":"Example plugin","slug":"example-plugin","type":"plugin","summary":"Adds extra options to the loader. Sample data.","status":"published","visibility":"public"},{"name":"Example patch","slug":"example-patch","type":"patch","summary":"An example of a community patch. Sample data.","status":"published","visibility":"public"}]}""";
        using var client = new ReleaseClient(new Handler(_ => Json(sample)));
        var owner = Ui.Window("Preview owner"); owner.ShowActivated = false; owner.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual; owner.Left = -20000; owner.Top = -20000; owner.Show();
        try { D2RLoaderBrowser.Show(owner, client, window => { Find<System.Windows.Controls.ListBox>(window).Single().SelectedIndex = 0; App.SavePreview(window, output); window.DialogResult = false; }); }
        finally { owner.Close(); }
    }
    private static string Mod(long size, string hash, string filename = "Mod-2.0.zip") => """
        {"name":"Example Mod","slug":"example","type":"plugin","status":"published","visibility":"public","loaderVersion":{"version":"1.3.1"},"releases":[
        {"id":"11111111-1111-4111-8111-111111111111","version":"1.0","status":"published","fileName":"old.zip","sizeBytes":1,"sha256":"HASH","publishedAt":"2026-01-01T00:00:00Z","updatedAt":"2999-01-01T00:00:00Z"},
        {"id":"22222222-2222-4222-8222-222222222222","version":"2.0","status":"published","fileName":"FILENAME","sizeBytes":SIZE,"sha256":"HASH","publishedAt":"2026-10-01T00:00:00Z","changelog":"Fixes","scanSummary":{"targets":[{"status":"flagged"}]},"reviewReason":"Moderator approved after manual review"},
        {"id":"33333333-3333-4333-8333-333333333333","version":"3.0-beta.1","status":"published","fileName":"preview.zip","sizeBytes":1,"sha256":"HASH","publishedAt":"2026-10-02T00:00:00Z"},
        {"id":"44444444-4444-4444-8444-444444444444","version":"4.0","status":"draft","publishedAt":"2026-10-03T00:00:00Z"},
        {"id":"55555555-5555-4555-8555-555555555555","version":"5.0","status":"published","publishedAt":"2999-01-01T00:00:00Z"}]}
        """.Replace("HASH", hash).Replace("FILENAME", filename).Replace("SIZE", size.ToString(System.Globalization.CultureInfo.InvariantCulture));
    internal static async Task Run()
    {
        Assertions = 0;
        var page = "https://d2rloader.net/hub/plugins/example"; var source = Repositories.Resolve(page);
        Check(source.ApiUrl == "https://d2rloader.net/api/v1/hub/plugins/example" && source.WebUrl == page && source.ReleasesUrl == page && source.Name == "D2RLoader", "D2RLoader web and API identities resolve correctly");
        foreach (var input in new[] { source.ApiUrl, "d2rloader.net/hub/plugins/example", "https://www.d2rloader.net/hub/plugins/EXAMPLE?tab=files" }) Check(Repositories.Parse(input) == page, "D2RLoader page/API aliases deduplicate");
        foreach (var bad in new[] { "https://d2rloader.net/api/v1/hub/plugins?page=1", "https://d2rloader.net/hub/plugins/example/other", "https://d2rloader.net/api/v1/hub/releases/1111/download", "https://d2rloader.net.evil.org/hub/plugins/example", "https://user@d2rloader.net/hub/plugins/example", "http://d2rloader.net/hub/plugins/example" })
        { var rejected = false; try { Repositories.Parse(bad); } catch (ArgumentException) { rejected = true; } Check(rejected, "Invalid provider URLs are rejected"); }
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("Wrapper/plugin.dll").Open())) writer.Write("test payload");
                using (var writer = new StreamWriter(zip.CreateEntry("Wrapper/manifest.json").Open())) writer.Write("{\"name\":\"Example-v2.0\"}");
            }
            bytes = buffer.ToArray();
        }
        var hash = Framework.Hex(Framework.Hash(bytes)).ToLowerInvariant(); var payload = Mod(bytes.Length, hash);
        using var data = JsonDocument.Parse(payload); var release = D2RLoader.Parse(source, data.RootElement, false);
        Check(release.Tag == "2.0" && D2RLoader.Parse(source, data.RootElement, true).Tag == "3.0-beta.1", "Newest published stable/preview selected by publication date, not edit date");
        Check(release.Assets.Single().Url == "https://d2rloader.net/api/v1/hub/releases/22222222-2222-4222-8222-222222222222/download" && release.Assets.Single().Digest == "sha256:" + hash && release.Assets.Single().Size == bytes.Length, "Download endpoint, SHA-256 and exact byte size come from selected release");
        Check(release.SourceWarning.Contains("scan detections") && release.SourceWarning.Contains("Moderator approved") && release.Notes.Contains("1.3.1"), "Loader requirement and flagged-but-approved scan information are preserved");
        var app = new TrackedApp { Repo = page, Release = release, Installed = "1.0", Latest = "2.0", AssetName = "Mod-1.0.zip" };
        Check(app.AppName == "Example Mod" && ReleaseFiles.Match(app)?.Name == "Mod-2.0.zip", "Provider title and normal versioned-file matching work");
        Check(app.StatusColor == "#F0D495", "Existing scan warning gets an amber app status");
        var restored = JsonSerializer.Deserialize<TrackedApp>(JsonSerializer.Serialize(app))!;
        Check(restored.Release!.SourceWarning == release.SourceWarning && restored.RepositoryUrl == page, "Provider and warning survive persistence");
        foreach (var bad in new[] { payload.Replace("\"visibility\":\"public\"", "\"visibility\":\"private\""), payload.Replace(hash, "bad"), payload.Replace("Mod-2.0.zip", "../escape.zip"), payload.Replace("22222222-2222-4222-8222-222222222222", "invalid") })
        { var rejected = false; try { using var json = JsonDocument.Parse(bad); D2RLoader.Parse(source, json.RootElement, false); } catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException) { rejected = true; } Check(rejected, "Unpublished/private/unsafe/incomplete release metadata rejected"); }
        using var rawData = JsonDocument.Parse(Mod(bytes.Length, hash, "plugin.dll")); var raw = D2RLoader.Parse(source, rawData.RootElement, false);
        Check(AssetCompatibility.Candidates(raw.Assets).Count == 1 && AssetCompatibility.Candidates([raw.Assets.Single() with { OriginHost = "github.com" }]).Count == 0, "Raw plugin DLL is offered only for D2RLoader manual handling");
        const string catalog = """{"page":2,"pageSize":18,"total":108,"items":[{"name":"Visible","slug":"visible","type":"plugin","summary":"Info","status":"published","visibility":"public"},{"name":"Hidden","slug":"hidden","status":"published","visibility":"private"}]}""";
        using var catalogData = JsonDocument.Parse(catalog); var parsedPage = D2RLoader.ParsePage(catalogData.RootElement);
        Check(parsedPage.Page == 2 && parsedPage.PageCount == 6 && parsedPage.Items.Single().Slug == "visible", "Catalog pagination and public-only filter respect API envelope");
        var root = Path.Combine(Path.GetTempPath(), "GROUT-d2rloader-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var installedFolder = Path.Combine(root, "version-test"); Directory.CreateDirectory(installedFolder);
            var manifest = Path.Combine(installedFolder, "manifest.json");
            File.WriteAllText(manifest, """{"name":"RuffnecKk-remote-stash-v2.3.4","version":"999","nested":{"name":"Wrong-v9.0"}}""");
            Check(InstalledVersions.Read(installedFolder, true).Version == "2.3.4" && InstalledVersions.Read(manifest, true).Source == manifest, "D2RLoader reads top-level manifest name, prioritizing it over schema version and nested names");
            File.WriteAllText(manifest, """{"name":"d2rl-auto-deposit-0.1.4","version":2,"files":[]}""");
            Check(InstalledVersions.Read(installedFolder, true).Version == "0.1.4", "Actual auto-deposit name without a v prefix ignores numeric manifest format version");
            var setupApp = new TrackedApp { Repo = page, InstallLocation = installedFolder, AutoReconcile = true };
            var setupOwner = new System.Windows.Window();
            try
            {
                var editor = new AppSettingsPage(setupOwner, setupApp);
                Check(setupApp.Installed == "0.1.4" && Find<System.Windows.Controls.TextBlock>(editor.Panel).Any(t => t.Text.Contains("0.1.4 · Automatic detection")), "Existing-plugin settings automatically detect the no-v manifest version");
            }
            finally { setupOwner.Close(); }
            File.WriteAllText(manifest, """{"name":"RuffnecKk-remote-stash-v2.3.4","version":"999"}""");
            File.WriteAllText(Path.Combine(installedFolder, "release-manifest.json"), """{"version":"7.0"}""");
            Check(InstalledVersions.Read(installedFolder).Version == "7.0" && InstalledVersions.Read(installedFolder, true).Version == "2.3.4", "D2RLoader-specific discovery leaves other providers unchanged");
            var versionApp = new TrackedApp { Repo = page, InstallLocation = installedFolder, Installed = "old", Latest = "2.3.4", AutoReconcile = true };
            InstalledVersions.Reconcile(versionApp);
            Check(versionApp.IsCurrent && !versionApp.LocalError && versionApp.LocalStatus.Contains("manifest.json"), "Automatic reconciliation finds installed D2RLoader mod version");
            Check(FileUpdates.StableD2RFolder("d2rl-auto-deposit-0.1.4") == "d2rl-auto-deposit" && FileUpdates.StableD2RFolder("RuffnecKk-remote-stash-v2.3.4") == "RuffnecKk-remote-stash", "Shown D2RLoader folder names lose their numeric version suffix");
            var stage = Path.Combine(root, "stable-stage"); var versioned = Path.Combine(stage, "RuffnecKk-remote-stash-v2.3.5"); Directory.CreateDirectory(versioned);
            File.WriteAllText(Path.Combine(versioned, "manifest.json"), """{"name":"RuffnecKk-remote-stash-v2.3.5"}""");
            File.WriteAllText(Path.Combine(versioned, "plugin.dll"), "new version");
            var stableTarget = Path.Combine(root, "stable-target");
            var stablePlan = FileUpdates.PlanZip(stage, stableTarget, "", false, true);
            Check(stablePlan.All(f => f.Relative.StartsWith("RuffnecKk-remote-stash\\", StringComparison.Ordinal)) && stablePlan.All(f => !f.Relative.Contains("v2.3.5")), "D2RLoader mod folder mapping uses stable destination names");
            foreach (var file in stablePlan) file.Apply = true;
            FileUpdates.Apply(stablePlan, stableTarget, Path.Combine(root, "stable-backup"));
            var stableManifest = Path.Combine(stableTarget, "RuffnecKk-remote-stash", "manifest.json");
            Check(InstalledVersions.Read(stableManifest, true).Version == "2.3.5" && File.ReadAllText(stableManifest).Contains("v2.3.5"), "Stable installed folder keeps original manifest/version unchanged");
            Check(FileUpdates.ResolveZipSelection(stage, "RuffnecKk-remote-stash-v2.3.4", false, true) == "RuffnecKk-remote-stash-v2.3.5", "Saved folder selection follows a unique new version suffix");
            Check(FileUpdates.ResolveZipSelection(stage, "RuffnecKk-remote-stash-v2.3.4/plugin.dll", true, true).EndsWith("v2.3.5/plugin.dll"), "Saved single-file selection follows its renamed parent folder");
            Check(FileUpdates.PlanZip(stage, stableTarget, "", false).Any(f => f.Relative.Contains("v2.3.5")), "Other sources retain their archive folder names");
            var another = Path.Combine(stage, "RuffnecKk-remote-stash-v2.3.6"); Directory.CreateDirectory(another); File.WriteAllText(Path.Combine(another, "manifest.json"), "{}");
            var ambiguous = false; try { FileUpdates.ResolveZipSelection(stage, "RuffnecKk-remote-stash-v2.3.4", false, true); } catch (IOException) { ambiguous = true; } Check(ambiguous, "Multiple version-folder matches stop instead of guessing");
            var collision = false; try { FileUpdates.PlanZip(stage, stableTarget, "", false, true); } catch (IOException) { collision = true; } Check(collision, "Stable folder-name collisions are rejected before writes");
            File.WriteAllText(manifest, """{"name":"RuffnecKk-remote-stash-v2.3.5-rc.1+build.4"}""");
            Check(InstalledVersions.Read(installedFolder, true).Version == "2.3.5-rc.1+build.4", "Manifest prerelease and build suffixes are preserved");
            foreach (var invalid in new[] { "{}", "{\"name\":12}", "{\"name\":\"tool-without-version\"}", "{\"name\":\"tool-v2.3.4 extra\"}", "{\"nested\":{\"name\":\"tool-v2.3.4\"}}" })
            {
                File.WriteAllText(manifest, invalid); versionApp.Installed = "2.3.4"; InstalledVersions.Reconcile(versionApp);
                Check(versionApp.LocalError && versionApp.Installed == "2.3.4", "Missing/ambiguous manifest versions preserve last known version and warn");
            }
            var handler = new Handler(request =>
            {
                Check(request.RequestUri!.Host == "d2rloader.net" && !request.Headers.Contains("apikey"), "D2RLoader requests use the provider host without credentials");
                return request.RequestUri.AbsolutePath.EndsWith("/download") ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : Json(request.RequestUri.Query.Contains("page=2") ? catalog : payload);
            });
            using var client = new ReleaseClient(handler);
            await client.VerifyRepository(page); var fetched = await client.Latest(source.ApiUrl); var second = await client.D2RLoaderCatalog(2);
            Check(fetched.Tag == "2.0" && second.Page == 2, "Client routes detail links and paged catalog through provider API");
            var target = Path.Combine(root, "installed"); app.UpdateMethod = "Zip"; app.UpdateTarget = app.InstallLocation = target; app.ZipSubfolder = "Wrapper"; app.AutoReconcile = true;
            string Allocate(string category) { var folder = Path.Combine(root, category, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
            var owner = new System.Windows.Window();
            try
            {
                var result = await UpdateRunner.Run(owner, app, fetched.Assets.Single(), client, (_, _) => { }, () => { }, newInstall: true, newWork: Allocate);
                Check(result == UpdateOutcome.FilesApplied && File.Exists(Path.Combine(target, "plugin.dll")) && !Directory.Exists(Path.Combine(target, "Wrapper")), "D2RLoader ZIP installs selected folder contents through shared validated pipeline");
                Check(app.IsCurrent && app.Installed == "2.0" && !app.LocalError, "ZIP install automatically reconciles version from installed manifest name");
                Check(app.DownloadedPath == "" && app.LastBackup == "", "Successful provider install cleans download and temporary rollback");
            }
            finally { owner.Close(); }
            var shared = Path.Combine(root, "shared-plugins"); Directory.CreateDirectory(Path.Combine(shared, "OtherPlugin")); File.WriteAllText(Path.Combine(shared, "OtherPlugin", "keep.txt"), "keep");
            var nestedApp = new TrackedApp { Repo = page, Installed = "1.0", Latest = "2.0", UpdateMethod = "Zip", UpdateTarget = shared, InstallLocation = shared, AutoReconcile = true };
            var nestedOwner = new System.Windows.Window();
            try
            {
                await UpdateRunner.Run(nestedOwner, nestedApp, fetched.Assets.Single(), client, (_, _) => { }, () => { }, newInstall: true, newWork: Allocate);
                Check(nestedApp.IsCurrent && nestedApp.InstallLocation == Path.Combine(shared, "Wrapper", "manifest.json") && nestedApp.D2RManagedFolder == "Wrapper", "Nested D2RLoader install remembers the exact installed manifest and owned mod folder");
                Check(AppUninstall.ZipFolder(nestedApp) == Path.Combine(shared, "Wrapper") && File.Exists(Path.Combine(shared, "OtherPlugin", "keep.txt")), "D2RLoader uninstall targets its installed mod folder rather than shared plugins parent");
                var persisted = JsonSerializer.Deserialize<TrackedApp>(JsonSerializer.Serialize(nestedApp))!;
                Check(AppUninstall.ZipFolder(persisted) == Path.Combine(shared, "Wrapper"), "Owned D2RLoader folder survives restart");
            }
            finally { nestedOwner.Close(); }
            var downloadPath = Path.Combine(root, "download.zip");
            var checksumFailed = false; try { await client.Download(fetched.Assets.Single() with { Digest = "sha256:" + new string('0', 64) }, downloadPath, new Progress<double>(), CancellationToken.None); } catch (IOException) { checksumFailed = true; }
            Check(checksumFailed && !File.Exists(downloadPath), "Wrong provider checksum never becomes an installed/downloaded file");
            var restricted = new Handler(_ => new((HttpStatusCode)429)); using var limited = new ReleaseClient(restricted);
            for (var i = 0; i < 2; i++) { try { await limited.Latest(page); } catch (InvalidOperationException) { } }
            Check(restricted.Calls == 1 && limited.Backoffs.ContainsKey("d2rloader.net"), "Rate limits pause D2RLoader independently");
            using var resumed = new ReleaseClient(restricted); resumed.RestoreBackoffs(limited.Backoffs);
            try { await resumed.Latest(page); } catch (InvalidOperationException) { }
            Check(restricted.Calls == 1, "D2RLoader pause survives restart");
            var requested = new List<(int Page, string Query)>();
            using var browseClient = new ReleaseClient(new Handler(r =>
            {
                var number = r.RequestUri!.Query.Contains("page=2") ? 2 : 1;
                var encoded = System.Text.RegularExpressions.Regex.Match(r.RequestUri.Query, "[?&]q=([^&]*)");
                var query = encoded.Success ? Uri.UnescapeDataString(encoded.Groups[1].Value) : ""; requested.Add((number, query));
                return Json(catalog.Replace("\"page\":2", "\"page\":" + number).Replace("visible", (query.Length > 0 ? "search" : "visible") + number));
            }));
            var browserOwner = Ui.Window("Isolated D2RLoader browser test"); browserOwner.ShowActivated = false; browserOwner.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual; browserOwner.Left = -20000; browserOwner.Top = -20000; browserOwner.Show();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) }; var ticks = 0; var step = 0;
            timer.Tick += (_, _) =>
            {
                var browser = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.Title == "Browse D2RLoader"); if (browser == null) return;
                if (++ticks > 200) { timer.Stop(); browser.Close(); return; }
                var list = Find<System.Windows.Controls.ListBox>(browser).FirstOrDefault(); if (list == null || list.Items.Count == 0) return;
                var next = Find<System.Windows.Controls.Button>(browser).Single(b => Equals(b.Content, "Next →"));
                var status = Find<System.Windows.Controls.TextBlock>(browser).FirstOrDefault(t => t.Text.StartsWith("Page "))?.Text ?? "";
                var searchButton = Find<System.Windows.Controls.Button>(browser).Single(b => Equals(b.Content, "Search"));
                if (!searchButton.IsEnabled) return;
                if (step == 0 && status.StartsWith("Page 1 ")) { step++; next.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); return; }
                if (step == 1 && status.StartsWith("Page 2 ")) { step++; Find<System.Windows.Controls.TextBox>(browser).Single().Text = "stash & merc"; searchButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); return; }
                if (step == 2 && status.StartsWith("Page 1 ") && status.Contains("Search:")) { step++; next.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); return; }
                if (step == 3 && status.StartsWith("Page 2 ") && status.Contains("Search:")) { step++; Find<System.Windows.Controls.TextBox>(browser).Single().Text = ""; searchButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); return; }
                if (step == 4 && status.StartsWith("Page 1 ") && !status.Contains("Search:")) { timer.Stop(); list.SelectedIndex = 0; Find<System.Windows.Controls.Button>(browser).Single(b => Equals(b.Content, "Use this mod")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }
            };
            timer.Start();
            try
            {
                var chosen = D2RLoaderBrowser.Show(browserOwner, browseClient);
                Check(chosen == "https://d2rloader.net/hub/plugins/visible1", "Visual catalog returns the selected mod");
                Check(requested.SequenceEqual(new[] { (1, ""), (2, ""), (1, "stash & merc"), (2, "stash & merc"), (1, "") }), "Search is safely encoded, resets page, persists across paging and clears to page one");
            }
            finally { timer.Stop(); browserOwner.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }
}
