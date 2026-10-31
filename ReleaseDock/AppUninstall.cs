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
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The app folder is a link. Remove it manually from Explorer.");
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
            args = Regex.Replace(args, @"(?i)(^|\s)/i(?=\s|\{)", "$1/X");
        }
        else if (!Path.IsPathFullyQualified(exe) || !File.Exists(exe)) throw new InvalidOperationException("The registered uninstaller could not be found. Use Windows Installed apps.");
        return new ProcessStartInfo(exe, args) { UseShellExecute = true };
    }
    public static InstalledRegistration? Registered(TrackedApp app, IEnumerable<InstalledRegistration> entries)
    {
        var folder = AppLocationDraft.From(app).Folder.TrimEnd('\\', '/');
        if (!Path.IsPathFullyQualified(folder)) return null;
        var matches = entries.Where(e => e.Uninstall.Length > 0 &&
            (string.Equals(Environment.ExpandEnvironmentVariables(e.Location.Trim().Trim('"')).TrimEnd('\\', '/'), folder, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Path.GetDirectoryName(InstallDiscovery.ExePath(e.Icon))?.TrimEnd('\\', '/'), folder, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(e => e.Uninstall, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
