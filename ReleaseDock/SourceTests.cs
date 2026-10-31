using System.IO;
using System.Net;
using System.Net.Http;

namespace ReleaseDock;

public static class SourceTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool condition, string reason) { Assertions++; if (!condition) throw new Exception(reason); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Requests.Add(request.RequestUri!.ToString()); return Task.FromResult(send(request)); }
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    public static async Task Run()
    {
        Assertions = 0;
        Check(Repositories.Parse("https://github.com/owner/repo/releases/tag/v1") == "owner/repo", "Existing GitHub keys remain compatible");
        Check(Repositories.Parse("https://codeberg.org/owner/app/releases/tag/v1") == "https://codeberg.org/owner/app", "Codeberg release URL normalizes to distinct canonical key");
        Check(Repositories.Parse("codeberg.org/owner/app.git") == "https://codeberg.org/owner/app", "Codeberg link without scheme and git suffix");
        Check(Repositories.Resolve("https://codeberg.org/owner/app").ApiUrl == "https://codeberg.org/api/v1/repos/owner/app", "Codeberg API routing");
        Check(Repositories.Resolve("https://codeberg.org/owner/app").ReleasesUrl == "https://codeberg.org/owner/app/releases", "Codeberg release-page routing");
        Check(Repositories.Parse("https://gitlab.com/group/subgroup/app/-/releases/v1.0") == "https://gitlab.com/group/subgroup/app", "GitLab release URL preserves nested groups");
        Check(Repositories.Resolve("https://gitlab.com/group/subgroup/app.git").ApiUrl == "https://gitlab.com/api/v4/projects/group%2Fsubgroup%2Fapp", "GitLab nested project path is encoded as one API ID");
        Check(Repositories.Resolve("gitlab.com/group/app").ReleasesUrl == "https://gitlab.com/group/app/-/releases", "GitLab releases page uses slash-dash route");
        foreach (var bad in new[] { "http://codeberg.org/a/b", "https://codeberg.org.evil.test/a/b", "https://user:pass@codeberg.org/a/b", "https://codeberg.org:8443/a/b", "https://codeberg.org/a/%2Fbad" })
        {
            try { Repositories.Parse(bad); throw new Exception("Invalid source link accepted: " + bad); } catch (ArgumentException) { Assertions++; }
        }
        var codeberg = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/releases/latest") ? Json("""{"tag_name":"v1.2.3","html_url":"https://codeberg.org/owner/app/releases/tag/v1.2.3","body":"Codeberg notes","assets":[{"name":"App.exe","browser_download_url":"https://codeberg.org/owner/app/releases/download/v1.2.3/App.exe","size":3}]}""") : request.RequestUri.Query.Length > 0 ? Json("""[{"tag_name":"draft","draft":true,"published_at":"2026-10-30T00:00:00Z","assets":[]},{"tag_name":"preview","draft":false,"prerelease":true,"published_at":"2026-10-29T00:00:00Z","assets":[]},{"tag_name":"old","draft":false,"published_at":"2026-10-01T00:00:00Z","assets":[]}]""") : Json("{}"));
        using var client = new ReleaseClient(codeberg);
        await client.VerifyRepository("https://codeberg.org/owner/app");
        var stable = await client.Latest("https://codeberg.org/owner/app");
        Check(stable.Tag == "v1.2.3" && stable.Notes == "Codeberg notes" && stable.Assets.Single().OriginHost == "codeberg.org", "Codeberg stable release/assets parsed");
        Check((await client.Latest("https://codeberg.org/owner/app", true)).Tag == "preview", "Codeberg preview includes newest published non-draft release");
        Check(codeberg.Requests.Last().EndsWith("releases?limit=100"), "Codeberg preview uses correct pagination parameter");
        var gitlabHandler = new Handler(request => Json("""[{"tag_name":"v9.0","released_at":"2999-01-01T00:00:00Z","assets":{"links":[]}},{"tag_name":"v3.0","upcoming_release":true,"released_at":"2026-10-29T00:00:00Z","assets":{"links":[]}},{"tag_name":"v2.0-beta.1","released_at":"2026-10-28T00:00:00Z","description":"Preview notes","assets":{"links":[]}},{"tag_name":"v1.0","released_at":"2026-10-27T00:00:00Z","description":"Stable notes","assets":{"sources":[{"format":"zip","url":"https://gitlab.com/source.zip"}],"links":[{"name":"Windows installer","url":"https://downloads.example.org/App-x64.exe","direct_asset_url":"https://gitlab.com/group/subgroup/app/-/releases/v1.0/downloads/windows"},{"name":"Portable.zip","url":"https://downloads.example.org/portable.zip"},{"name":"Website","url":"https://example.org/downloads"},{"name":"unsafe.exe","url":"http://example.org/unsafe.exe"}]}}]"""));
        using var gitlab = new ReleaseClient(gitlabHandler);
        var gitlabStable = await gitlab.Latest("https://gitlab.com/group/subgroup/app");
        Check(gitlabStable.Tag == "v1.0" && gitlabStable.Notes == "Stable notes", "GitLab stable selection excludes preview, future date and upcoming flag");
        Check(gitlabStable.Assets.Count == 2 && gitlabStable.Assets[0].Name == "App-x64.exe" && gitlabStable.Assets[0].Url.EndsWith("downloads/windows"), "GitLab friendly labels resolve filename while preferring direct asset route");
        Check(gitlabStable.Assets.All(a => a.Size == -1 && a.OriginHost == "gitlab.com") && !ReleaseFiles.SizeText(gitlabStable.Assets[0]).Contains('-'), "Unknown GitLab size is represented honestly");
        Check((await gitlab.Latest("https://gitlab.com/group/subgroup/app", true)).Tag == "v2.0-beta.1", "GitLab preview mode still excludes future and upcoming releases");
        Check(gitlabHandler.Requests.All(r => r.Contains("group%2Fsubgroup%2Fapp/releases?per_page=100")), "GitLab source uses encoded project release-list endpoint");
        Check(ReleaseClient.IsPreviewTag("v1.0-rc.1") && ReleaseClient.IsPreviewTag("v1.0-windows-preview") && !ReleaseClient.IsPreviewTag("v1.0-stable"), "GitLab preview tag heuristic");
        var root = Path.Combine(Path.GetTempPath(), "GROUT-sources-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var store = new Store(Path.Combine(root, "apps.json")); store.Save([new() { Repo = "owner/app" }, new() { Repo = "https://codeberg.org/owner/app", Release = stable }]);
            var entries = store.Load();
            Check(entries[0].Repo != entries[1].Repo && entries[1].Release!.Assets.Single().OriginHost == "codeberg.org", "Provider identity and asset provenance survive library persistence");
            using var download = new ReleaseClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent("abc"u8.ToArray()) }));
            await download.Download(stable.Assets.Single(), Path.Combine(root, "app.exe"), new Progress<double>(), CancellationToken.None);
            Check(File.ReadAllText(Path.Combine(root, "app.exe")) == "abc", "Codeberg release asset downloads through existing pipeline");
            await download.Download(gitlabStable.Assets[0], Path.Combine(root, "gitlab.exe"), new Progress<double>(), CancellationToken.None);
            Check(File.ReadAllText(Path.Combine(root, "gitlab.exe")) == "abc", "GitLab public linked asset downloads with initially unknown size");
            using var html = new ReleaseClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("<html>Sign in</html>", System.Text.Encoding.UTF8, "text/html") }));
            try { await html.Download(gitlabStable.Assets[0], Path.Combine(root, "login.exe"), new Progress<double>(), CancellationToken.None); throw new Exception("Login page accepted as app"); } catch (IOException) { Assertions++; }
            Check(!File.Exists(Path.Combine(root, "login.exe")), "HTML login response is not saved as an executable");
            var redirected = new Handler(r => r.RequestUri!.Host == "codeberg.org" ? new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://cdn.example.org/App.exe") } } : new(HttpStatusCode.OK) { Content = new ByteArrayContent("abc"u8.ToArray()) });
            using var redirects = new ReleaseClient(redirected);
            await redirects.Download(stable.Assets[0], Path.Combine(root, "redirect.exe"), new Progress<double>(), CancellationToken.None);
            Check(redirected.Requests.Count == 2 && File.ReadAllText(Path.Combine(root, "redirect.exe")) == "abc", "HTTPS release CDN redirect followed without source change");
            using var unsafeRedirect = new ReleaseClient(new Handler(_ => new(HttpStatusCode.Found) { Headers = { Location = new Uri("http://example.org/app.exe") } }));
            try { await unsafeRedirect.Download(stable.Assets[0], Path.Combine(root, "unsafe.exe"), new Progress<double>(), CancellationToken.None); throw new Exception("HTTPS downgrade redirect accepted"); } catch (InvalidOperationException) { Assertions++; }
            try { await download.Download(new("app.exe", "https://127.0.0.1/app.exe", -1, null, "gitlab.com"), Path.Combine(root, "local.exe"), new Progress<double>(), CancellationToken.None); throw new Exception("Loopback release asset accepted"); } catch (InvalidOperationException) { Assertions++; }
        }
        finally { Directory.Delete(root, true); }
        var rate = new Handler(r =>
        {
            if (r.RequestUri!.Host != "codeberg.org") return Json("{}");
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new(TimeSpan.FromMinutes(5)); return response;
        });
        using var isolated = new ReleaseClient(rate);
        try { await isolated.VerifyRepository("https://codeberg.org/owner/app"); } catch (InvalidOperationException) { }
        await isolated.VerifyRepository("owner/app");
        await isolated.VerifyRepository("https://gitlab.com/group/app");
        Check(rate.Requests.Count == 3 && isolated.Backoffs.ContainsKey("codeberg.org") && !isolated.Backoffs.ContainsKey("github.com"), "Codeberg rate limit does not block GitHub or GitLab");
    }
}
