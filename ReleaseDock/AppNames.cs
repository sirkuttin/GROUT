using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
namespace ReleaseDock;
public static class AppNames
{
    public static string Suggest(TrackedApp app)
    {
        if (app.Release?.Title.Length > 0 && app.Repo.Length > 0 && Repositories.Resolve(app.Repo).Host == "nexusmods.com") return app.Release.Title;
        var draft = AppLocationDraft.From(app);
        var path = draft.FileName.Length > 0 ? Path.Combine(draft.Folder.Trim().Trim('"'), draft.FileName) : draft.Folder.Trim().Trim('"');
        try
        {
            if (Directory.Exists(path))
            {
                var files = Directory.EnumerateFiles(path, "*.exe").Where(p => !Regex.IsMatch(Path.GetFileName(p), "unins|setup|install|update|crash", RegexOptions.IgnoreCase)).Take(2).ToList();
                if (files.Count == 1)
                {
                    static string Identity(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "");
                    var repoName = app.Repo.Length > 0 ? Identity(Repositories.Resolve(app.Repo).Path.Split('/').Last()) : "";
                    var folderName = Identity(Path.GetFileName(Framework.TrimEndingDirectorySeparator(path)));
                    var info = FileVersionInfo.GetVersionInfo(files[0]);
                    var identities = new[] { Identity(Path.GetFileNameWithoutExtension(files[0])), Identity(info.ProductName ?? ""), Identity(info.FileDescription ?? "") };
                    // A shared parent with one unrelated EXE is not the app.
                    if (identities.Any(n => n.Length > 0 && (n == repoName || n == folderName))) path = files[0];
                }
            }
            if (File.Exists(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                foreach (var name in new[] { info.ProductName, info.FileDescription })
                    if (!string.IsNullOrWhiteSpace(name) && name.Length <= 120 && !name.Any(char.IsControl)) return name.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        try { if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(path); } catch (ArgumentException) { }
        return app.Repo.Length > 0 ? Repositories.Resolve(app.Repo).Path.Split('/').Last() : "App";
    }
}
