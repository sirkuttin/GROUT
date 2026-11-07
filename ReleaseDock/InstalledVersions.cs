using System.Diagnostics;
using System.IO;
using ReleaseDock.Json;
using System.Text.RegularExpressions;

namespace ReleaseDock;

public sealed record InstalledVersion(string Version, string Source);

public static class InstalledVersions
{
    public static InstalledVersion ReadForApp(TrackedApp app, string location)
    {
        if (app.AutoReconcile && app.VersionRecord != null && string.Equals(Path.GetFullPath(location), Path.GetFullPath(app.InstallLocation), StringComparison.OrdinalIgnoreCase))
        {
            Reconcile(app);
            if (app.LocalError) throw new InvalidOperationException(app.LocalStatus);
            if (app.VersionRecord != null) return new(app.Installed, "GROUT installation record");
        }
        return Read(location, UsesD2RLoaderManifest(app));
    }
    public static bool UsesD2RLoaderManifest(TrackedApp app) => app.Repo.Length > 0 && Repositories.Resolve(app.Repo).Host == "d2rloader.net";
    public static InstalledVersion Read(string location, bool d2rLoader = false)
    {
        location = Path.GetFullPath(location.Trim().Trim('"'));
        if (Directory.Exists(location))
        {
            foreach (var name in d2rLoader ? new[] { "manifest.json", "release-manifest.json", "vrmod-manifest.json" } : new[] { "release-manifest.json", "vrmod-manifest.json" })
            {
                var manifest = Path.Combine(location, name);
                if (File.Exists(manifest)) return Read(manifest, d2rLoader);
            }
            var candidates = Directory.EnumerateFiles(location, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(p => !Regex.IsMatch(Path.GetFileName(p), "unins|setup|install|update|crash", RegexOptions.IgnoreCase)).ToList();
            if (candidates.Count != 1) throw new InvalidOperationException("Choose the app's EXE or a version manifest; this folder has no unambiguous version source.");
            return Read(candidates[0], d2rLoader);
        }
        if (!File.Exists(location)) throw new FileNotFoundException("Install location is missing. Last known version kept.");
        string? version = null;
        if (Path.GetExtension(location).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(location);
            if (stream.Length > 2 * 1024 * 1024) throw new InvalidOperationException("Version manifest is too large.");
            using var json = JsonDocument.Parse(stream);
            if (d2rLoader && Path.GetFileName(location).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                if (!json.RootElement.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) throw new InvalidOperationException("D2RLoader manifest.json has no top-level name with a version.");
                var markers = Regex.Matches(name.GetString() ?? "", @"(?:^|[-_])[vV]?(?=\d+\.\d)");
                if (markers.Count == 0) throw new InvalidOperationException("D2RLoader manifest name must end in a version such as -0.1.4 or -v2.3.4.");
                var marker = markers[markers.Count - 1];
                version = name.GetString()!.Substring(marker.Index + marker.Length);
                if (!Regex.IsMatch(version, @"^\d+\.\d+(?:\.\d+){0,2}(?:-[A-Za-z0-9][A-Za-z0-9.-]*)?(?:\+[A-Za-z0-9][A-Za-z0-9.-]*)?$")) throw new InvalidOperationException("D2RLoader manifest name must end in a version such as -0.1.4 or -v2.3.4.");
            }
            else foreach (var field in new[] { "release_tag", "version" })
                if (json.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) { version = value.GetString(); break; }
        }
        else if (Path.GetExtension(location).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var info = FileVersionInfo.GetVersionInfo(location);
            version = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
        }
        else throw new InvalidOperationException("Select an install folder, an EXE, or a JSON version manifest.");
        if (version == null || string.IsNullOrWhiteSpace(version) || version.Length > 160 || version.Any(char.IsControl)) throw new InvalidOperationException("No usable version found. Last known version kept.");
        return new(version.Trim(), location);
    }

    // Allow a nonnumeric project prefix, but never discard prerelease/channel suffixes.
    public static bool Matches(string installed, string latest)
    {
        if (string.Equals(installed, latest, StringComparison.Ordinal)) return true;
        static string? Numeric(string value)
        {
            var match = Regex.Match(value.Trim(), @"^(?:[A-Za-z][A-Za-z_-]*[-_])?[vV]?(\d+\.\d+(?:\.\d+){0,2})(?:\+[A-Za-z0-9.-]+)?$");
            return match.Success && Version.TryParse(match.Groups[1].Value, out var v) ? $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}.{Math.Max(0, v.Revision)}" : null;
        }
        var a = Numeric(installed); var b = Numeric(latest);
        return a != null && b != null && a == b;
    }
    public static int? CompareVersionNumbers(string installed, string latest)
    {
        static Version? Parse(string value)
        {
            var match = Regex.Match(value.Trim(), @"^(?:[A-Za-z][A-Za-z_-]*[-_])?[vV]?(\d+(?:\.\d+){0,3})(?:-[A-Za-z0-9][A-Za-z0-9.-]*)?(?:\+[A-Za-z0-9.-]+)?$");
            if (!match.Success) return null;
            var numeric = match.Groups[1].Value;
            if (!numeric.Contains('.')) numeric += ".0";
            return Version.TryParse(numeric, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)) : null;
        }
        var a = Parse(installed); var b = Parse(latest);
        return a != null && b != null ? a.CompareTo(b) : null;
    }

    public static void Reconcile(TrackedApp app)
    {
        app.InstallationMissing = false;
        var draft = AppLocationDraft.From(app);
        var location = draft.FileName.Length > 0 ? Path.Combine(draft.Folder, draft.FileName) : app.InstallLocation;
        if (location.Length > 0)
        {
            try { _ = File.GetAttributes(location); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                app.InstallationMissing = true; app.LocalError = true;
                app.LocalStatus = "The saved app location no longer exists: " + location;
                app.UpdateState = "";
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { }
        }
        if (!app.AutoReconcile) app.LocalError = false;
        if (!app.AutoReconcile) { app.LocalStatus = app.InstalledProvenance.Length > 0 ? app.InstalledProvenance : "Manual version tracking"; return; }
        if (app.VersionRecord is { } record)
        {
            if (!InstalledReleaseRecords.ValidLocation(app, record)) app.VersionRecord = null;
            else
            {
                try
                {
                    var actual = Read(app.InstallLocation, UsesD2RLoaderManifest(app));
                    if (InstalledReleaseRecords.NewSource(record, actual))
                    {
                        var ownedSource = record.Files.Any(f => string.Equals(Path.GetFullPath(f), Path.GetFullPath(actual.Source), StringComparison.OrdinalIgnoreCase));
                        var unchangedMetadata = !ownedSource || record.NativeVersion.Length > 0 && Matches(actual.Version, record.NativeVersion);
                        app.VersionRecord = record with { Version = unchangedMetadata ? record.Version : actual.Version, NativeVersion = actual.Version, IgnoredSource = actual.Source, IgnoredHash = InstalledReleaseRecords.Fingerprint(actual.Source), AppliedVersion = record.AppliedVersion.Length > 0 ? record.AppliedVersion : record.Version, Evidence = unchangedMetadata ? record.Evidence : "Detected" };
                        app.Installed = app.VersionRecord.Version;
                        app.InstalledProvenance = unchangedMetadata ? "Installed release recorded by GROUT: " + record.Version : "Version detected from changed app files: " + actual.Version;
                        app.LocalStatus = app.InstalledProvenance; app.LocalError = false; return;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                if (record.RegistrationId.Length > 0 && InstallDiscovery.ReadRegistry().FirstOrDefault(r => r.Id == record.RegistrationId) is { } registration && !string.IsNullOrWhiteSpace(registration.Version))
                {
                    if (record.NativeVersion.Length == 0) app.VersionRecord = record with { NativeVersion = registration.Version };
                    else if (!Matches(registration.Version, record.NativeVersion)) app.VersionRecord = record with { Version = registration.Version, NativeVersion = registration.Version, Evidence = "Detected" };
                    app.Installed = app.VersionRecord!.Version; app.LocalError = false;
                    app.LocalStatus = "Installed release recorded by GROUT: " + app.Installed; return;
                }
                var missing = record.Files.Where(f => !File.Exists(f)).ToList();
                if (missing.Count > 0)
                {
                    app.LocalError = true; app.InstallationMissing = missing.Count == record.Files.Length;
                    app.LocalStatus = "Recorded app files are missing: " + missing[0]; return;
                }
                app.Installed = record.Version; app.LocalError = false;
                app.LocalStatus = "Release " + record.Version + " recorded by GROUT after installation"; return;
            }
        }
        try
        {
            var found = Read(app.InstallLocation, UsesD2RLoaderManifest(app));
            app.Installed = found.Version;
            app.LocalStatus = $"Detected {found.Version} from {found.Source}";
            app.LocalError = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        {
            app.LocalStatus = ex.Message;
            app.LocalError = true;
        }
    }
    public static void RecordAppliedExe(TrackedApp app)
    {
        app.InstallLocation = app.UpdateTarget;
        app.PartialUpdate = false;
        InstalledReleaseRecords.Record(app, [app.UpdateTarget]);
        Reconcile(app);
        app.UpdateState = "";
    }
    public static void RecordAppliedZip(TrackedApp app, List<UpdateFile> plan)
    {
        var candidates = plan.Where(f => Path.GetFileName(f.Target).Equals("release-manifest.json", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f.Target).Equals("vrmod-manifest.json", StringComparison.OrdinalIgnoreCase) || UsesD2RLoaderManifest(app) && Path.GetFileName(f.Target).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || string.Equals(f.Target, app.InstallLocation, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0) candidates = plan.Where(f => Path.GetExtension(f.Target).Equals(".exe", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(Path.GetFileName(f.Target), "unins|setup|install|update|crash", RegexOptions.IgnoreCase)).ToList();
        if (candidates.Count == 1)
        {
            try
            {
                var found = Read(candidates[0].Target, UsesD2RLoaderManifest(app));
                app.AppFolder = app.UpdateTarget; app.AppFile = ""; app.VersionSourceOverride = candidates[0].Relative; app.InstallLocation = found.Source;
                InstalledReleaseRecords.Record(app, plan.Select(f => f.Target)); Reconcile(app); return;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { }
        }
        app.AppFolder = app.UpdateTarget; app.AppFile = ""; app.VersionSourceOverride = ""; app.InstallLocation = app.UpdateTarget;
        InstalledReleaseRecords.Record(app, plan.Select(f => f.Target)); Reconcile(app);
    }
    public static void RecordAppliedInstaller(TrackedApp app, string registrationId = "")
    {
        if (app.PendingInstallerRelease != app.Latest || app.Latest.Length == 0 || app.InstallLocation.Length == 0) return;
        var folder = AppLocationDraft.From(app).Folder;
        var files = File.Exists(app.InstallLocation) ? new[] { app.InstallLocation } : Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Take(10001).ToArray() : [];
        if (files.Length is > 0 and <= 10000) { InstalledReleaseRecords.Record(app, files, registrationId); Reconcile(app); }
    }
}
