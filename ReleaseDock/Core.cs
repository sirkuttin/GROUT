using System.IO;
using System.Net;
using System.Net.Http;
using ReleaseDock.Json;

using System.Text.RegularExpressions;

namespace ReleaseDock;

public sealed class TrackedApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ParentId { get; set; } = "";
    public int SiblingOrder { get; set; } = int.MaxValue;
    public bool IsGroup { get; set; }
    public bool Expanded { get; set; } = true;
    public string DefaultChildFolder { get; set; } = "";
    [JsonIgnore] public int TreeDepth { get; set; }
    [JsonIgnore] public int TreeOrder { get; set; }
    [JsonIgnore] public bool TreeVisible { get; set; } = true;
    [JsonIgnore] public bool HasChildren { get; set; }
    [JsonIgnore] public bool TreeFilterExpanded { get; set; }
    [JsonIgnore] public bool CanToggleTree => HasChildren && !TreeFilterExpanded;
    [JsonIgnore] public string TreeToggleHint => !HasChildren ? "Use Left arrow to select the parent." : TreeFilterExpanded ? "Clear search and Updates only to collapse this branch." : Expanded ? "Expanded. Left arrow collapses; Right arrow selects the first child." : "Collapsed. Right arrow expands.";
    [JsonIgnore] public string LibraryAutomationName => AppName + (HasChildren ? Expanded || TreeFilterExpanded ? ", expanded" : ", collapsed" : "") + (LibraryStatus.Length > 0 ? ", " + LibraryStatus : "");
    [JsonIgnore] public string OptionsName => "Options for " + AppName;
    [JsonIgnore] public string TreeToggleName => (Expanded || TreeFilterExpanded ? "Expanded: " : "Collapsed: ") + AppName;
    [JsonIgnore] public int BranchItemCount { get; set; }
    [JsonIgnore] public int BranchUpdateCount { get; set; }
    [JsonIgnore] public int BranchIssueCount { get; set; }
    [JsonIgnore] public bool ShowBranchSummary => HasChildren && !Expanded && !TreeFilterExpanded;
    [JsonIgnore] public string BranchSummary => BranchItemCount == 0 ? "" : BranchUpdateCount > 0 ? BranchUpdateCount + (BranchUpdateCount == 1 ? " update" : " updates") + (BranchIssueCount > 0 ? " · " + BranchIssueCount + (BranchIssueCount == 1 ? " issue" : " issues") : "") : BranchIssueCount > 0 ? BranchIssueCount + (BranchIssueCount == 1 ? " issue" : " issues") : "All up to date";
    [JsonIgnore] public string LibraryStatus => ShowBranchSummary ? BranchSummary : DisplayStatus;
    [JsonIgnore] public string LibraryStatusColor => ShowBranchSummary ? BranchIssueCount > 0 ? "#F0D495" : "#76DCC6" : StatusColor;
    [JsonIgnore] public string LibraryStatusHint => ShowBranchSummary ? $"{BranchItemCount} tracked item(s) in this branch. Includes this item and all descendants. Expand to see individual statuses." : Error;
    [JsonIgnore] public string TreeGlyph => HasChildren ? Expanded || TreeFilterExpanded ? "▾" : "▸" : "";
    [JsonIgnore] public System.Windows.Thickness TreeMargin => new(8 + TreeDepth * 16, 10, 8, 10);
    public string Repo { get; set; } = "";
    [JsonIgnore] public string RepositoryUrl => Repo.Length > 0 ? Repositories.Resolve(Repo).WebUrl : "";
    public string CustomName { get; set; } = "";
    public string ItemKind { get; set; } = "";
    public string TargetName { get; set; } = "";
    public string ModScope { get; set; } = "Custom";
    [JsonIgnore] public bool IsMod => !IsGroup && (ItemKind == "Mod" || ItemKind.Length == 0 && Repo.Length > 0 && Repositories.Resolve(Repo).Host == "d2rloader.net");
    [JsonIgnore] public string ItemLabel => IsMod ? "Mod" : "App";
    [JsonIgnore] public string TargetLabel => IsMod ? TargetName.Trim() : "";
    [JsonIgnore] public string InstallationSummary => IsMod ? (TargetLabel.Length > 0 ? TargetLabel + " · " : "") + AppLocationDraft.From(this).Folder : "";
    private string nameKey = "", suggestedName = "";
    internal void InvalidateSuggestedName() => nameKey = "";
    [JsonIgnore] public string AppName { get { if (!string.IsNullOrWhiteSpace(CustomName)) return CustomName.Trim(); var key = Repo + "|" + AppFolder + "|" + AppFile + "|" + InstallLocation + "|" + UpdateTarget + "|" + AssetName + "|" + (Release?.Title ?? ""); if (key != nameKey) { nameKey = key; suggestedName = AppNames.Suggest(this); } return suggestedName; } }
    [JsonIgnore] public string ProjectLabel { get { if (Repo.Length == 0) return ""; var source = Repositories.Resolve(Repo); return source.Path + (source.Host == "github.com" ? "" : " · " + source.Name); } }
    public string Installed { get; set; } = "";
    public string Latest { get; set; } = "";
    public string WebsiteDownloadUrl { get; set; } = "";
    public string AssetName { get; set; } = "";
    public long NexusFileId { get; set; }
    public bool IncludePrereleases { get; set; }
    public bool ShowAllDownloads { get; set; }
    public string InstallLocation { get; set; } = "";
    public bool AutoReconcile { get; set; }
    public string UpdateMethod { get; set; } = "Manual";
    public string UpdateTarget { get; set; } = "";
    public string ZipSubfolder { get; set; } = "";
    public bool ZipSourceIsFile { get; set; }
    public string D2RManagedFolder { get; set; } = "";
    public string LastNotifiedTag { get; set; } = "";
    public string LastBackup { get; set; } = "";
    public string UpdateState { get; set; } = "";
    [JsonIgnore] public string UpdateIssue => UpdateState is "Installed version detected" or "Marked installed manually" or "Previous files restored" || UpdateState.StartsWith("Portable app saved from release ", StringComparison.Ordinal) || UpdateState.StartsWith("Kept in library.", StringComparison.Ordinal) ? "" : UpdateState;
    public string DownloadedPath { get; set; } = "";
    public string DownloadedTag { get; set; } = "";
    public string InstalledProvenance { get; set; } = "";
    public string VersionRecordId { get; set; } = "";
    [JsonIgnore] public InstalledReleaseRecord? VersionRecord { get; set; }
    [JsonIgnore] public string VersionRecordError { get; set; } = "";
    public string PendingInstallerRelease { get; set; } = "";
    public bool InstallerWaitStopped { get; set; }
    [JsonIgnore] public string ActiveOperation { get; set; } = "";
    public string AppFolder { get; set; } = "";
    public string AppFile { get; set; } = "";
    public string VersionSourceOverride { get; set; } = "";
    public bool PartialUpdate { get; set; }
    public bool UninstallPending { get; set; }
    public string UninstallRegistrationId { get; set; } = "";
    public string UninstallTarget { get; set; } = "";
    [JsonIgnore] public bool PendingLocationConflict { get; set; }
    [JsonIgnore] public List<InstallationCandidate> InstallCandidates { get; set; } = [];
    [JsonIgnore] public string InstallDiscoveryNote { get; set; } = "";
    [JsonIgnore] public bool InstallationLocated { get; set; }
    [JsonIgnore] public IReadOnlyList<InstalledRegistration> InstallationBefore { get; set; } = [];
    [JsonIgnore] public string LocalStatus { get; set; } = "Manual version tracking";
    [JsonIgnore] public bool LocalError { get; set; }
    [JsonIgnore] public bool InstallationMissing { get; set; }
    [JsonIgnore] public string StatusColor => UninstallPending || InstallationMissing || LocalError || Error.Length > 0 || Release?.SourceWarning.Length > 0 || CanUpdate && ReleaseFiles.Match(this) == null ? "#F0D495" : "#76DCC6";
    public DateTimeOffset? CheckedAt { get; set; }
    public Release? Release { get; set; }
    public string Error { get; set; } = "";
    [JsonIgnore] public bool IsCurrent => Installed.Length > 0 && InstalledVersions.Matches(Installed, Latest);
    [JsonIgnore] public bool IsOlderRelease => InstalledVersions.CompareVersionNumbers(Installed, Latest) is > 0;
    [JsonIgnore] public bool CanUpdate => !UninstallPending && !InstallationMissing && Installed.Length > 0 && Latest.Length > 0 && (!IsCurrent || PartialUpdate) && !IsOlderRelease && Error.Length == 0 && !(AutoReconcile && LocalError) && Release?.Tag == Latest;
    [JsonIgnore] public string Status => UninstallPending ? "Uninstall pending" : InstallationMissing ? "App not found" : Error.Length > 0 ? "Update check failed" : AutoReconcile && LocalError ? "Installed version unverified" : PartialUpdate ? "Partial update — review files" : Latest.Length == 0 ? "Not checked" : Installed.Length == 0 ? "Version unknown" : IsCurrent ? "Up to date" : IsOlderRelease ? "Installed version is newer" : InstalledVersions.CompareVersionNumbers(Installed, Latest) is < 0 ? "Update available" : "Different release available";
    [JsonIgnore] public string Versions => $"{(InstallationMissing ? "Last known version" : "Installed")}: {(Installed.Length == 0 ? "unknown" : Installed)}    →    Latest: {(Latest.Length == 0 ? "unknown" : Latest)}";
    [JsonIgnore] public string DisplayStatus => ActiveOperation.Length > 0 ? "Updating…" : IsGroup ? "" : CanUpdate && ReleaseFiles.Match(this) == null ? ReleaseFiles.UpdateCandidates(this).Count > 0 ? "Choose download" : "No compatible download" : Status;
}
public sealed record Asset(string Name, string Url, long Size, string? Digest, string? OriginHost = null, long FileId = 0, string Version = "");
public sealed record Release(string Tag, string Url, string Notes, List<Asset> Assets, long SelectedFileId = 0, string Title = "", string SourceWarning = "");

public sealed class Store(string path)
{
    public List<TrackedApp> Load()
    {
        var apps = File.Exists(path) ? JsonSerializer.Deserialize<List<TrackedApp>>(File.ReadAllText(path)) ?? throw new JsonException("The saved library must contain a list of items, not null.") : [];
        InstalledReleaseRecords.Load(Path.GetDirectoryName(Path.GetFullPath(path))!, apps); return apps;
    }
    public void Save(IEnumerable<TrackedApp> apps)
    {
        Directory.CreateDirectory(Framework.GetDirectoryName(path)!);
        var list = apps.ToList(); var data = Path.GetDirectoryName(Path.GetFullPath(path))!;
        InstalledReleaseRecords.Save(data, list);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        Framework.Move(path + ".tmp", path, true);
        InstalledReleaseRecords.Clean(data, list);
    }
}
