using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace ReleaseDock;

internal static class SelfTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    public static async Task Run()
    {
        FrameworkTests.Run();
        AssetCompatibilityTests.Run();
        AppIdentityTests.Run();
        StorageCleanupTests.Run();
        MissingInstallationTests.Run();
        await ZipFolderTests.Run();
        await SelfUpdateTests.Run();
        await FeatureTests.Run();
        await WizardTests.Run();
        await PortableInstallTests.Run();
        AppSettingsTests.Run();
        await SourceTests.Run();
        await SourceWizardTests.Run();
        await InstallDiscoveryTests.Run();
        Check(Repositories.Parse("https://github.com/owner/repo/releases/tag/v1") == "owner/repo", "Release URL parsing");
        Check(Repositories.Parse("owner/repo.git") == "owner/repo", "Git URL parsing");
        foreach (var bad in new[] { "https://evil.test/owner/repo", "owner", "owner/..", "http://github.com/a/b", "a/b/c" })
        {
            try { Repositories.Parse(bad); throw new Exception("Accepted invalid repository: " + bad); }
            catch (ArgumentException) { }
        }
        var root = Path.Combine(Path.GetTempPath(), "GROUT-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var install = Path.Combine(root, "installed");
            Directory.CreateDirectory(install);
            var manifest = Path.Combine(install, "release-manifest.json");
            File.WriteAllText(manifest, """{"game_version":"99.0","release_tag":"v0.3.30-windows-preview","version":"0.3.30"}""");
            Check(InstalledVersions.Read(install).Version == "v0.3.30-windows-preview", "Exact release tag preferred over unrelated version fields");
            var tracked = new TrackedApp { AutoReconcile = true, InstallLocation = install, Installed = "old", Latest = "v0.3.30-windows-preview" };
            InstalledVersions.Reconcile(tracked);
            Check(tracked.Installed == tracked.Latest && tracked.IsCurrent && !tracked.LocalError, "Installed version reconciliation");
            File.WriteAllText(manifest, """{"release_tag":"v0.3.31-windows-preview"}""");
            InstalledVersions.Reconcile(tracked);
            Check(tracked.Installed == "v0.3.31-windows-preview", "Local updates detected");
            File.WriteAllText(manifest, "broken");
            InstalledVersions.Reconcile(tracked);
            Check(tracked.LocalError && tracked.Installed == "v0.3.31-windows-preview" && tracked.Status == "Installed version unverified", "Corrupt manifest preserves previous version, reports uncertainty");
            tracked.InstallLocation = Path.Combine(root, "missing");
            InstalledVersions.Reconcile(tracked);
            Check(tracked.LocalError && tracked.Installed == "v0.3.31-windows-preview", "Missing location preserves version");
            tracked.AutoReconcile = false; tracked.Installed = "manual";
            InstalledVersions.Reconcile(tracked);
            Check(tracked.Installed == "manual", "Manual mode preserved");
            Check(InstalledVersions.Matches("1.2.3.0", "v1.2.3"), "Numeric Windows version matching");
            Check(!InstalledVersions.Matches("0.3.30", "v0.3.30-windows-preview"), "Preview suffix never guessed");
            File.Delete(manifest);
            File.WriteAllText(Path.Combine(install, "vrmod-manifest.json"), """{"version":"0.3.30"}""");
            Check(InstalledVersions.Read(install).Version == "0.3.30", "Version manifest fallback");
            var ownExe = Framework.ProcessPath!;
            Check(InstalledVersions.Read(ownExe).Version.Length > 0, "EXE version resource detection without execution");
            File.Delete(Path.Combine(install, "vrmod-manifest.json"));
            File.WriteAllText(Path.Combine(install, "unins000.exe"), "");
            File.WriteAllText(Path.Combine(install, "Setup.exe"), "");
            try { InstalledVersions.Read(install); throw new Exception("Installer selected as version source"); } catch (InvalidOperationException) { }
            var store = new Store(Path.Combine(root, "apps.json"));
            store.Save([new() { Repo = "a/b", Installed = "v1", Latest = "v2", AssetName = "app.exe", IncludePrereleases = true, InstallLocation = install, AutoReconcile = true }]);
            var saved = store.Load().Single();
            Check(saved.Status == "Update available" && saved.AssetName == "app.exe" && saved.IncludePrereleases, "Persistence and update status");
            Check(saved.AutoReconcile && saved.InstallLocation == install, "Location and automatic setting persisted");
            saved.Installed = "v2"; Check(saved.Status == "Up to date", "Installed status");
            using var github = new ReleaseClient(new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent("""{"tag_name":"v2","html_url":"https://github.com/a/b/releases/tag/v2","body":"Notes","assets":[{"name":"app.exe","size":3,"browser_download_url":"https://github.com/a/b/releases/download/v2/app.exe","digest":null}]}""") }));
            var release = await github.Latest("a/b");
            Check(release.Tag == "v2" && release.Assets.Single().Size == 3, "Release API parsing");
            var update = new TrackedApp { Installed = "v1", Latest = "v2", Release = release };
            Check(update.CanUpdate, "Update enabled for checked different release");
            update.Installed = "v2"; Check(!update.CanUpdate, "Update disabled when current");
            update.Installed = ""; Check(!update.CanUpdate, "Update disabled for unknown installed version");
            update.Installed = "v1"; update.Error = "Failed"; Check(!update.CanUpdate, "Update disabled after failed release check");
            update.Error = ""; update.AutoReconcile = true; update.LocalError = true;
            Check(!update.CanUpdate, "Update disabled for unverified local version");
            using var previews = new ReleaseClient(new Handler(request =>
            {
                Check(request.RequestUri!.Query == "?per_page=100", "Preview list endpoint");
                return new(HttpStatusCode.OK) { Content = new StringContent("""[{"tag_name":"v1","draft":false,"prerelease":false,"published_at":"2026-01-01T00:00:00Z","html_url":"https://github.com/a/b/releases/tag/v1","assets":[]},{"tag_name":"draft","draft":true,"published_at":"2026-12-01T00:00:00Z","assets":[]},{"tag_name":"v2-preview","draft":false,"prerelease":true,"published_at":"2026-10-01T00:00:00Z","html_url":"https://github.com/a/b/releases/tag/v2-preview","assets":[]}]""") };
            }));
            Check((await previews.Latest("a/b", true)).Tag == "v2-preview", "Newest published preview selected, drafts excluded");
            using var empty = new ReleaseClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("[]") }));
            try { await empty.Latest("a/b", true); throw new Exception("Empty release list accepted"); } catch (InvalidOperationException) { }
            using var missing = new ReleaseClient(new Handler(_ => new(HttpStatusCode.NotFound)));
            try { await missing.Latest("a/b"); throw new Exception("Missing release accepted"); } catch (InvalidOperationException) { }
            var data = Encoding.UTF8.GetBytes("abc");
            using var downloads = new ReleaseClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }));
            var asset = new Asset("app.exe", "https://github.com/a/b/releases/download/v2/app.exe", 3, "sha256:" + Framework.Hex(Framework.Hash(data)));
            var destination = Path.Combine(root, "app.exe");
            await downloads.Download(asset, destination, new Progress<double>(), CancellationToken.None);
            Check(File.ReadAllText(destination) == "abc", "Download and checksum");
            try { await downloads.Download(asset with { Digest = "sha256:bad" }, destination, new Progress<double>(), CancellationToken.None); throw new Exception("Bad digest accepted"); } catch (IOException) { }
            Check(File.ReadAllText(destination) == "abc" && Directory.GetFiles(root, "*.partial").Length == 0, "Failed download preserves previous file and cleans partial");
        }
        finally { Directory.Delete(root, true); }
    }
}
