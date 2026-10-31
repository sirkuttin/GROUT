using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
namespace ReleaseDock;
public static class AppNames
{
    public static string Suggest(TrackedApp app)
    {
        var draft = AppLocationDraft.From(app);
        var path = draft.FileName.Length > 0 ? Path.Combine(draft.Folder, draft.FileName) : app.InstallLocation;
        try
        {
            if (Directory.Exists(path))
            {
                var files = Directory.EnumerateFiles(path, "*.exe").Where(p => !Regex.IsMatch(Path.GetFileName(p), "unins|setup|install|update|crash", RegexOptions.IgnoreCase)).Take(2).ToList();
                if (files.Count == 1) path = files[0];
            }
            if (File.Exists(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                foreach (var name in new[] { info.ProductName, info.FileDescription })
                    if (!string.IsNullOrWhiteSpace(name) && name.Length <= 120 && !name.Any(char.IsControl)) return name.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(path);
        return app.Repo.Length > 0 ? Repositories.Resolve(app.Repo).Path.Split('/').Last() : "App";
    }
}
