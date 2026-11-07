using System.IO;
using ReleaseDock.Json;

namespace ReleaseDock;

public sealed record InstalledReleaseRecord(string Source, string Version, string Location, string Method, string[] Files, DateTimeOffset AppliedAt, string IgnoredSource = "", string IgnoredHash = "", string RegistrationId = "", string NativeVersion = "", string AppliedVersion = "", string Evidence = "Applied");

public static class InstalledReleaseRecords
{
    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    internal static string Fingerprint(string path) { using var file = File.OpenRead(path); return Framework.Hex(Framework.Hash(file)); }
    public static void Record(TrackedApp app, IEnumerable<string> files, string registrationId = "")
    {
        if (app.Latest.Length == 0 || app.InstallLocation.Length == 0) throw new InvalidOperationException("An installed location and applied release are required for version tracking.");
        var installed = files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (installed.Length == 0) throw new InvalidOperationException("No installed app files were found to record.");
        var ignored = ""; var hash = ""; var native = "";
        try { var found = InstalledVersions.Read(app.InstallLocation, InstalledVersions.UsesD2RLoaderManifest(app)); ignored = found.Source; native = found.Version; hash = Fingerprint(ignored); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception) { }
        if (!Guid.TryParseExact(app.VersionRecordId, "N", out _)) app.VersionRecordId = Guid.NewGuid().ToString("N");
        app.VersionRecord = new(app.Repo, app.Latest, Path.GetFullPath(app.InstallLocation), app.UpdateMethod, installed, DateTimeOffset.UtcNow, ignored, hash, registrationId, native, app.Latest);
        app.Installed = app.Latest; app.AutoReconcile = true; app.LocalError = false; app.InstallationMissing = false;
        app.InstalledProvenance = "Saved from " + (app.Repo.Length > 0 ? Repositories.Resolve(app.Repo).Name : "app") + " release " + app.Latest + ". Version recorded by GROUT after installation.";
        app.LocalStatus = app.InstalledProvenance; app.UpdateState = "";
    }
    public static bool ValidLocation(TrackedApp app, InstalledReleaseRecord record) => record.Source == app.Repo && app.InstallLocation.Length > 0 && Same(record.Location, app.InstallLocation);
    public static bool NewSource(InstalledReleaseRecord record, InstalledVersion found) => record.IgnoredSource.Length == 0 || !Same(record.IgnoredSource, found.Source) || !string.Equals(record.IgnoredHash, Fingerprint(found.Source), StringComparison.OrdinalIgnoreCase);
    internal static string FilePath(string data, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new IOException("Invalid GROUT version record ID.");
        return Path.Combine(data, "versions", id + ".json");
    }
    public static void Load(string data, IEnumerable<TrackedApp> apps)
    {
        foreach (var app in apps.Where(a => a.VersionRecordId.Length > 0))
        {
            try
            {
                var path = FilePath(data, app.VersionRecordId); FileUpdates.NoLinks(path);
                if (File.Exists(path) && new FileInfo(path).Length <= 2 * 1024 * 1024) app.VersionRecord = JsonSerializer.Deserialize<InstalledReleaseRecord>(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { app.VersionRecord = null; }
        }
    }
    public static void Save(string data, IEnumerable<TrackedApp> apps)
    {
        foreach (var app in apps.Where(a => a.VersionRecord != null))
        {
            var path = FilePath(data, app.VersionRecordId); FileUpdates.NoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(app.VersionRecord)); Framework.Move(path + ".tmp", path, true);
        }
    }
    public static void Clean(string data, IEnumerable<TrackedApp> apps)
    {
        var folder = Path.Combine(data, "versions"); if (!Directory.Exists(folder)) return; FileUpdates.NoLinks(folder);
        var keep = apps.Where(a => a.VersionRecord != null).Select(a => a.VersionRecordId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _) && !keep.Contains(Path.GetFileNameWithoutExtension(path))) { FileUpdates.NoLinks(path); File.Delete(path); }
    }
}
