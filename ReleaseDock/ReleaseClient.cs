using System.IO;
using System.Net;
using System.Net.Http;
using ReleaseDock.Json;
using System.Text.RegularExpressions;

namespace ReleaseDock;

public sealed class ReleaseClient : IDisposable
{
    private readonly HttpClient client;
    private readonly Dictionary<string, DateTimeOffset> backoffs = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset? RetryAfter => backoffs.Values.Where(t => t > DateTimeOffset.UtcNow).Select(t => (DateTimeOffset?)t).Min();
    public Dictionary<string, DateTimeOffset> Backoffs => backoffs.Where(p => p.Value > DateTimeOffset.UtcNow).ToDictionary(p => p.Key, p => p.Value);
    public void RestoreBackoffs(Dictionary<string, DateTimeOffset> saved) { foreach (var pair in saved.Where(p => p.Key is "github.com" or "codeberg.org" or "gitlab.com")) backoffs[pair.Key] = pair.Value; }
    public ReleaseClient(HttpMessageHandler? handler = null)
    {
        client = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GROUT/0.4");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }
    private async Task<HttpResponseMessage> Get(RepositoryRef repository, string url, CancellationToken cancellation)
    {
        if (backoffs.TryGetValue(repository.Host, out var pause) && pause > DateTimeOffset.UtcNow) throw new InvalidOperationException($"{repository.Name} requests paused until {pause.ToLocalTime():g}.");
        var response = await Follow(new Uri(url), HttpCompletionOption.ResponseContentRead, cancellation, new Uri(repository.ApiUrl).Host);
        if (response.StatusCode == HttpStatusCode.Unauthorized) { response.Dispose(); throw new InvalidOperationException($"{repository.Name} requires authentication for this resource. This version supports public projects only."); }
        if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
        {
            var next = DateTimeOffset.UtcNow.AddHours(1);
            foreach (var header in new[] { "X-RateLimit-Reset", "RateLimit-Reset" })
                if (response.Headers.TryGetValues(header, out var values) && long.TryParse(values.FirstOrDefault(), out var seconds) && seconds > 0 && seconds < 253402300799) next = DateTimeOffset.FromUnixTimeSeconds(seconds).AddSeconds(10);
            if (response.Headers.RetryAfter?.Delta is { } delay) next = DateTimeOffset.UtcNow.Add(delay);
            if (response.Headers.RetryAfter?.Date is { } date) next = date;
            backoffs[repository.Host] = next < DateTimeOffset.UtcNow.AddMinutes(1) ? DateTimeOffset.UtcNow.AddMinutes(1) : next;
            response.Dispose();
            throw new InvalidOperationException($"{repository.Name} limit or access restriction reached. Its checks pause until {backoffs[repository.Host].ToLocalTime():g}.");
        }
        if (string.Equals(response.Content?.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase)) { response.Dispose(); throw new IOException($"{repository.Name} returned a web/login page instead of project metadata. Check public access."); }
        return response;
    }
    public async Task VerifyRepository(string repo)
    {
        var reference = Repositories.Resolve(repo);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await Get(reference, reference.ApiUrl, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException($"{reference.Name} project not found. Check the link and public access.");
        response.EnsureSuccessStatusCode();
    }
    public async Task<Release> Latest(string repo, bool includePrereleases = false)
    {
        var reference = Repositories.Resolve(repo);
        if (reference.Host == "gitlab.com") return await LatestGitLab(reference, includePrereleases);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var endpoint = includePrereleases ? reference.Host == "codeberg.org" ? "/releases?limit=100" : "/releases?per_page=100" : "/releases/latest";
        using var response = await Get(reference, reference.ApiUrl + endpoint, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException(includePrereleases ? "Project unavailable. Check the URL and public access." : "No stable release found. Try including preview releases, or check the project link.");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        var root = json.RootElement;
        if (includePrereleases)
        {
            root = root.EnumerateArray().Where(r => !Flag(r, "draft")).OrderByDescending(r => Date(r, "published_at")).FirstOrDefault();
            if (root.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("No public releases found.");
        }
        var assets = root.GetProperty("assets").EnumerateArray().Select(a => new Asset(Text(a, "name"), Text(a, "browser_download_url"), a.TryGetProperty("size", out var s) ? s.GetInt64() : -1, a.TryGetProperty("digest", out var d) ? d.GetString() : null, reference.Host)).ToList();
        return new(Text(root, "tag_name"), Text(root, "html_url"), Text(root, "body"), assets);
    }
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static bool Flag(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static DateTimeOffset Date(JsonElement root, string name) => DateTimeOffset.TryParse(Text(root, name), out var date) ? date : DateTimeOffset.MinValue;
    private async Task<Release> LatestGitLab(RepositoryRef reference, bool includePrereleases)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await Get(reference, reference.ApiUrl + "/releases?per_page=100&order_by=released_at&sort=desc", timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("GitLab project/releases unavailable. Check the link and public access.");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        var release = json.RootElement.EnumerateArray()
            .Where(r => !Flag(r, "upcoming_release") && Date(r, "released_at") <= DateTimeOffset.UtcNow && (includePrereleases || !IsPreviewTag(Text(r, "tag_name"))))
            .OrderByDescending(r => Date(r, "released_at")).FirstOrDefault();
        if (release.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException(includePrereleases ? "No published GitLab releases found." : "No stable-tag release found among the latest 100 GitLab releases. Try Include preview releases.");
        var assets = new List<Asset>();
        if (release.TryGetProperty("assets", out var collection) && collection.TryGetProperty("links", out var links))
        {
            foreach (var link in links.EnumerateArray())
            {
                var raw = Text(link, "direct_asset_url"); if (raw.Length == 0) raw = Text(link, "url");
                if (!Uri.TryCreate(new Uri(reference.WebUrl + "/"), raw, out var url) || url.Scheme != "https" || url.UserInfo.Length > 0) continue;
                var name = Text(link, "name");
                if (!Repositories.IsWindowsAsset(name)) name = Uri.UnescapeDataString(Path.GetFileName(url.AbsolutePath));
                if (!Repositories.IsWindowsAsset(name) && Uri.TryCreate(Text(link, "url"), UriKind.Absolute, out var original)) name = Uri.UnescapeDataString(Path.GetFileName(original.AbsolutePath));
                if (!Repositories.IsWindowsAsset(name)) continue; // Do not treat generated source archives or web pages as app installers.
                assets.Add(new(Path.GetFileName(name), url.AbsoluteUri, -1, null, reference.Host));
            }
        }
        var web = release.TryGetProperty("_links", out var releaseLinks) ? Text(releaseLinks, "self") : "";
        if (!Uri.TryCreate(web, UriKind.Absolute, out var webUri) || webUri.Scheme != "https" || webUri.Host != "gitlab.com") web = reference.ReleasesUrl + "/" + Uri.EscapeDataString(Text(release, "tag_name"));
        return new(Text(release, "tag_name"), web, Text(release, "description"), assets.GroupBy(a => a.Url).Select(g => g.First()).ToList());
    }
    public static bool IsPreviewTag(string tag) => Regex.IsMatch(tag, @"(?:alpha|beta|preview|nightly|snapshot|rc|dev|pre)(?:[-_.]?\d+)?(?:$|[-_.])", RegexOptions.IgnoreCase);
    private static void ValidateUrl(Uri uri, string? metadataHost = null)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Release links must use public HTTPS URLs without embedded credentials.");
        if (metadataHost != null && !uri.Host.Equals(metadataHost, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The project API redirected outside its source host.");
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            var b = address.GetAddressBytes();
            if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || b.Length == 4 && (b[0] is 0 or 10 or 127 || b[0] == 169 && b[1] == 254 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31) || b.Length == 16 && (b[0] & 0xFE) == 0xFC) throw new InvalidOperationException("Release assets cannot use local/private IP addresses.");
        }
    }
    private async Task<HttpResponseMessage> Follow(Uri uri, HttpCompletionOption completion, CancellationToken cancellation, string? metadataHost = null)
    {
        for (var redirect = 0; redirect <= 8; redirect++)
        {
            ValidateUrl(uri, metadataHost);
            var response = await client.GetAsync(uri, completion, cancellation);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location == null) throw new IOException("Release redirect had no destination.");
            uri = new Uri(uri, location);
        }
        throw new IOException("Too many release redirects.");
    }

    public async Task Download(Asset asset, string destination, IProgress<double> progress, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || !uri.IsDefaultPort || (asset.OriginHost ?? "github.com") is not ("github.com" or "codeberg.org" or "gitlab.com") || (asset.OriginHost == null && uri.Host != "github.com")) throw new InvalidOperationException("Unexpected release download URL.");
        var partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var response = await Follow(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            if (string.Equals(response.Content?.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase) || string.Equals(response.Content?.Headers.ContentType?.MediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase)) throw new IOException("This asset returned a web/login page instead of an app file. Check the release link and public access.");
            if (response.Content == null) throw new IOException("The release download returned no content.");
            var expectedSize = asset.Size >= 0 ? asset.Size : response.Content.Headers.ContentLength;
            using (var source = await response.Content.ReadAsStreamAsync(cancellation))
            using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    if (expectedSize.HasValue && read > expectedSize.Value - total) throw new IOException("Download exceeded the release or response metadata size.");
                    await target.WriteAsync(buffer, 0, read, cancellation); total += read;
                    progress.Report(expectedSize > 0 ? Math.Min(100, total * 100d / expectedSize.Value) : 0);
                }
                if (expectedSize.HasValue && total != expectedSize.Value) throw new IOException("Download size did not match the release or response metadata.");
            }
            if (asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
            {
                using var file = File.OpenRead(partial);
                var hash = Framework.Hex(await Framework.HashAsync(file, cancellation));
                if (!hash.Equals(asset.Digest[7..], StringComparison.OrdinalIgnoreCase)) throw new IOException("Download checksum did not match.");
            }
            Framework.Move(partial, destination, true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    public void Dispose() => client.Dispose();
}
