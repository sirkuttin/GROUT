using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReleaseDock;

public sealed record InstalledVersion(string Version, string Source);

public static class InstalledVersions
{
    public static InstalledVersion Read(string location)
    {
        location = Path.GetFullPath(location.Trim().Trim('"'));
        if (Directory.Exists(location))
        {
            foreach (var name in new[] { "release-manifest.json", "vrmod-manifest.json" })
            {
                var manifest = Path.Combine(location, name);
                if (File.Exists(manifest)) return Read(manifest);
            }
            var candidates = Directory.EnumerateFiles(location, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(p => !Regex.IsMatch(Path.GetFileName(p), "unins|setup|install|update|crash", RegexOptions.IgnoreCase)).ToList();
            if (candidates.Count != 1) throw new InvalidOperationException("Choose the app's EXE or a version manifest; this folder has no unambiguous version source.");
            return Read(candidates[0]);
        }
        if (!File.Exists(location)) throw new FileNotFoundException("Install location is missing. Last known version kept.");
        string? version = null;
        if (Path.GetExtension(location).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(location);
            if (stream.Length > 2 * 1024 * 1024) throw new InvalidOperationException("Version manifest is too large.");
            using var json = JsonDocument.Parse(stream);
            foreach (var field in new[] { "release_tag", "version" })
                if (json.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) { version = value.GetString(); break; }
        }
        else if (Path.GetExtension(location).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var info = FileVersionInfo.GetVersionInfo(location);
            version = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
        }
        else throw new InvalidOperationException("Select an install folder, an EXE, or a JSON version manifest.");
        if (string.IsNullOrWhiteSpace(version) || version.Length > 160 || version.Any(char.IsControl)) throw new InvalidOperationException("No usable version found. Last known version kept.");
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
        try
        {
            var found = Read(app.InstallLocation);
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
        app.Installed = app.Latest;
        app.PartialUpdate = false;
        app.InstalledProvenance = $"Saved from {Repositories.Resolve(app.Repo.Length > 0 ? app.Repo : "local/app").Name} release {app.Latest}. Future updates track the release saved by GROUT.";
        app.AutoReconcile = false;
        app.LocalError = false;
        try
        {
            var detected = Read(app.UpdateTarget);
            if (Matches(detected.Version, app.Latest)) { app.AutoReconcile = true; app.Installed = detected.Version; }
            else app.InstalledProvenance += $" The EXE reports {detected.Version}; automatic metadata comparison is paused.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { }
        Reconcile(app);
        app.UpdateState = "";
    }
}
