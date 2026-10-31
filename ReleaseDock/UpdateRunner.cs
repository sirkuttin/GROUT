using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ReleaseDock;

public enum UpdateOutcome { Downloaded, Deferred, FilesApplied, InstallerReturned }

public sealed class UpdatePrompts
{
    public Func<string, Task> ExecuteInstaller { get; init; } = async path => { using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); if (process != null) await process.WaitForExitAsync(); };
    public Func<Window, bool> LeaveSetup { get; init; } = owner => MessageBox.Show(owner, "Leave setup? Downloads and any installation already performed stay on disk, but this app will not be added to your library.", "Leave Add App", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
    public Func<Window, List<UpdateFile>, string?, string, bool> ReviewFiles { get; init; } = UpdateRunner.Review;
    public Func<Window, Asset, bool> RunInstaller { get; init; } = (owner, asset) => MessageBox.Show(owner, $"Run this installer?\n\n{asset.Name}\n\nThe installer controls its destination. GROUT will check the configured version source afterward.", "Run installer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}

public static class UpdateRunner
{
    public static async Task<UpdateOutcome> Run(Window owner, TrackedApp app, Asset asset, ReleaseClient github, Action<string, double> status, Action persist, CancellationToken cancel = default, bool newInstall = false, UpdatePrompts? prompts = null, Func<string, string>? newWork = null, InstallDiscovery? discovery = null)
    {
        prompts ??= new(); newWork ??= AppPaths.NewWork;
        ReleaseFiles.Validate(app, asset);
        var reusable = asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true && app.DownloadedTag == app.Latest && Path.GetFileName(app.DownloadedPath) == asset.Name && File.Exists(app.DownloadedPath) && new FileInfo(app.DownloadedPath).Length == asset.Size;
        if (reusable && asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
        {
            await using var cached = File.OpenRead(app.DownloadedPath);
            var hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(cached, cancel));
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
            var target = Path.GetFullPath(app.UpdateTarget);
            var plan = new List<UpdateFile> { new() { Source = path, Target = target, Relative = Path.GetFileName(target), Existed = File.Exists(target) } };
            if ((!newInstall || File.Exists(target)) && !prompts.ReviewFiles(owner, plan, null, app.UpdateTarget)) return UpdateOutcome.Deferred;
            status("Replacing app file…", 100);
            app.LastBackup = newWork("backups"); persist();
            var previous = InstallationSnapshot.Capture(app);
            await Task.Run(() => FileUpdates.Apply(plan, Path.GetDirectoryName(target)!, app.LastBackup, previous));
            InstalledVersions.RecordAppliedExe(app); persist(); status(app.UpdateState, 100);
            return UpdateOutcome.FilesApplied;
        }
        else if (app.UpdateMethod == "Zip")
        {
            status("Inspecting ZIP contents…", 100);
            var staging = await Task.Run(() => FileUpdates.StageZip(path, newWork("staging"), cancel));
            cancel.ThrowIfCancellationRequested();
            var plan = FileUpdates.Plan(staging, app.UpdateTarget, false);
            if (!prompts.ReviewFiles(owner, plan, staging, app.UpdateTarget)) return UpdateOutcome.Deferred;
            status("Applying selected files…", 100);
            app.LastBackup = newWork("backups"); persist();
            var previous = InstallationSnapshot.Capture(app);
            await Task.Run(() => FileUpdates.Apply(plan, app.UpdateTarget, app.LastBackup, previous));
            app.PartialUpdate = plan.Any(f => !f.Apply && !f.SettingsOrData);
            app.UpdateState = "Files applied — installation not yet verified"; persist();
        }
        InstalledVersions.Reconcile(app);
        app.UpdateState = app.PartialUpdate ? "Some app files were not applied. Version source reports " + (app.Installed.Length > 0 ? app.Installed : "unknown") + ". Review the files or confirm the result manually." : app.AutoReconcile && !app.LocalError && app.IsCurrent ? "Installed version detected" : "Installation not verified — recheck the version or mark it manually";
        if (app.UpdateMethod == "Installer" && app.InstallDiscoveryNote.Length > 0) app.UpdateState += "\n" + app.InstallDiscoveryNote;
        persist(); status(app.UpdateState, 100);
        return app.UpdateMethod == "Installer" ? UpdateOutcome.InstallerReturned : UpdateOutcome.FilesApplied;
    }
    public static bool Review(Window owner, List<UpdateFile> files, string? staging, string target)
    {
        var window = Ui.Window("Review files before applying", 930, 730); window.Owner = owner;
        var panel = Ui.Panel(window);
        panel.Children.Add(Ui.Text("Review the update", 26));
        panel.Children.Add(Ui.Text("Destination: " + target));
        panel.Children.Add(Ui.Text("Checked files will be added or replaced. Replaced files are backed up. Files absent from the download stay in place. Close the app before applying. Existing settings/save files are unchecked by default; review the list before continuing.", muted: true));
        var strip = Ui.Check("Remove the archive's single outer folder"); strip.Visibility = staging == null ? Visibility.Collapsed : Visibility.Visible;
        panel.Children.Add(strip);
        var grid = new DataGrid { ItemsSource = files, AutoGenerateColumns = false, Height = 330, CanUserAddRows = false, CanUserDeleteRows = false, Background = System.Windows.Media.Brushes.White, Foreground = System.Windows.Media.Brushes.Black };
        var textStyle = new Style(typeof(TextBlock)); textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Black)); grid.Resources.Add(typeof(TextBlock), textStyle);
        TextBlock Header(string text) => new() { Text = text, Foreground = System.Windows.Media.Brushes.Black, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4) };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = Header("Apply"), Binding = new Binding("Apply") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        grid.Columns.Add(new DataGridTextColumn { Header = Header("Path relative to destination"), Binding = new Binding("Relative"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = Header("Action"), Binding = new Binding("Action"), IsReadOnly = true });
        panel.Children.Add(grid);
        var error = Ui.Text("", muted: true); panel.Children.Add(error);
        strip.Click += (_, _) =>
        {
            try { var replacement = FileUpdates.Plan(staging!, target, strip.IsChecked == true); files.Clear(); files.AddRange(replacement); grid.ItemsSource = null; grid.ItemsSource = files; error.Text = "Review the updated destination mapping and file selections."; }
            catch (Exception ex) { strip.IsChecked = false; error.Text = ex.Message; }
        };
        var row = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        row.Children.Add(Ui.Button("Cancel", (_, _) => window.DialogResult = false));
        row.Children.Add(Ui.Button("Back up and apply selected files", (_, _) => { grid.CommitEdit(); grid.CommitEdit(DataGridEditingUnit.Row, true); if (files.Any(f => f.Apply)) window.DialogResult = true; else error.Text = "Select at least one file."; }));
        panel.Children.Add(row);
        return window.ShowDialog() == true;
    }
}
