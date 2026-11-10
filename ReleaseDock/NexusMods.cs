using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ReleaseDock.Json;

namespace ReleaseDock;

public sealed record NexusAccount(string Key, string Name, bool Premium);
internal static class NexusCredentials
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GROUT Nexus connection v1");
    internal static string PathName => Path.Combine(AppPaths.Data, "nexus-connection.bin");
    internal static NexusAccount? Load(string? path = null)
    {
        path ??= PathName;
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<NexusAccount>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return null; }
    }
    internal static void Save(NexusAccount account, string? path = null)
    {
        path ??= PathName; Directory.CreateDirectory(Framework.GetDirectoryName(path)!);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(account)), Entropy, DataProtectionScope.CurrentUser);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); Framework.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static void Disconnect() { if (File.Exists(PathName)) File.Delete(PathName); }
}

public sealed class NexusMods : IDisposable
{
    internal static bool TestMode { get; set; }
    public static bool Enabled
    {
        get
        {
#if NEXUS_TESTING
            return true;
#else
            return TestMode;
#endif
        }
    }
    private readonly HttpClient client;
    private readonly Func<NexusAccount?> account;
    public DateTimeOffset? PausedUntil { get; set; }
    public bool CanDownload => Enabled && account()?.Premium == true;
    public NexusMods(HttpMessageHandler? handler = null, Func<NexusAccount?>? account = null)
    {
        client = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(30); this.account = account ?? (() => NexusCredentials.Load());
    }
    internal static string Text(JsonElement root, string key) => root.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private static long Number(JsonElement root, string key, long fallback = 0) => root.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number ? item.GetInt64() : fallback;
    private static bool Flag(JsonElement root, string key) => root.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.True;
    private async Task<JsonDocument> Get(string url, CancellationToken cancel, string? keyOverride = null)
    {
        if (!Enabled) throw new InvalidOperationException("Nexus Mods support is not available in this public release yet. Your saved entry is kept.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "api.nexusmods.com" || !uri.IsDefaultPort || uri.UserInfo.Length > 0) throw new InvalidOperationException("Invalid Nexus API endpoint.");
        if (PausedUntil > DateTimeOffset.UtcNow) throw new InvalidOperationException("Nexus checks are paused until " + PausedUntil.Value.ToLocalTime().ToString("g") + ".");
        var key = (keyOverride ?? account()?.Key ?? "").Trim();
        if (key.Length == 0 || key.Any(char.IsControl)) throw new InvalidOperationException("Connect Nexus Mods in Settings to check this source.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("apikey", key); request.Headers.Add("Application-Name", "GROUT"); request.Headers.Add("Application-Version", SelfUpdater.CurrentLabel);
        request.Headers.Accept.ParseAdd("application/json"); request.Headers.UserAgent.ParseAdd("GROUT/" + SelfUpdater.Current);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancel);
        // Do not follow redirects or forward the user's key to a file host.
        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw new IOException("Nexus API redirected unexpectedly. No credentials were forwarded.");
        var hourlyEmpty = response.Headers.TryGetValues("x-rl-hourly-remaining", out var hourly) && hourly.FirstOrDefault() == "0";
        var dailyEmpty = response.Headers.TryGetValues("x-rl-daily-remaining", out var daily) && daily.FirstOrDefault() == "0";
        var exhausted = hourlyEmpty || dailyEmpty;
        if ((int)response.StatusCode == 429 || exhausted)
        {
            var until = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1));
            foreach (var header in dailyEmpty ? new[] { "x-rl-daily-reset", "x-rl-hourly-reset" } : new[] { "x-rl-hourly-reset" })
                if (response.Headers.TryGetValues(header, out var values))
                {
                    var raw = values.FirstOrDefault(); DateTimeOffset reset;
                    if (DateTimeOffset.TryParse(raw, out reset) && reset > until) until = reset;
                    else if (long.TryParse(raw, out var unix) && unix > 0 && unix < 253402300799 && DateTimeOffset.FromUnixTimeSeconds(unix) > until) until = DateTimeOffset.FromUnixTimeSeconds(unix);
                }
            PausedUntil = until > DateTimeOffset.UtcNow.AddMinutes(1) ? until : DateTimeOffset.UtcNow.AddMinutes(1);
            if ((int)response.StatusCode == 429) throw new InvalidOperationException("Nexus request limit reached. Checks will resume after " + PausedUntil.Value.ToLocalTime().ToString("g") + ".");
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("Nexus connection expired or was revoked. Reconnect in Settings.");
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new InvalidOperationException("Your Nexus account cannot access this item. Check its permissions and download availability on Nexus.");
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("Nexus mod or file not found. It may have moved or been removed.");
        response.EnsureSuccessStatusCode();
        if (response.Content == null || response.Content.Headers.ContentType?.MediaType == "text/html") throw new IOException("Nexus returned a page instead of API data.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
    }
    public async Task<NexusAccount> Validate(string key, CancellationToken cancel = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var data = await Get("https://api.nexusmods.com/v1/users/validate.json", timeout.Token, key);
        var name = Text(data.RootElement, "name"); if (name.Length == 0) throw new IOException("Nexus did not return a valid account.");
        return new(key.Trim(), name, Flag(data.RootElement, "is_premium"));
    }
    public async Task Verify(RepositoryRef source)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); using var mod = await Get(source.ApiUrl + ".json", timeout.Token);
        if (Text(mod.RootElement, "name").Length == 0) throw new IOException("Nexus did not return a mod page.");
    }
    public async Task<Release> Latest(RepositoryRef source, long selectedId = 0, CancellationToken cancel = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var mod = await Get(source.ApiUrl + ".json", timeout.Token);
        using var files = await Get(source.ApiUrl + "/files.json", timeout.Token);
        return Parse(source, mod.RootElement, files.RootElement, selectedId);
    }
    internal static Release Parse(RepositoryRef source, JsonElement mod, JsonElement data, long selectedId)
    {
        var active = data.GetProperty("files").EnumerateArray().Where(f => Number(f, "file_id") > 0 && Number(f, "category_id") is 1 or 2 or 3 or 5).OrderByDescending(f => Number(f, "uploaded_timestamp")).ToList();
        var id = selectedId;
        if (id > 0 && data.TryGetProperty("file_updates", out var updates))
        {
            var links = updates.EnumerateArray().OrderByDescending(f => Number(f, "uploaded_timestamp")).GroupBy(f => Number(f, "old_file_id")).ToDictionary(g => g.Key, g => Number(g.First(), "new_file_id"));
            var visited = new HashSet<long>();
            while (links.TryGetValue(id, out var next) && next > 0 && next != id) { if (!visited.Add(id)) throw new IOException("Nexus returned a circular file-update chain."); id = next; }
        }
        var chosen = id > 0 ? active.FirstOrDefault(f => Number(f, "file_id") == id) : active.FirstOrDefault(f => Flag(f, "is_primary"));
        if (id == 0 && chosen.ValueKind == JsonValueKind.Undefined) chosen = active.FirstOrDefault(f => Number(f, "category_id") == 1);
        var chosenId = Number(chosen, "file_id");
        string Version(JsonElement file) { var version = Text(file, "version"); return version.Length > 0 ? version : Text(mod, "version"); }
        var assets = new List<Asset>();
        foreach (var file in active)
        {
            var name = Text(file, "file_name"); var fileId = Number(file, "file_id");
            if (!Repositories.IsWindowsAsset(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name != Path.GetFileName(name)) continue;
            // Legacy size/size_kb values are rounded KB, not exact byte counts.
            assets.Add(new(name, source.ApiUrl + "/files/" + fileId + ".json", Number(file, "size_in_bytes", -1), null, "nexusmods.com", fileId, Version(file)));
        }
        if (!assets.Any(a => a.FileId == chosenId)) chosenId = 0;
        var versionText = chosenId > 0 ? assets.Single(a => a.FileId == chosenId).Version : Text(mod, "version");
        var notes = WebUtility.HtmlDecode(Regex.Replace(Text(chosen, "changelog_html"), "<[^>]+>", " "));
        if (selectedId > 0 && chosenId == 0) notes = "The tracked Nexus file is no longer available. Choose its replacement in App settings.\n" + notes;
        return new(versionText, source.ReleasesUrl, notes, assets, chosenId, Text(mod, "name"));
    }
    public async Task<string> DownloadUrl(Asset asset, CancellationToken cancel)
    {
        if (asset.OriginHost != "nexusmods.com" || asset.FileId <= 0 || !Regex.IsMatch(asset.Url, @"^https://api\.nexusmods\.com/v1/games/[a-z0-9_-]+/mods/[1-9][0-9]*/files/" + asset.FileId + @"\.json$")) throw new InvalidOperationException("Invalid Nexus file reference.");
        // Verify current account status before requesting a Premium-only direct URL.
        var current = account() ?? throw new InvalidOperationException("Connect Nexus Mods in Settings first.");
        var validated = await Validate(current.Key, cancel);
        if (!validated.Premium) throw new InvalidOperationException("Nexus Premium is required for direct downloads. Open this file on Nexus to download it in your browser.");
        using var links = await Get(asset.Url.Substring(0, asset.Url.Length - 5) + "/download_link.json", cancel);
        foreach (var link in links.RootElement.EnumerateArray())
        {
            var url = Text(link, "URI");
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.IsDefaultPort) return uri.AbsoluteUri;
        }
        throw new IOException("Nexus did not provide a download URL for this file.");
    }
    public static string FilePage(string repo, Asset asset) => Repositories.Resolve(repo).WebUrl + "?tab=files&file_id=" + asset.FileId;
    public void Dispose() => client.Dispose();
}
