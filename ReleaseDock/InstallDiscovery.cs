using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ReleaseDock;

public sealed record InstalledRegistration(string Id, string Name, string Version, string Location, string Icon, string Uninstall);
public sealed record InstallationCandidate(InstalledRegistration Entry, string Folder, string AppFile, int Score, bool Changed)
{
    public string Label => $"{Entry.Name} · {Entry.Version}\n{Folder}";
}

public sealed class InstallDiscovery(Func<IReadOnlyList<InstalledRegistration>>? reader = null)
{
    public IReadOnlyList<InstalledRegistration> Snapshot() => (reader ?? ReadRegistry)();
    public static IReadOnlyList<InstalledRegistration> ReadRegistry()
    {
        var result = new List<InstalledRegistration>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (var id in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var entry = uninstall.OpenSubKey(id); if (entry == null) continue;
                        string Value(string name) => entry.GetValue(name) as string ?? "";
                        var name = Value("DisplayName"); if (name.Length == 0) continue;
                        result.Add(new($"{hive}/{view}/{id}", name, Value("DisplayVersion"), Value("InstallLocation"), Value("DisplayIcon"), Value("UninstallString")));
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        return result;
    }
    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]", "");
    private static string AssetIdentity(string name) => Normalize(Regex.Replace(Path.GetFileNameWithoutExtension(name), @"\d+(?:[._-]\d+)*|(?i:setup|installer|install|windows|win64|win32|x64|x86|arm64|portable)", ""));
    public static string ExePath(string raw)
    {
        raw = Environment.ExpandEnvironmentVariables(raw.Trim().TrimStart('@'));
        var match = Regex.Match(raw, "^\"([^\"]+\\.exe)\"|^(.+?\\.exe)(?:,|\\s|$)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value : "";
    }
    public List<InstallationCandidate> Find(TrackedApp app, IReadOnlyList<InstalledRegistration>? before = null)
    {
        var project = app.Repo.Length > 0 ? Repositories.Resolve(app.Repo).Path.Split('/').Last() : "";
        var identities = new[] { Normalize(project), AssetIdentity(app.AssetName) }.Where(n => n.Length >= 3).Distinct().ToList();
        var previous = (before ?? []).ToDictionary(e => e.Id, e => e);
        var found = new List<InstallationCandidate>();
        foreach (var entry in Snapshot())
        {
            var name = Normalize(entry.Name); var icon = ExePath(entry.Icon); var executableName = Normalize(Path.GetFileNameWithoutExtension(icon));
            var exact = identities.Any(n => n == name || n == executableName);
            var related = identities.Any(n => n.Length >= 5 && (name.Contains(n) || n.Contains(name) && name.Length >= 5));
            if (!exact && !related) continue;
            var folder = Environment.ExpandEnvironmentVariables(entry.Location.Trim().Trim('"')).TrimEnd('\\', '/');
            if (folder.Length == 0 && icon.Length > 0) folder = Framework.GetDirectoryName(icon) ?? "";
            if (folder.Length == 0) folder = Framework.GetDirectoryName(ExePath(entry.Uninstall)) ?? "";
            if (!Framework.IsPathFullyQualified(folder) || folder.StartsWith(@"\\") || !Directory.Exists(folder)) continue;
            var filename = icon.Length > 0 && File.Exists(icon) && string.Equals(Framework.GetDirectoryName(icon)?.TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(Path.GetFileName(icon), "(?i)unins|setup|installer|update|crash") ? Path.GetFileName(icon) : "";
            var changed = before != null && (!previous.TryGetValue(entry.Id, out var old) || old != entry);
            var score = (exact ? 100 : 55) + (changed ? 25 : 0) + (InstalledVersions.Matches(entry.Version, app.Latest) ? 10 : 0);
            found.Add(new(entry, folder, filename, score, changed));
        }
        return found.GroupBy(c => c.Folder + "|" + c.AppFile, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(c => c.Score).First()).OrderByDescending(c => c.Score).ThenBy(c => c.Entry.Name).ToList();
    }
    public static InstallationCandidate? ClearMatch(IReadOnlyList<InstallationCandidate> candidates) => candidates.Count > 0 && candidates[0].Score >= 100 && (candidates.Count == 1 || candidates[0].Score - candidates[1].Score >= 20) ? candidates[0] : null;
    public static void Apply(TrackedApp app, InstallationCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.Entry.Version)) app.VersionRecord = null;
        app.InstallationLocated = true;
        app.AppFolder = candidate.Folder; app.AppFile = candidate.AppFile; app.VersionSourceOverride = "";
        app.InstallLocation = candidate.AppFile.Length > 0 ? Path.Combine(candidate.Folder, candidate.AppFile) : candidate.Folder;
        app.AutoReconcile = true; InstalledVersions.Reconcile(app);
        if (app.LocalError)
        {
            app.AutoReconcile = false; app.LocalError = false;
            if (!string.IsNullOrWhiteSpace(candidate.Entry.Version)) app.Installed = candidate.Entry.Version;
            app.InstalledProvenance = candidate.Entry.Version.Length > 0 ? $"Windows installed-app entry {candidate.Entry.Name} reports {candidate.Entry.Version}. Automatic file-version detection is unavailable." : $"Windows registered {candidate.Entry.Name} here, but supplied no usable version. Automatic file-version detection is unavailable.";
            InstalledVersions.Reconcile(app);
        }
        app.InstallDiscoveryNote = $"Found {candidate.Entry.Name} in Windows installed apps: {candidate.Folder}";
    }
    public static bool CanFinish(TrackedApp app) => app.InstallationLocated && ClearMatch(app.InstallCandidates) is { } candidate && (candidate.Changed || app.IsCurrent) && InstalledVersions.CompareVersionNumbers(app.Installed, app.Latest) is not < 0 && (app.AutoReconcile || candidate.AppFile.Length > 0 && File.Exists(Path.Combine(candidate.Folder, candidate.AppFile)));
}
