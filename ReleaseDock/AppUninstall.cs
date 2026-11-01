using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
namespace ReleaseDock;
public static class AppUninstall
{
    public static string PortableFolder(string folder)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new InvalidOperationException("Choose the app folder in App settings first.");
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
        else if (!Path.IsPathFullyQualified(exe) || !File.Exists(exe)) throw new InvalidOperationException("The registered uninstaller could not be found. Use Windows Installed apps.");
        return new ProcessStartInfo(exe, args) { UseShellExecute = true };
    }
    public static InstalledRegistration? Registered(TrackedApp app, IEnumerable<InstalledRegistration> entries)
    {
        var draft = AppLocationDraft.From(app);
        var folder = draft.Folder.TrimEnd('\\', '/');
        if (!Path.IsPathFullyQualified(folder)) return null;
        static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", "");
        static bool SamePath(string a, string b)
        {
            try { return Path.IsPathFullyQualified(a) && Path.IsPathFullyQualified(b) && Path.GetFullPath(a).TrimEnd('\\', '/').Equals(Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return false; }
        }
        var knownExe = draft.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.Combine(folder, draft.FileName) : "";
        var identities = new[] { Normalize(app.Repo.Length > 0 ? Repositories.Resolve(app.Repo).Path.Split('/').Last() : ""), Normalize(app.AppName) }.Where(n => n.Length >= 3).ToList();
        var matches = entries.Where(e => e.Uninstall.Length > 0 &&
            (SamePath(Environment.ExpandEnvironmentVariables(e.Location.Trim().Trim('"')), folder) || SamePath(Path.GetDirectoryName(InstallDiscovery.ExePath(e.Icon)) ?? "", folder)) &&
            (knownExe.Length > 0 ? SamePath(InstallDiscovery.ExePath(e.Icon), knownExe) : identities.Contains(Normalize(e.Name)) || identities.Contains(Normalize(Path.GetFileNameWithoutExtension(InstallDiscovery.ExePath(e.Icon))))))
            .GroupBy(e => e.Uninstall, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
