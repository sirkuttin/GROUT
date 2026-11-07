using System.IO;
using ReleaseDock.Json;
using Microsoft.Win32;

namespace ReleaseDock;

public static class AppPaths
{
    public static string Data => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReleaseDock");
    public static string NewWork(string category)
    {
        var path = Path.Combine(Data, category, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
}

public sealed class Preferences
{
    public int CheckHours { get; set; } = 24;
    public bool Quiet { get; set; }
    public bool SelfUpdateChecks { get; set; } = true;
    public DateTimeOffset? SelfUpdateLastAttempt { get; set; }
    public string SelfUpdateNotifiedTag { get; set; } = "";
    public bool TrayExplained { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
    public DateTimeOffset? LastSuccess { get; set; }
    public DateTimeOffset? RetryAfter { get; set; }
    public Dictionary<string, DateTimeOffset> SourceBackoffs { get; set; } = [];
    public DateTimeOffset? NextCheck => CheckHours == 0 ? null : new[] { (LastAttempt ?? DateTimeOffset.MinValue).AddHours(CheckHours), RetryAfter ?? DateTimeOffset.MinValue }.Max();
    public bool IsDue(DateTimeOffset now) => NextCheck is { } next && now >= next;
    public static Preferences Load(string? path = null)
    {
        path ??= Path.Combine(AppPaths.Data, "settings.json");
        var result = File.Exists(path) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new() : new();
        if (result.CheckHours is not (0 or 1 or 6 or 24)) result.CheckHours = 24;
        return result;
    }
    public void Save(string? path = null)
    {
        path ??= Path.Combine(AppPaths.Data, "settings.json");
        Directory.CreateDirectory(Framework.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        Framework.Move(path + ".tmp", path, true);
    }
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool StartsWithWindows()
    {
        return StartupCommand() != null;
    }
    public static string? StartupCommand() { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("ReleaseDock") as string; }
    public static void SetStartupCommand(string? command) { using var key = Registry.CurrentUser.CreateSubKey(RunKey); if (command != null) key.SetValue("ReleaseDock", command); else key.DeleteValue("ReleaseDock", false); }
    public static void SetStartup(bool enabled)
    {
        SetStartupCommand(enabled ? $"\"{Framework.ProcessPath}\" --tray" : null);
    }
}

public static class ReleaseFiles
{
    public static string FileLabel(Asset asset) => asset.Name + (asset.FileId > 0 ? " · v" + asset.Version : "") + " · " + SizeText(asset);
    public static void Select(TrackedApp app, Asset asset) { app.AssetName = asset.Name; app.NexusFileId = asset.FileId; if (asset.FileId > 0) { app.Latest = asset.Version; if (app.Release != null) app.Release = app.Release with { Tag = asset.Version, SelectedFileId = asset.FileId }; } }
    public static string SizeText(Asset asset) => asset.Size >= 0 ? $"{asset.Size / 1048576d:F1} MB" : "size unknown";
    public static bool TakeNotification(TrackedApp app, bool quiet)
    {
        if (!app.CanUpdate || app.LastNotifiedTag == app.Latest) return false;
        app.LastNotifiedTag = app.Latest;
        return !quiet;
    }
    public static string Family(string name)
    {
        var family = System.Text.RegularExpressions.Regex.Replace(name, @"\d+\.\d+(?:\.\d+)*", "{version}");
        return family.Contains("{version}") ? System.Text.RegularExpressions.Regex.Replace(family, @"(?<=\{version\})[._ -]+[rR]\d+(?=\.[^.]+$)", "") : family;
    }
    public static List<Asset> UpdateCandidates(TrackedApp app) => AssetCompatibility.Candidates(app.Release?.Assets ?? [], app.ShowAllDownloads).Where(a => app.UpdateMethod switch { "Zip" => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase), "ReplaceExe" => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), "Installer" => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase), _ => true }).ToList();
    public static string SelectionExplanation(TrackedApp app)
    {
        if (!app.CanUpdate || Match(app) != null) return "";
        if (UpdateCandidates(app).Count == 0) return "Release " + app.Latest + " has no compatible download for this update method. Open the release page or change the update method in settings.";
        return app.AssetName.Length == 0 ? "Choose the file to use for this update. Your choice will be remembered." : "The saved download, " + app.AssetName + ", could not be matched in release " + app.Latest + ". Choose its replacement to continue.";
    }
    public static Asset? Match(TrackedApp app)
    {
        var files = UpdateCandidates(app);
        if (app.Repo.Length > 0 && Repositories.Resolve(app.Repo).Host == "nexusmods.com") return files.FirstOrDefault(a => a.FileId == (app.Release?.SelectedFileId > 0 ? app.Release.SelectedFileId : app.NexusFileId) && a.FileId > 0);
        var exact = files.FirstOrDefault(a => a.Name == app.AssetName);
        if (exact != null) return exact;
        var family = files.Where(a => Family(a.Name) == Family(app.AssetName)).ToList();
        return family.Count == 1 ? family[0] : null;
    }
    public static string Label(string method, bool mod = false) => method switch { "ReplaceExe" => "Replace app EXE", "Installer" => "Run installer", "Zip" => mod ? "Extract ZIP into mod folder" : "Extract ZIP into app folder", _ => "Download only" };
    public static void Validate(TrackedApp app, Asset asset)
    {
        if (app.UpdateMethod is not ("Manual" or "ReplaceExe" or "Installer" or "Zip")) throw new InvalidOperationException("Choose a supported update method in App settings.");
        var ext = Path.GetExtension(asset.Name).ToLowerInvariant();
        if (app.UpdateMethod == "Installer" && ext is not (".exe" or ".msi")) throw new InvalidOperationException("Installer mode needs an EXE or MSI release file.");
        if (app.UpdateMethod == "ReplaceExe" && (ext != ".exe" || !app.UpdateTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Replacement needs an EXE download and an exact EXE destination.");
        if (app.UpdateMethod == "Zip" && ext != ".zip") throw new InvalidOperationException("Extraction needs a ZIP release file.");
        if (app.UpdateMethod == "Zip") FileUpdates.NormalizeZipSubfolder(app.ZipSubfolder);
        if (app.UpdateMethod is "Zip" or "ReplaceExe")
        {
            if (!Framework.IsPathFullyQualified(app.UpdateTarget)) throw new InvalidOperationException("Choose an absolute update destination.");
            var target = Path.GetFullPath(app.UpdateTarget);
            if (target.TrimEnd('\\') == Path.GetPathRoot(target)?.TrimEnd('\\')) throw new InvalidOperationException("Choose an app folder, not a drive root.");
            if (target.StartsWith(AppPaths.Data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || target.Equals(AppPaths.Data, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose an app destination outside GROUT's own data folder.");
            if (File.Exists(target) && app.UpdateMethod == "Zip" || Directory.Exists(target) && app.UpdateMethod == "ReplaceExe") throw new InvalidOperationException("The destination type does not match the update method.");
            if (target.Equals(Framework.ProcessPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("GROUT cannot replace its own running executable.");
        }
    }
}
