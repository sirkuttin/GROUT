using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ReleaseDock;

public enum UpdateOutcome { Downloaded, Deferred, FilesApplied, InstallerReturned }

public sealed class UpdatePrompts
{
    public Func<string, Task> ExecuteInstaller { get; init; } = async path => { using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); if (process != null) await process.WaitForExitAsync(); };
    public Func<Window, bool> LeaveSetup { get; init; } = owner => MessageBox.Show(owner, "Leave setup? Downloads and any installation already performed stay on disk, but this app will not be added to your library.", "Leave Add App", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
    // Kept as a regression-test hook: applying files must never invoke a review.
    public Func<Window, List<UpdateFile>, string?, string, bool> ReviewFiles { get; init; } = (_, _, _, _) => true;
    public Func<Window, Asset, bool> RunInstaller { get; init; } = (owner, asset) => MessageBox.Show(owner, $"Run this installer?\n\n{asset.Name}\n\nThe installer controls its destination. GROUT will check the configured version source afterward.", "Run installer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}

public static class UpdateRunner
{
    public static async Task<UpdateOutcome> Run(Window owner, TrackedApp app, Asset asset, ReleaseClient github, Action<string, double> status, Action persist, CancellationToken cancel = default, bool newInstall = false, UpdatePrompts? prompts = null, Func<string, string>? newWork = null, InstallDiscovery? discovery = null)
    {
        var allocate = newWork ?? AppPaths.NewWork;
        var allocated = new List<(string Category, string Folder)>();
        UpdateOutcome? outcome = null;
        try
        {
            outcome = await RunCore(owner, app, asset, github, status, persist, cancel, newInstall, prompts, category => { var folder = allocate(category); allocated.Add((category, folder)); return folder; }, discovery);
            return outcome.Value;
        }
        finally
        {
            foreach (var item in allocated)
                if (item.Category == "staging" || item.Category == "backups" && StorageCleanup.SafeBackup(item.Folder) || item.Category == "downloads" && (outcome == UpdateOutcome.FilesApplied || outcome == UpdateOutcome.InstallerReturned && app.IsCurrent))
                    StorageCleanup.DeleteFolder(item.Folder, Framework.GetDirectoryName(item.Folder)!);
            if (!File.Exists(app.DownloadedPath)) { app.DownloadedPath = ""; app.DownloadedTag = ""; }
            if (!Directory.Exists(app.LastBackup)) app.LastBackup = "";
            persist();
        }
    }
    private static async Task<UpdateOutcome> RunCore(Window owner, TrackedApp app, Asset asset, ReleaseClient github, Action<string, double> status, Action persist, CancellationToken cancel, bool newInstall, UpdatePrompts? prompts, Func<string, string>? newWork, InstallDiscovery? discovery)
    {
        prompts ??= new(); newWork ??= AppPaths.NewWork;
        ReleaseFiles.Validate(app, asset);
        var reusable = asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true && app.DownloadedTag == app.Latest && Path.GetFileName(app.DownloadedPath) == asset.Name && File.Exists(app.DownloadedPath) && new FileInfo(app.DownloadedPath).Length == asset.Size;
        if (reusable && asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var cached = File.OpenRead(app.DownloadedPath);
            var hash = Framework.Hex(await Framework.HashAsync(cached, cancel));
            reusable = hash.Equals(asset.Digest[7..], StringComparison.OrdinalIgnoreCase);
        }
        var path = reusable ? app.DownloadedPath : Path.Combine(newWork("downloads"), Path.GetFileName(asset.Name));
        if (!reusable)
        {
            status("Downloading " + asset.Name + "…", 0);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            cancellation.CancelAfter(TimeSpan.FromMinutes(30));
            await github.Download(asset, path, new Progress<double>(p => status("Downloading " + asset.Name + "…", p)), cancellation.Token);
        }
        app.DownloadedPath = path; app.DownloadedTag = app.Latest;
        app.UpdateState = "Downloaded — installation pending"; persist();
        status(app.UpdateState, 100);
        cancel.ThrowIfCancellationRequested();
        if (app.UpdateMethod == "Manual")
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return UpdateOutcome.Downloaded;
        }
        if (app.UpdateMethod == "Installer")
        {
            if (!prompts.RunInstaller(owner, asset)) return UpdateOutcome.Deferred;
            discovery ??= new(); var before = await Task.Run(discovery.Snapshot); app.InstallationBefore = before; app.InstallationLocated = false;
            app.UpdateState = "Installer started — installation not yet verified"; persist();
            status(app.UpdateState, 100);
            await prompts.ExecuteInstaller(path);
            status("Looking for the installed app…", 100);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                app.InstallCandidates = await Task.Run(() => discovery.Find(app, before));
                var match = InstallDiscovery.ClearMatch(app.InstallCandidates);
                if (match != null && (newInstall || app.InstallLocation.Length == 0 || string.Equals(AppLocationDraft.From(app).Folder.TrimEnd('\\'), match.Folder, StringComparison.OrdinalIgnoreCase)))
                {
                    InstallDiscovery.Apply(app, match);
                    if (InstallDiscovery.CanFinish(app)) break;
                    app.InstallDiscoveryNote = $"Found {match.Entry.Name}, but it still reports {app.Installed}. The expected release is {app.Latest}. Confirm the installation or try finding it again.";
                }
                else app.InstallDiscoveryNote = app.InstallCandidates.Count > 0 ? "Possible installations found. Choose the correct entry or keep the configured location." : "No matching install location found yet. The installer may still be finishing; try Find installation again.";
                if (attempt < 3) await Task.Delay(750);
            }
        }
        else if (app.UpdateMethod == "ReplaceExe")
        {
            ValidatePortableExe(path);
            var target = Path.GetFullPath(app.UpdateTarget);
            var plan = new List<UpdateFile> { new() { Source = path, Target = target, Relative = Path.GetFileName(target), Existed = File.Exists(target) } };
            status("Replacing app file…", 100);
            app.LastBackup = newWork("backups"); persist();
            var previous = InstallationSnapshot.Capture(app);
            await Task.Run(() => FileUpdates.Apply(plan, Framework.GetDirectoryName(target)!, app.LastBackup, previous));
            InstalledVersions.RecordAppliedExe(app); persist(); status(app.UpdateState, 100);
            return UpdateOutcome.FilesApplied;
        }
        else if (app.UpdateMethod == "Zip")
        {
            status("Inspecting ZIP contents…", 100);
            var staging = await Task.Run(() => FileUpdates.StageZip(path, newWork("staging"), cancel));
            cancel.ThrowIfCancellationRequested();
            var plan = FileUpdates.Plan(staging, app.UpdateTarget, false);
            foreach (var file in plan) file.Apply = true;
            status("Extracting ZIP…", 100);
            app.LastBackup = newWork("backups"); persist();
            var previous = InstallationSnapshot.Capture(app);
            await Task.Run(() => FileUpdates.Apply(plan, app.UpdateTarget, app.LastBackup, previous));
            app.PartialUpdate = false;
            app.UpdateState = "Files applied — installation not yet verified"; persist();
        }
        InstalledVersions.Reconcile(app);
        app.UpdateState = app.PartialUpdate ? "Some app files were not applied. Version source reports " + (app.Installed.Length > 0 ? app.Installed : "unknown") + ". Review the files or confirm the result manually." : app.AutoReconcile && !app.LocalError && app.IsCurrent ? "" : "Installation not verified — recheck the version or mark it manually";
        if (app.UpdateMethod == "Installer" && app.InstallDiscoveryNote.Length > 0) app.UpdateState += "\n" + app.InstallDiscoveryNote;
        persist(); status(app.UpdateState, 100);
        return app.UpdateMethod == "Installer" ? UpdateOutcome.InstallerReturned : UpdateOutcome.FilesApplied;
    }
    public static void ValidatePortableExe(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d) throw new BadImageFormatException();
            stream.Position = 0x3c; var header = reader.ReadUInt32();
            if (header > stream.Length - 24) throw new BadImageFormatException();
            stream.Position = header;
            if (reader.ReadUInt32() != 0x4550) throw new BadImageFormatException();
            reader.ReadUInt16(); var sections = reader.ReadUInt16();
            stream.Position = header + 20; var optionalSize = reader.ReadUInt16(); var flags = reader.ReadUInt16();
            if ((flags & 2) == 0 || (flags & 0x2000) != 0 || sections == 0 || sections > 96 || optionalSize < 64 || (long)header + 24 + optionalSize + sections * 40 > stream.Length) throw new BadImageFormatException();
            var magic = reader.ReadUInt16(); if (magic is not (0x10b or 0x20b)) throw new BadImageFormatException();
            stream.Position = header + 24 + 16; var entry = reader.ReadUInt32();
            var entryMapped = false;
            for (var i = 0; i < sections; i++)
            {
                stream.Position = header + 24 + optionalSize + i * 40 + 12;
                var address = reader.ReadUInt32(); var size = reader.ReadUInt32(); var pointer = reader.ReadUInt32();
                if ((long)pointer + size > stream.Length) throw new BadImageFormatException();
                var offset = (long)entry - address;
                if (offset >= 0 && offset < size) entryMapped = true;
            }
            if (!entryMapped) throw new BadImageFormatException();
        }
        catch (BadImageFormatException ex)
        {
            throw new IOException("The downloaded file is not a complete Windows executable. The installed app has not been replaced.", ex);
        }
    }
}
