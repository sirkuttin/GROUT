using System.IO;
using System.Text.Json;
namespace ReleaseDock;
public static class StorageCleanup
{
    public static void DeleteFolder(string path, string root)
    {
        try
        {
            path = Path.GetFullPath(path); root = Path.GetFullPath(root).TrimEnd('\\', '/');
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(path)) return;
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return;
                if (Directory.Exists(entry)) DeleteFolder(entry, path); else File.Delete(entry);
            }
            Directory.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }
    public static bool SafeBackup(string folder)
    {
        try
        {
            var file = Path.Combine(folder, "recovery.json");
            if (!File.Exists(file)) return !Directory.EnumerateFileSystemEntries(folder).Any();
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            return json.RootElement.GetProperty("State").GetString() is "Applied" or "Restored" or "Rolled back";
        }
        catch { return false; }
    }
    public static void Sweep(IEnumerable<TrackedApp> apps, string? data = null)
    {
        data ??= AppPaths.Data;
        var list = apps.ToList();
        foreach (var category in new[] { "downloads", "staging", "backups" })
        {
            var root = Path.Combine(data, category); if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                if (category == "downloads" && list.Any(a => !a.IsCurrent && Path.GetDirectoryName(a.DownloadedPath)?.Equals(folder, StringComparison.OrdinalIgnoreCase) == true)) continue;
                if (category == "backups" && !SafeBackup(folder)) continue;
                DeleteFolder(folder, root);
            }
        }
        foreach (var app in list)
        {
            if (!File.Exists(app.DownloadedPath)) { app.DownloadedPath = ""; app.DownloadedTag = ""; }
            if (!Directory.Exists(app.LastBackup)) app.LastBackup = "";
        }
    }
}
