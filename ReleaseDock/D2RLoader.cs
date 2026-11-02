using System.IO;
using System.Text.RegularExpressions;
using ReleaseDock.Json;

namespace ReleaseDock;

public sealed record D2RLoaderItem(string Name, string Slug, string Summary, string Type);
public sealed record D2RLoaderPage(List<D2RLoaderItem> Items, int Page, int PageCount);
internal static class D2RLoader
{
    private static string Text(JsonElement item, string key) => NexusMods.Text(item, key);
    private static DateTimeOffset Date(JsonElement item, string key) => DateTimeOffset.TryParse(Text(item, key), out var time) ? time : DateTimeOffset.MinValue;
    internal static void Validate(JsonElement mod)
    {
        if (Text(mod, "status") != "published" || Text(mod, "visibility") != "public") throw new InvalidOperationException("This D2RLoader mod is not publicly published.");
        if (Text(mod, "name").Length == 0) throw new IOException("D2RLoader returned invalid mod metadata.");
    }
    internal static Release Parse(RepositoryRef source, JsonElement mod, bool previews)
    {
        Validate(mod);
        var release = mod.GetProperty("releases").EnumerateArray()
            .Where(r => Text(r, "status") == "published" && Date(r, "publishedAt") != DateTimeOffset.MinValue && Date(r, "publishedAt") <= DateTimeOffset.UtcNow && (previews || !ReleaseClient.IsPreviewTag(Text(r, "version"))))
            .OrderByDescending(r => Date(r, "publishedAt")).ThenByDescending(r => Date(r, "createdAt")).FirstOrDefault();
        if (release.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("No published D2RLoader release found for this channel.");
        var version = Text(release, "version"); var filename = Text(release, "fileName");
        if (!Guid.TryParse(Text(release, "id"), out var id) || version.Length == 0 || filename.Length == 0 || filename != Path.GetFileName(filename) || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new IOException("D2RLoader returned an invalid release file.");
        var sha = Text(release, "sha256");
        if (!Regex.IsMatch(sha, "^[a-fA-F0-9]{64}$")) throw new IOException("D2RLoader release has no valid SHA-256 checksum.");
        var size = release.GetProperty("sizeBytes").GetInt64(); if (size < 0) throw new IOException("D2RLoader returned an invalid download size.");
        var assets = new List<Asset>();
        if (Repositories.IsWindowsAsset(filename) || filename.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) assets.Add(new(filename, "https://d2rloader.net/api/v1/hub/releases/" + id.ToString("D") + "/download", size, "sha256:" + sha.ToLowerInvariant(), "d2rloader.net"));
        var notes = Text(release, "changelog");
        var loader = Text(release, "loaderVersion");
        if (loader.Length == 0 && mod.TryGetProperty("loaderVersion", out var required)) loader = Text(required, "version");
        if (loader.Length > 0) notes = "Requires D2RLoader " + loader + ".\n\n" + notes;
        var warning = "";
        if (release.TryGetProperty("scanSummary", out var scan) && scan.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array && targets.EnumerateArray().Any(t => Text(t, "status") is "flagged" or "suspicious"))
        {
            warning = "D2RLoader reports scan detections for this release. ";
            var reason = Text(release, "reviewReason"); if (reason.Length > 0) warning += reason + ". ";
            warning += "Review the source page for the scan report.";
        }
        return new(version, source.WebUrl, notes, assets, Title: Text(mod, "name"), SourceWarning: warning);
    }
    internal static D2RLoaderPage ParsePage(JsonElement root)
    {
        var page = root.GetProperty("page").GetInt32(); var pageSize = root.GetProperty("pageSize").GetInt32(); var total = root.GetProperty("total").GetInt32();
        if (page < 1 || pageSize < 1 || pageSize > 1000 || total < 0) throw new IOException("D2RLoader returned invalid pagination.");
        var items = root.GetProperty("items").EnumerateArray().Where(i => Text(i, "status") == "published" && Text(i, "visibility") == "public" && Regex.IsMatch(Text(i, "slug"), "^[a-z0-9][a-z0-9_-]*$"))
            .Select(i => new D2RLoaderItem(Text(i, "name"), Text(i, "slug"), Text(i, "summary"), Text(i, "type"))).ToList();
        return new(items, page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
    }
}
