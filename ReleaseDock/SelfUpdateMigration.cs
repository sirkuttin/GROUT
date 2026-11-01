using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
namespace ReleaseDock;
public static class SelfUpdateMigration
{
    public static string Destination(string current)
        => Regex.IsMatch(Path.GetFileName(current), @"^GROUT-\d+\.\d+\.\d+-win-x64\.exe$", RegexOptions.IgnoreCase) ? Path.Combine(Framework.GetDirectoryName(current)!, "GROUT.exe") : current;
    public static void CheckDestination(string current, string destination)
    {
        FileUpdates.NoLinks(destination);
        if (!string.Equals(current, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination)) throw new IOException("A GROUT.exe already exists in this folder. Close GROUT and choose which copy to keep before updating; neither file has been changed.");
    }
    public static void Install(string package, string current, string destination, string hash, Action launch)
    {
        CheckDestination(current, destination);
        if (string.Equals(current, destination, StringComparison.OrdinalIgnoreCase)) { SelfUpdateHandoff.Replace(package, current, hash, launch); return; }
        SelfUpdateHandoff.Verify(package, hash);
        var next = destination + ".grout-next-" + Guid.NewGuid().ToString("N"); var installed = false;
        try { File.Copy(package, next); File.Move(next, destination); installed = true; launch(); }
        catch { if (installed) File.Delete(destination); throw; }
        finally { if (File.Exists(next)) File.Delete(next); }
    }
    public static void UpdateShortcuts(string oldPath, string newPath, IEnumerable<string> roots)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcut support is unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
            foreach (var path in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            {
                dynamic? shortcut = null;
                try
                {
                    shortcut = shell.CreateShortcut(path);
                    if (!string.Equals((string)shortcut.TargetPath, oldPath, StringComparison.OrdinalIgnoreCase)) continue;
                    shortcut.TargetPath = newPath;
                    var icon = (string)shortcut.IconLocation;
                    if (icon.Equals(oldPath, StringComparison.OrdinalIgnoreCase) || icon.StartsWith(oldPath + ",", StringComparison.OrdinalIgnoreCase)) shortcut.IconLocation = newPath + icon[oldPath.Length..];
                    shortcut.Save();
                }
                finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
    public static void UpdateReferences(SelfUpdatePlan plan)
    {
        if (plan.OriginalTarget.Length == 0) return;
        if (!string.Equals(Destination(plan.OriginalTarget), plan.Target, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid filename migration.");
        var startup = Preferences.StartupCommand();
        var repointed = RepointStartup(startup, plan.OriginalTarget, plan.Target);
        if (repointed != startup) Preferences.SetStartupCommand(repointed);
        UpdateShortcuts(plan.OriginalTarget, plan.Target, [Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned")]);
    }
    public static string? RepointStartup(string? command, string oldPath, string newPath)
    {
        if (command == null || !string.Equals(InstallDiscovery.ExePath(command), oldPath, StringComparison.OrdinalIgnoreCase)) return command;
        var match = Regex.Match(command.Trim(), "^\"[^\"]+\\.exe\"(.*)$|^.+?\\.exe(\\s.*|$)", RegexOptions.IgnoreCase);
        if (!match.Success) return command;
        return "\"" + newPath + "\"" + (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
    }
    public static void RemoveOriginal(SelfUpdatePlan plan)
    {
        if (plan.OriginalTarget.Length == 0 || !string.Equals(Destination(plan.OriginalTarget), plan.Target, StringComparison.OrdinalIgnoreCase) || string.Equals(plan.Target, plan.OriginalTarget, StringComparison.OrdinalIgnoreCase) || !File.Exists(plan.OriginalTarget)) return;
        FileUpdates.NoLinks(plan.OriginalTarget);
        string hash; using (var file = File.OpenRead(plan.OriginalTarget)) hash = Framework.Hex(Framework.Hash(file));
        if (hash.Equals(plan.OriginalHash, StringComparison.OrdinalIgnoreCase)) File.Delete(plan.OriginalTarget);
    }
}
