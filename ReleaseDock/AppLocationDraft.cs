using System.IO;

namespace ReleaseDock;

public sealed class AppLocationDraft
{
    public string Folder { get; set; } = "";
    public string FileName { get; set; } = "";
    public string SourceOverride { get; set; } = "";
    public bool Conflict { get; private set; }
    public string OriginalSource { get; private set; } = "";
    public string OriginalTarget { get; private set; } = "";
    private static bool IsExe(string path) => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    private static bool IsJson(string path) => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    private static string Parent(string path) => Framework.GetDirectoryName(path) ?? "";
    private static bool Same(string a, string b) => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    public static AppLocationDraft From(TrackedApp app)
    {
        var source = app.InstallLocation;
        var draft = new AppLocationDraft { OriginalSource = source, OriginalTarget = app.UpdateTarget };
        if (app.AppFolder.Length > 0) { draft.Folder = app.AppFolder; draft.FileName = app.AppFile; draft.SourceOverride = app.VersionSourceOverride; }
        else if (IsExe(source)) { draft.Folder = Parent(source); draft.FileName = Path.GetFileName(source); }
        else if (IsJson(source)) { draft.Folder = Parent(source); draft.SourceOverride = Path.GetFileName(source); }
        else draft.Folder = source;
        if (app.UpdateTarget.Length > 0)
        {
            var targetFolder = IsExe(app.UpdateTarget) ? Parent(app.UpdateTarget) : app.UpdateTarget;
            if (draft.Folder.Length == 0) { draft.Folder = targetFolder; if (IsExe(app.UpdateTarget)) draft.FileName = Path.GetFileName(app.UpdateTarget); }
            else draft.Conflict = !Same(draft.Folder, targetFolder) || (draft.FileName.Length > 0 && IsExe(app.UpdateTarget) && !Same(draft.FileName, Path.GetFileName(app.UpdateTarget)));
        }
        draft.Conflict |= app.PendingLocationConflict;
        return draft;
    }
    public void Resolve(bool useExistingSource)
    {
        if (useExistingSource)
        {
            if (IsExe(OriginalSource)) { Folder = Parent(OriginalSource); FileName = Path.GetFileName(OriginalSource); SourceOverride = ""; }
            else if (IsJson(OriginalSource)) { Folder = Parent(OriginalSource); SourceOverride = Path.GetFileName(OriginalSource); }
            else if (OriginalSource.Length > 0) { Folder = OriginalSource; SourceOverride = ""; }
        }
        else
        {
            Folder = IsExe(OriginalTarget) ? Parent(OriginalTarget) : OriginalTarget;
            if (IsExe(OriginalTarget)) FileName = Path.GetFileName(OriginalTarget);
            SourceOverride = OriginalSource; // Explicitly preserve a separate, previously configured version source.
        }
        Conflict = false;
    }
    public string VersionSource => SourceOverride.Length > 0 ? Framework.IsPathFullyQualified(SourceOverride) ? SourceOverride : Path.Combine(Folder.Trim().Trim('"'), SourceOverride) : FileName.Length > 0 ? Path.Combine(Folder.Trim().Trim('"'), FileName) : Folder.Trim().Trim('"');
    public void Apply(TrackedApp app, string method)
    {
        if (Conflict) throw new InvalidOperationException("Choose which saved app location to keep before saving.");
        Folder = Folder.Trim().Trim('"'); FileName = FileName.Trim();
        if (Folder.Length > 0 && (!Framework.IsPathFullyQualified(Folder) || File.Exists(Folder))) throw new InvalidOperationException("App folder must be a full directory path.");
        if (FileName.Length > 0 && (FileName.Contains('\\') || FileName.Contains('/') || FileName != Path.GetFileName(FileName))) throw new InvalidOperationException("App file is a filename only, not another path.");
        if (FileName.Length > 0) FileUpdates.SafeRelative(FileName);
        var target = method == "ReplaceExe" ? SetupPaths.PortableExe(Folder, FileName) : method == "Zip" ? Folder : "";
        app.AppFolder = Folder; app.AppFile = FileName; app.VersionSourceOverride = SourceOverride;
        app.PendingLocationConflict = false;
        app.InstallLocation = VersionSource; app.UpdateTarget = target; app.UpdateMethod = method;
    }
}
