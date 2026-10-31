using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReleaseDock;

public sealed class UpdateFile
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string Relative { get; set; } = "";
    public bool Apply { get; set; } = true;
    public bool Existed { get; set; }
    public bool SettingsOrData { get; set; }
    public string Action => Existed ? "Replace" : "Add";
}
public sealed class Recovery
{
    public string Root { get; set; } = "";
    public string State { get; set; } = "Prepared";
    public List<UpdateFile> Files { get; set; } = [];
    public InstallationSnapshot? PreviousInstallation { get; set; }
}

public sealed record InstallationSnapshot(string Installed, bool AutoReconcile, string Provenance, string Location, string Folder, string File, string SourceOverride, bool Partial = false)
{
    public static InstallationSnapshot Capture(TrackedApp app) => new(app.Installed, app.AutoReconcile, app.InstalledProvenance, app.InstallLocation, app.AppFolder, app.AppFile, app.VersionSourceOverride, app.PartialUpdate);
    public void Apply(TrackedApp app)
    {
        app.Installed = Installed; app.AutoReconcile = AutoReconcile; app.InstalledProvenance = Provenance; app.InstallLocation = Location;
        app.AppFolder = Folder; app.AppFile = File; app.VersionSourceOverride = SourceOverride;
        app.PartialUpdate = Partial;
    }
}

public static class FileUpdates
{
    public static void NoLinks(string path)
    {
        for (var item = new DirectoryInfo(Path.GetFullPath(path)); item != null; item = item.Parent)
        {
            try
            {
                if ((File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0) throw new IOException("Update paths cannot contain symbolic links or junctions: " + item.FullName);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    public static string SafeRelative(string name)
    {
        name = name.Replace('\\', '/');
        var parts = name.Split('/');
        if (parts.Any(p => string.IsNullOrEmpty(p) || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)))
            throw new IOException("Unsafe archive path: " + name);
        return string.Join(Path.DirectorySeparatorChar, parts);
    }
    public static string StageZip(string archive, string? stagingDirectory = null, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 10000 || zip.Entries.Sum(e => e.Length) > 8L * 1024 * 1024 * 1024) throw new IOException("Archive exceeds the 10,000-entry / 8 GB extraction limit.");
        var files = new List<(ZipArchiveEntry Entry, string Path)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) throw new IOException("Archive links are not supported.");
            var name = entry.FullName.TrimEnd('/', '\\');
            var relative = SafeRelative(name);
            if (!names.Add(relative)) throw new IOException("Archive contains duplicate paths: " + name);
            if (!entry.FullName.EndsWith('/') && !entry.FullName.EndsWith('\\')) files.Add((entry, relative));
        }
        if (files.Count == 0) throw new IOException("Archive has no files.");
        // Reject file/directory collisions before extracting anything.
        var fileNames = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
            for (var parent = Path.GetDirectoryName(file.Path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                if (fileNames.Contains(parent)) throw new IOException("Archive has conflicting file and directory names.");
        var staging = stagingDirectory ?? AppPaths.NewWork("staging");
        Directory.CreateDirectory(staging);
        if (Directory.EnumerateFileSystemEntries(staging).Any()) throw new IOException("Staging directory must be empty.");
        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            var path = Path.Combine(staging, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = file.Entry.Open();
            using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920]; int read;
            while ((read = source.Read(buffer)) > 0) { cancellation.ThrowIfCancellationRequested(); target.Write(buffer, 0, read); }
        }
        return staging;
    }
    public static List<UpdateFile> Plan(string sourceRoot, string destination, bool removeOuterFolder)
    {
        destination = Path.GetFullPath(destination);
        NoLinks(destination);
        var files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
        if (removeOuterFolder)
        {
            var roots = files.Select(f => Path.GetRelativePath(sourceRoot, f).Split(Path.DirectorySeparatorChar)[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (roots.Count != 1 || !Directory.Exists(Path.Combine(sourceRoot, roots[0]))) throw new IOException("The archive does not have one outer folder to remove.");
            sourceRoot = Path.Combine(sourceRoot, roots[0]);
        }
        return files.Select(f =>
        {
            var relative = SafeRelative(Path.GetRelativePath(sourceRoot, f));
            var target = Path.Combine(destination, relative);
            NoLinks(target);
            var exists = File.Exists(target);
            var protectedFile = Regex.IsMatch(relative, @"(^|[\\/])(saves?|userdata|config|settings)([\\/]|\.)|\.(ini|cfg|config|sav|db|sqlite|yaml|yml|toml)$", RegexOptions.IgnoreCase)
                || (relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(relative) is not ("release-manifest.json" or "vrmod-manifest.json" or "package.json"));
            return new UpdateFile { Source = f, Target = target, Relative = relative, Existed = exists, Apply = !exists || !protectedFile, SettingsOrData = protectedFile };
        }).ToList();
    }
    public static string Apply(List<UpdateFile> plan, string root, string? backupDirectory = null, InstallationSnapshot? previousInstallation = null)
    {
        var selected = plan.Where(f => f.Apply).ToList();
        if (selected.Count == 0) throw new IOException("No files selected.");
        root = Path.GetFullPath(root);
        if (selected.Select(f => Path.GetFullPath(f.Target)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Count) throw new IOException("Duplicate update destinations.");
        foreach (var file in selected)
        {
            NoLinks(file.Source);
            var path = Path.GetFullPath(file.Target);
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Update path escaped the selected folder.");
            if (!path.Equals(Path.GetFullPath(Path.Combine(root, SafeRelative(file.Relative))), StringComparison.OrdinalIgnoreCase)) throw new IOException("Update mapping is inconsistent.");
            NoLinks(path);
            if (File.Exists(path)) { using var check = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        }
        var backup = backupDirectory ?? AppPaths.NewWork("backups");
        Directory.CreateDirectory(backup);
        if (Directory.EnumerateFileSystemEntries(backup).Any()) throw new IOException("Backup directory must be empty.");
        var journal = new Recovery { Root = root, Files = selected, PreviousInstallation = previousInstallation };
        foreach (var file in selected)
        {
            file.Existed = File.Exists(file.Target);
            if (file.Existed)
            {
                var path = Path.Combine(backup, "files", SafeRelative(file.Relative));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(file.Target, path);
            }
        }
        void Save() => File.WriteAllText(Path.Combine(backup, "recovery.json"), JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
        Save();
        var applied = new List<UpdateFile>();
        try
        {
            journal.State = "Applying"; Save();
            foreach (var file in selected)
            {
                NoLinks(file.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                AtomicCopy(file.Source, file.Target);
                applied.Add(file);
            }
            journal.State = "Applied"; Save();
            return backup;
        }
        catch (Exception ex)
        {
            try { RestoreFiles(backup, applied); journal.State = "Rolled back"; Save(); }
            catch (Exception recovery) { throw new IOException($"Update failed: {ex.Message}\nRecovery also needs attention: {recovery.Message}\nBackup: {backup}", ex); }
            throw new IOException($"Update failed; changed files were restored. Close the app and retry.\n{ex.Message}\nBackup: {backup}", ex);
        }
    }
    private static void AtomicCopy(string source, string target)
    {
        var temp = target + ".releasedock-" + Guid.NewGuid().ToString("N");
        try { File.Copy(source, temp, false); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void RestoreFiles(string backup, IEnumerable<UpdateFile> files)
    {
        foreach (var file in files.Reverse())
        {
            NoLinks(file.Target);
            if (file.Existed) AtomicCopy(Path.Combine(backup, "files", SafeRelative(file.Relative)), file.Target);
            else if (File.Exists(file.Target)) File.Delete(file.Target);
        }
    }
    public static InstallationSnapshot? Restore(string backup)
    {
        var journal = JsonSerializer.Deserialize<Recovery>(File.ReadAllText(Path.Combine(backup, "recovery.json"))) ?? throw new IOException("Invalid recovery record.");
        if (journal.State is "Restored" or "Rolled back") throw new IOException("This backup was already restored.");
        foreach (var file in journal.Files)
        {
            if (!Path.GetFullPath(file.Target).Equals(Path.GetFullPath(Path.Combine(journal.Root, SafeRelative(file.Relative))), StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid recovery target.");
            NoLinks(file.Target);
            if (file.Existed && !File.Exists(Path.Combine(backup, "files", SafeRelative(file.Relative)))) throw new IOException("A recovery file is missing. No changes were made.");
            if (File.Exists(file.Target)) { using var check = new FileStream(file.Target, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        }
        RestoreFiles(backup, journal.Files);
        journal.State = "Restored";
        File.WriteAllText(Path.Combine(backup, "recovery.json"), JsonSerializer.Serialize(journal));
        return journal.PreviousInstallation;
    }
}
