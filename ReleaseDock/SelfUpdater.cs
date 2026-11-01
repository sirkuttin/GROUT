using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReleaseDock;

public sealed record SelfRelease(Version Version, string Tag, string Page, long ExeId, long ChecksumId, string ExeName);

public sealed class SelfUpdater : IDisposable
{
    public const string Repository = "sirkuttin/GROUT";
    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    public static string CurrentLabel => $"{Current.Major}.{Current.Minor}.{Current.Build}";
    private readonly HttpClient client;
    public SelfUpdater(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GROUT/" + CurrentLabel);
    }
    private async Task<HttpResponseMessage> Get(string url, bool binary, CancellationToken cancel)
    {
        var uri = new Uri(url);
        for (int redirects = 0; redirects < 6; redirects++)
        {
            if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 || !(uri.Host == "api.github.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))) throw new IOException("Unexpected update download host.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd(binary ? "application/octet-stream" : "application/vnd.github+json");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location; response.Dispose();
                uri = location != null ? new Uri(uri, location) : throw new IOException("Update redirect has no destination."); continue;
            }
            if (!response.IsSuccessStatusCode) { var code = (int)response.StatusCode; response.Dispose(); throw new IOException($"GitHub update request failed ({code}). Try again later."); }
            return response;
        }
        throw new IOException("Too many update redirects.");
    }
    public static SelfRelease? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json); var r = doc.RootElement;
        if (r.GetProperty("draft").GetBoolean() || r.GetProperty("prerelease").GetBoolean()) return null;
        var tag = r.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$") || !Version.TryParse(tag.TrimStart('v'), out var version)) throw new IOException("Release has an unsupported version tag.");
        var comparable = new Version(version.Major, version.Minor, version.Build, 0);
        if (comparable <= current) return null;
        var name = $"GROUT-{version}-win-x64.exe"; var sum = $"GROUT-{version}-SHA256SUMS.txt";
        long Asset(string filename) => r.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == filename && a.GetProperty("state").GetString() == "uploaded").Select(a => a.GetProperty("id").GetInt64()).Single();
        var exe = Asset(name); var checksums = Asset(sum);
        if (exe <= 0 || checksums <= 0) throw new IOException("Release assets are not ready.");
        return new(version, tag, $"https://github.com/{Repository}/releases/tag/{tag}", exe, checksums, name);
    }
    public async Task<SelfRelease?> Check(CancellationToken cancel = default)
    {
        using var response = await Get($"https://api.github.com/repos/{Repository}/releases/latest", false, cancel);
        return Parse(await response.Content.ReadAsStringAsync(cancel), Current);
    }
    public static string Checksum(string text, string filename)
    {
        var matches = text.Split('\n').Select(line => Regex.Match(line.Trim(), @"^([a-fA-F0-9]{64})\s+\*?(.+)$")).Where(m => m.Success && m.Groups[2].Value == filename).ToList();
        if (matches.Count != 1) throw new IOException("Release checksum is missing or ambiguous.");
        return matches[0].Groups[1].Value.ToLowerInvariant();
    }
    public async Task<(string Path, string Hash)> Download(SelfRelease release, string folder, CancellationToken cancel)
    {
        async Task Copy(long id, string destination, long limit)
        {
            using var response = await Get($"https://api.github.com/repos/{Repository}/releases/assets/{id}", true, cancel);
            if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/json") throw new IOException("GitHub returned a page instead of the update file.");
            await using var input = await response.Content.ReadAsStreamAsync(cancel); await using var output = File.Create(destination);
            var buffer = new byte[81920]; long total = 0; int count;
            while ((count = await input.ReadAsync(buffer, cancel)) > 0) { total += count; if (total > limit) throw new IOException("Update asset exceeds its size limit."); await output.WriteAsync(buffer.AsMemory(0, count), cancel); }
        }
        Directory.CreateDirectory(folder);
        var sum = Path.Combine(folder, "SHA256SUMS.txt"); var path = Path.Combine(folder, "update.exe");
        await Copy(release.ChecksumId, sum, 65536);
        var hash = Checksum(await File.ReadAllTextAsync(sum, cancel), release.ExeName);
        await Copy(release.ExeId, path, 300L * 1024 * 1024);
        await using var file = File.OpenRead(path);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(file, cancel)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Update checksum verification failed. Nothing was installed.");
        return (path, hash);
    }
    public void Dispose() => client.Dispose();
}
