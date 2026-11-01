using System.IO;
using System.Net;
using System.Net.Http;
using ReleaseDock.Json;

using System.Text.RegularExpressions;

namespace ReleaseDock;

public sealed class TrackedApp
{
    public string Repo { get; set; } = "";
    [JsonIgnore] public string RepositoryUrl => Repo.Length > 0 ? Repositories.Resolve(Repo).WebUrl : "";
    public string CustomName { get; set; } = "";
    private string nameKey = "", suggestedName = "";
    internal void InvalidateSuggestedName() => nameKey = "";
    [JsonIgnore] public string AppName { get { if (!string.IsNullOrWhiteSpace(CustomName)) return CustomName.Trim(); var key = Repo + "|" + AppFolder + "|" + AppFile + "|" + InstallLocation + "|" + UpdateTarget + "|" + AssetName; if (key != nameKey) { nameKey = key; suggestedName = AppNames.Suggest(this); } return suggestedName; } }
    [JsonIgnore] public string ProjectLabel { get { if (Repo.Length == 0) return ""; var source = Repositories.Resolve(Repo); return source.Path + (source.Host == "github.com" ? "" : " · " + source.Name); } }
    public string Installed { get; set; } = "";
    public string Latest { get; set; } = "";
    public string AssetName { get; set; } = "";
    public bool IncludePrereleases { get; set; }
    public bool ShowAllDownloads { get; set; }
    public string InstallLocation { get; set; } = "";
    public bool AutoReconcile { get; set; }
    public string UpdateMethod { get; set; } = "Manual";
    public string UpdateTarget { get; set; } = "";
    public string LastNotifiedTag { get; set; } = "";
    public string LastBackup { get; set; } = "";
    public string UpdateState { get; set; } = "";
    [JsonIgnore] public string UpdateIssue => UpdateState is "Installed version detected" or "Marked installed manually" or "Previous files restored" || UpdateState.StartsWith("Portable app saved from release ", StringComparison.Ordinal) || UpdateState.StartsWith("Kept in library.", StringComparison.Ordinal) ? "" : UpdateState;
    public string DownloadedPath { get; set; } = "";
    public string DownloadedTag { get; set; } = "";
    public string InstalledProvenance { get; set; } = "";
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
    [JsonIgnore] public string StatusColor => UninstallPending || InstallationMissing || LocalError || Error.Length > 0 ? "#F0D495" : "#76DCC6";
    public DateTimeOffset? CheckedAt { get; set; }
    public Release? Release { get; set; }
    public string Error { get; set; } = "";
    [JsonIgnore] public bool IsCurrent => Installed.Length > 0 && InstalledVersions.Matches(Installed, Latest);
    [JsonIgnore] public bool IsOlderRelease => InstalledVersions.CompareVersionNumbers(Installed, Latest) is > 0;
    [JsonIgnore] public bool CanUpdate => !UninstallPending && !InstallationMissing && Installed.Length > 0 && Latest.Length > 0 && (!IsCurrent || PartialUpdate) && !IsOlderRelease && Error.Length == 0 && !(AutoReconcile && LocalError) && Release?.Tag == Latest;
    [JsonIgnore] public string Status => UninstallPending ? "Uninstall pending" : InstallationMissing ? "App not found" : Error.Length > 0 ? "Update check failed" : AutoReconcile && LocalError ? "Installed version unverified" : PartialUpdate ? "Partial update — review files" : Latest.Length == 0 ? "Not checked" : Installed.Length == 0 ? "Version unknown" : IsCurrent ? "Up to date" : IsOlderRelease ? "Installed version is newer" : InstalledVersions.CompareVersionNumbers(Installed, Latest) is < 0 ? "Update available" : "Different release available";
    [JsonIgnore] public string Versions => $"{(InstallationMissing ? "Last known version" : "Installed")}: {(Installed.Length == 0 ? "unknown" : Installed)}    →    Latest: {(Latest.Length == 0 ? "unknown" : Latest)}";
    [JsonIgnore] public string DisplayStatus => CanUpdate && ReleaseFiles.Match(this) == null ? Release?.Assets.Any(a => Repositories.IsWindowsAsset(a.Name)) == true ? "Choose release download" : "No app downloads found" : Status;
}
public sealed record Asset(string Name, string Url, long Size, string? Digest, string? OriginHost = null);
public sealed record Release(string Tag, string Url, string Notes, List<Asset> Assets);

public sealed class Store(string path)
{
    public List<TrackedApp> Load() => File.Exists(path) ? JsonSerializer.Deserialize<List<TrackedApp>>(File.ReadAllText(path)) ?? [] : [];
    public void Save(IEnumerable<TrackedApp> apps)
    {
        Directory.CreateDirectory(Framework.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(apps, new JsonSerializerOptions { WriteIndented = true }));
        Framework.Move(path + ".tmp", path, true);
    }
}
