using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
namespace ReleaseDock;
public static class AppUninstall
{
    public static string ZipFolder(TrackedApp app)
    {
        var folder = AppLocationDraft.From(app).Folder;
        if (InstalledVersions.UsesD2RLoaderManifest(app) && app.D2RManagedFolder.Length > 0)
            folder = Path.Combine(folder, FileUpdates.SafeRelative(app.D2RManagedFolder));
        return PortableFolder(folder);
    }
    public static bool RegistrationGone(string id)
    {
        try
        {
            var parts = id.Split('/', 3);
            if (parts.Length != 3 || !Enum.TryParse<Microsoft.Win32.RegistryHive>(parts[0], out var hive) || !Enum.TryParse<Microsoft.Win32.RegistryView>(parts[1], out var view)) return false;
            using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + parts[2]);
            return key == null;
        }
        catch { return false; }
    }
    public static void Begin(TrackedApp app, string target, string registrationId = "")
    {
        app.UninstallPending = true; app.UninstallTarget = target; app.UninstallRegistrationId = registrationId;
        app.UpdateState = "Waiting for uninstallation to finish";
    }
    public static void KeepTracking(TrackedApp app)
    {
        app.UninstallPending = false; app.UninstallTarget = ""; app.UninstallRegistrationId = "";
    }
    public static bool CanConfirm(TrackedApp app, IEnumerable<InstalledRegistration> registrations)
    {
        if (!app.UninstallPending || !Framework.IsPathFullyQualified(app.UninstallTarget)) return false;
        if (app.UninstallRegistrationId.Length > 0 && registrations.Any(r => r.Id == app.UninstallRegistrationId)) return false;
        // Installers can deliberately leave an empty/data folder behind.
        // When no exact EXE is known, removal of the corroborated registry entry
        // is the completion signal, rather than deleting that leftover folder.
        if (app.UninstallRegistrationId.Length > 0 && !app.UninstallTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return true;
        try { _ = File.GetAttributes(app.UninstallTarget); return false; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { return false; }
    }
    public static bool Complete(IList<TrackedApp> library, TrackedApp app, IEnumerable<InstalledRegistration> registrations, Action persist)
    {
        var index = library.IndexOf(app);
        if (index < 0 || !CanConfirm(app, registrations)) return false;
        library.RemoveAt(index);
        try { persist(); }
        catch { library.Insert(index, app); throw; }
        return true;
    }
    public static (string Title, string Message) TrackingPrompt(TrackedApp app)
    {
        if (app.UninstallPending) return ("Stop tracking", $"Stop tracking {app.AppName}?\n\nThis removes its library entry but does not stop a running Windows uninstaller.");
        if (app.InstallationMissing) return ("Remove missing app", $"Remove {app.AppName} from GROUT?\n\nIts saved install location was not found. This only removes the library entry.");
        return ("Stop tracking", $"Stop tracking {app.AppName}?\n\nThis removes it from GROUT's library without changing app files.");
    }
    public static string PortableFolder(string folder)
    {
        if (!Framework.IsPathFullyQualified(folder)) throw new InvalidOperationException("Choose the app folder in App settings first.");
        var path = Path.GetFullPath(folder).TrimEnd('\\', '/');
        var protectedPaths = new[] { Path.GetPathRoot(path), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) };
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (protectedPaths.Any(p => !string.IsNullOrEmpty(p) && string.Equals(path, p.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) || path.Equals(windows, StringComparison.OrdinalIgnoreCase) || path.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase) || new[] { "apps", "programs", "downloads", "documents", "desktop" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("This is a system or shared folder. Select the specific app's folder before uninstalling.");
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("App folder was not found.");
        FileUpdates.NoLinks(path);
        return path;
    }
    public static ProcessStartInfo Command(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        var match = Regex.Match(command, "^\"([^\"]+\\.exe)\"(.*)$|^(.+?\\.exe)(\\s.*|$)", RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidOperationException("Windows did not provide a supported executable uninstaller. Use Windows Installed apps.");
        var exe = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value;
        var args = (match.Groups[1].Success ? match.Groups[2].Value : match.Groups[4].Value).Trim();
        if (Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            exe = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
            args = Regex.Replace(args, @"(?i)(^|\s)/i(?=\s|\{|"")", "$1/X");
        }
        else if (!Framework.IsPathFullyQualified(exe) || !File.Exists(exe)) throw new InvalidOperationException("The registered uninstaller could not be found. Use Windows Installed apps.");
        return new ProcessStartInfo(exe, args) { UseShellExecute = true };
    }
    public static InstalledRegistration? Registered(TrackedApp app, IEnumerable<InstalledRegistration> entries)
    {
        var draft = AppLocationDraft.From(app);
        var folder = draft.Folder.TrimEnd('\\', '/');
        if (!Framework.IsPathFullyQualified(folder)) return null;
        static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", "");
        static bool SamePath(string a, string b)
        {
            try { return Framework.IsPathFullyQualified(a) && Framework.IsPathFullyQualified(b) && Path.GetFullPath(a).TrimEnd('\\', '/').Equals(Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return false; }
        }
        var knownExe = draft.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.Combine(folder, draft.FileName) : "";
        var identities = new[] { Normalize(app.Repo.Length > 0 ? Repositories.Resolve(app.Repo).Path.Split('/').Last() : ""), Normalize(app.AppName) }.Where(n => n.Length >= 3).ToList();
        var matches = entries.Where(e => e.Uninstall.Length > 0 &&
            (SamePath(Environment.ExpandEnvironmentVariables(e.Location.Trim().Trim('"')), folder) || SamePath(Framework.GetDirectoryName(InstallDiscovery.ExePath(e.Icon)) ?? "", folder)) &&
            (knownExe.Length > 0 ? SamePath(InstallDiscovery.ExePath(e.Icon), knownExe) : identities.Contains(Normalize(e.Name)) || identities.Contains(Normalize(Path.GetFileNameWithoutExtension(InstallDiscovery.ExePath(e.Icon))))))
            .GroupBy(e => e.Uninstall, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
