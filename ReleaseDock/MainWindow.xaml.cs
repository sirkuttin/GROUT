using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ReleaseDock;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TrackedApp> apps = [];
    private readonly ReleaseClient github = new();
    private readonly Store store;
    private readonly string preferencesPath;
    private Preferences preferences = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private Forms.NotifyIcon? tray;
    private bool busy, exiting;
    private CancellationTokenSource? operation;
    private readonly bool preview;
    private TrackedApp? Current => Library.SelectedItem as TrackedApp;
    public MainWindow(bool preview = false, bool startHidden = false, string? dataDirectory = null)
    {
        this.preview = preview;
        dataDirectory ??= AppPaths.Data;
        store = new Store(Path.Combine(dataDirectory, "apps.json"));
        preferencesPath = Path.Combine(dataDirectory, "settings.json");
        InitializeComponent(); Library.ItemsSource = apps;
        Ui.ConstrainToScreen(this);
        if (preview)
        {
            apps.Add(new() { Repo = "example/desktop-app", Installed = "v1.0.0", Latest = "v1.1.0", AssetName = "DesktopApp-x64.exe", UpdateMethod = "Installer", Release = new("v1.1.0", "", "A faster launch and several useful improvements.", [new("DesktopApp-x64.exe", "", 0, null)]) });
            Library.SelectedIndex = 0; Render(); StatusLabel.Text = "Preview data • Your library is untouched"; return;
        }
        try { preferences = Preferences.Load(preferencesPath); foreach (var app in store.Load()) { Repositories.Parse(app.Repo); apps.Add(app); } }
        catch (Exception ex) { MessageBox.Show("Could not load data. Existing files have been left intact.\n" + ex.Message, "GROUT"); IsEnabled = false; Closed += (_, _) => Application.Current.Shutdown(); return; }
        if (preferences.RetryAfter is { } legacyPause && legacyPause > DateTimeOffset.UtcNow) preferences.SourceBackoffs["github.com"] = legacyPause;
        preferences.RetryAfter = null; github.RestoreBackoffs(preferences.SourceBackoffs);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open GROUT", null, (_, _) => Dispatcher.Invoke(ShowMain));
        menu.Items.Add("Check now", null, (_, _) => Dispatcher.InvokeAsync(async () => await CheckAll(true)));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => { ShowMain(); Settings_Click(this, new()); }));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Quit));
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "GROUT", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
        tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(ShowMain);
        Closing += (_, e) =>
        {
            if (exiting) return;
            e.Cancel = true;
            if (OwnedWindows.Count > 0) { StatusLabel.Text = "Finish or close the open dialog first."; return; }
            Hide();
            if (!preferences.TrayExplained)
            {
                preferences.TrayExplained = true;
                tray.ShowBalloonTip(5000, "GROUT is running in the tray", "Open it from the tray. Choose Exit there to stop checks.", Forms.ToolTipIcon.Info);
                SafePersist();
            }
        };
        Closed += (_, _) => { timer.Stop(); if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); tray = null; } github.Dispose(); };
        Activated += (_, _) => { if (!busy && OwnedWindows.Count == 0) ReconcileAll(); };
        timer.Tick += async (_, _) => { if (busy || OwnedWindows.Count > 0) return; ReconcileAll(); if (preferences.IsDue(DateTimeOffset.UtcNow) && apps.Count > 0) await CheckAll(false); RenderSchedule(); };
        timer.Start();
        Loaded += async (_, _) =>
        {
            if (apps.Count > 0) Library.SelectedIndex = 0;
            ReconcileAll(); Render();
            if (startHidden) Hide();
            if (preferences.IsDue(DateTimeOffset.UtcNow) && apps.Count > 0) await CheckAll(false);
        };
        Application.Current.SessionEnding += (_, _) => { exiting = true; };
        Render();
    }
    private void ShowMain() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Quit()
    {
        if (busy || OwnedWindows.Count > 0) { ShowMain(); MessageBox.Show(this, "Finish the current operation or close the open dialog before exiting.", "GROUT"); return; }
        StopWindow(); Application.Current.Shutdown();
    }
    internal void StopWindow() { exiting = true; Close(); }
    internal bool TrayVisible => tray?.Visible == true;
    private void Persist() { if (preview) return; if (!busy && OwnedWindows.Count == 0) StorageCleanup.Sweep(apps); store.Save(apps); preferences.SourceBackoffs = github.Backoffs; preferences.Save(preferencesPath); Library.Items.Refresh(); }
    private void SafePersist() { try { Persist(); } catch (Exception ex) { StatusLabel.Text = "Could not save: " + ex.Message; } }
    private void RenderSchedule()
    {
        var last = preferences.LastSuccess is { } time ? time.ToLocalTime().ToString("g") : "not yet";
        var next = preferences.NextCheck is { } due ? due <= DateTimeOffset.UtcNow ? "due" : due.ToLocalTime().ToString("g") : "manual only";
        ScheduleLabel.Text = $"Last successful check: {last}  ·  Next: {next}";
    }
    private void Render()
    {
        var app = Current;
        CountLabel.Text = $"{apps.Count} app(s) · {apps.Count(a => a.CanUpdate && ReleaseFiles.Match(a) != null)} update(s) ready";
        AddAppButton.IsEnabled = AppSettingsButton.IsEnabled = SettingsButton.IsEnabled = CheckButton.IsEnabled = !busy;
        VersionSection.IsEnabled = UninstallButton.IsEnabled = RecoveryButton.IsEnabled = RemoveButton.IsEnabled = !busy;
        Details.Visibility = app == null ? Visibility.Collapsed : Visibility.Visible;
        AppTitle.Text = app?.AppName ?? "Bring your apps aboard.";
        SourceLabel.Text = app?.ProjectLabel ?? "";
        VersionLabel.Text = app?.Versions ?? "Choose Add App to track an existing installation or install something new from a supported release host.";
        StateLabel.Text = app?.DisplayStatus ?? "";
        var missing = app?.InstallationMissing == true;
        MissingBanner.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        StateLabel.Visibility = VersionSection.Visibility = MethodLabel.Visibility = missing ? Visibility.Collapsed : Visibility.Visible;
        MissingMessage.Text = "The app may have been moved or uninstalled. Find it again or stop tracking it.\n\n" + app?.LocalStatus;
        StateLabel.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(app?.StatusColor ?? "#76DCC6"));
        VersionSection.IsEnabled = UninstallButton.IsEnabled = AppSettingsButton.IsEnabled = !busy && !missing;
        FindInstallationButton.IsEnabled = !busy;
        RemoveButton.Visibility = missing ? Visibility.Collapsed : Visibility.Visible;
        InstalledInput.Text = app?.Installed ?? "";
        LocalLabel.Text = app?.LocalStatus ?? "";
        var asset = app != null ? ReleaseFiles.Match(app) : null;
        UpdateButton.IsEnabled = !busy && app?.CanUpdate == true && asset != null;
        UpdateButton.ToolTip = busy ? "Wait for the current operation to finish." : app?.Error.Length > 0 ? "The release check failed. Check now or review App settings." : app is { AutoReconcile: true, LocalError: true } ? "Installed version could not be verified. Recheck it or enter it manually." : app?.IsOlderRelease == true ? "The available version is older than the installed version. Automatic downgrades are disabled; review the release channel in App settings." : app?.Installed.Length == 0 ? "Set the installed version or configure detection first." : app?.IsCurrent == true ? "This app is up to date." : asset == null ? "Choose a release download in App settings." : "Apply the configured update method.";
        MethodLabel.Text = app == null ? "" : ReleaseFiles.Label(app.UpdateMethod) + (asset == null ? " · Choose a release file in App settings" : app.UpdateMethod is "Zip" or "ReplaceExe" ? " → " + app.UpdateTarget : "");
        UpdateStateLabel.Text = app == null || missing ? "" : string.Join("\n", new[] { app.Error, app.UpdateState }.Where(s => s.Length > 0));
        DownloadedButton.Visibility = app != null && File.Exists(app.DownloadedPath) ? Visibility.Visible : Visibility.Collapsed;
        AppFolderButton.Visibility = !missing && app != null && AppLocationDraft.From(app).Folder.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecoveryButton.Visibility = app != null && File.Exists(Path.Combine(app.LastBackup, "recovery.json")) ? Visibility.Visible : Visibility.Collapsed;
        Notes.Text = app?.Release?.Notes ?? "Check for releases to load notes.";
        CheckedLabel.Text = app?.CheckedAt is { } time ? $"Release checked {time.ToLocalTime():g}" : "Release not checked yet";
        RenderSchedule();
    }
    private void ReconcileAll()
    {
        if (busy || preview || OwnedWindows.Count > 0) return;
        foreach (var app in apps)
        {
            InstalledVersions.Reconcile(app);
            if (!app.LocalError && app.IsCurrent && !app.PartialUpdate && app.UpdateState.Length > 0) app.UpdateState = "Installed version detected";
        }
        SafePersist();
        if (!InstalledInput.IsKeyboardFocusWithin) Render();
    }
    private async Task Fetch(TrackedApp app)
    {
        InstalledVersions.Reconcile(app);
        try { app.Release = await github.Latest(app.Repo, app.IncludePrereleases); app.Latest = app.Release.Tag; app.Error = ""; app.CheckedAt = DateTimeOffset.UtcNow; }
        catch (Exception ex) { app.Error = ex is OperationCanceledException ? "Check timed out" : ex.Message; }
    }
    private async Task CheckAll(bool manual)
    {
        if (busy || preview || OwnedWindows.Count > 0) return;
        busy = true; preferences.LastAttempt = DateTimeOffset.UtcNow;
        try
        {
            StatusLabel.Text = "Checking releases…"; Render();
            var alerts = new List<TrackedApp>();
            foreach (var app in apps.ToList())
            {
                await Fetch(app);
                if (ReleaseFiles.TakeNotification(app, preferences.Quiet)) alerts.Add(app);
                Library.Items.Refresh(); Render();
                await Task.Delay(400);
            }
            if (apps.All(a => a.Error.Length == 0)) { preferences.LastSuccess = DateTimeOffset.UtcNow; preferences.RetryAfter = null; }
            Persist();
            if (alerts.Count > 0) tray?.ShowBalloonTip(8000, $"{alerts.Count} app update(s) available", string.Join("\n", alerts.Take(4).Select(a => a.Repo + " · " + a.Latest)), Forms.ToolTipIcon.Info);
            StatusLabel.Text = $"{apps.Count(a => a.CanUpdate)} update(s) available · {apps.Count(a => a.Error.Length > 0)} check(s) failed";
        }
        catch (Exception ex) { StatusLabel.Text = ex.Message; SafePersist(); }
        finally { busy = false; Render(); }
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (busy || preview) return;
        var wizard = new SetupWindow(github, apps.Select(a => a.Repo)) { Owner = this };
        if (wizard.ShowDialog() != true) return;
        if (wizard.ExistingRepo.Length > 0) { Library.SelectedItem = apps.First(a => a.Repo.Equals(wizard.ExistingRepo, StringComparison.OrdinalIgnoreCase)); return; }
        wizard.Result.InstallationBefore = []; apps.Add(wizard.Result); SafePersist(); Library.SelectedItem = wizard.Result; Render(); StatusLabel.Text = wizard.Result.InstallDiscoveryNote.Length > 0 ? "App added. " + wizard.Result.InstallDiscoveryNote : "App added.";
    }
    private async void AppSettings_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app || preview) return;
        var wizard = new SetupWindow(github, [], app) { Owner = this };
        if (wizard.ShowDialog() != true) return;
        var index = apps.IndexOf(app); apps[index] = wizard.Result; Library.SelectedItem = wizard.Result; busy = true;
        try { await Fetch(wizard.Result); SafePersist(); } finally { busy = false; Render(); }
    }
    private async void Check_Click(object sender, RoutedEventArgs e) { if (OwnedWindows.Count == 0) await CheckAll(true); }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (busy || preview || OwnedWindows.Count > 0) return;
        try { if (SettingsWindow.Show(this, preferences)) { RenderSchedule(); StatusLabel.Text = "Settings saved."; } }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    private void Selected(object sender, SelectionChangedEventArgs e) { Render(); VersionSection.IsExpanded = Current is { } app && (app.Installed.Length == 0 || app.LocalError); }
    private async void FindInstallation_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app || preview) return;
        busy = true; Render();
        try
        {
            var candidates = await Task.Run(() => new InstallDiscovery().Find(app));
            var dialog = Ui.Window("Find installation", 700, 540); dialog.Owner = this;
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(Ui.Text("Find " + app.AppName, 24));
            panel.Children.Add(Ui.Text("Choose a Windows installation below, or browse to the app's current folder or EXE.", muted: true));
            void Choose(string folder, string filename)
            {
                if (app.UpdateMethod == "ReplaceExe" && filename.Length == 0) { MessageBox.Show(dialog, "Choose the app's EXE so GROUT knows which file to replace.", "Choose app EXE"); return; }
                app.AppFolder = folder; app.AppFile = filename; app.VersionSourceOverride = "";
                app.InstallLocation = filename.Length > 0 ? Path.Combine(folder, filename) : folder;
                if (app.UpdateMethod == "ReplaceExe") app.UpdateTarget = app.InstallLocation;
                else if (app.UpdateMethod == "Zip") app.UpdateTarget = folder;
                InstalledVersions.Reconcile(app); dialog.DialogResult = true;
            }
            foreach (var candidate in candidates)
                panel.Children.Add(Ui.Button(candidate.Label, (_, _) => Choose(candidate.Folder, candidate.AppFile)));
            if (candidates.Count == 0) panel.Children.Add(Ui.Text("No matching Windows installation found.", muted: true));
            panel.Children.Add(Ui.Button("Choose app EXE…", (_, _) => { var picker = new Microsoft.Win32.OpenFileDialog { Title = "Find the installed app", Filter = "Application|*.exe" }; if (picker.ShowDialog(dialog) == true) Choose(Path.GetDirectoryName(picker.FileName)!, Path.GetFileName(picker.FileName)); }));
            panel.Children.Add(Ui.Button("Choose app folder…", (_, _) => { var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Find the installed app folder" }; if (picker.ShowDialog(dialog) == true) Choose(picker.FolderName, ""); }));
            panel.Children.Add(Ui.Button("Cancel", (_, _) => dialog.DialogResult = false));
            dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            if (dialog.ShowDialog() == true) { SafePersist(); StatusLabel.Text = app.InstallationMissing ? app.LocalStatus : "App location updated. " + app.LocalStatus; }
        }
        catch (Exception ex) { StatusLabel.Text = "Could not find installation: " + ex.Message; }
        finally { busy = false; Render(); }
    }
    private void Recheck_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app) return;
        if (app.AutoReconcile) { ReconcileAll(); StatusLabel.Text = app.LocalStatus; return; }
        try { var found = InstalledVersions.Read(app.InstallLocation); app.Installed = found.Version; app.InstalledProvenance = "Version checked from " + found.Source; InstalledVersions.Reconcile(app); SafePersist(); Render(); StatusLabel.Text = "Version checked once. Manual tracking remains enabled."; }
        catch (Exception ex) { StatusLabel.Text = ex.Message + " Last recorded version kept."; }
    }
    private void Mark_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app || app.Latest.Length == 0) return;
        app.Installed = app.Latest; app.PartialUpdate = false; app.AutoReconcile = false; app.InstalledProvenance = "Manually marked installed"; app.LocalStatus = app.InstalledProvenance; app.UpdateState = "Marked installed manually"; SafePersist(); Render();
    }
    private void SaveVersion_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app) return;
        app.Installed = InstalledInput.Text.Trim(); app.PartialUpdate = false; app.AutoReconcile = false; app.InstalledProvenance = "Manual version tracking"; app.LocalStatus = app.InstalledProvenance; SafePersist(); Render();
    }
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { CanUpdate: true } app || ReleaseFiles.Match(app) is not { } asset || preview) return;
        busy = true; Progress.Visibility = Visibility.Visible; Render();
        operation = new(); CancelDownload.Visibility = Visibility.Visible;
        try { await UpdateRunner.Run(this, app, asset, github, (text, value) => { StatusLabel.Text = app.Repo + ": " + text; Progress.Value = value; Progress.IsIndeterminate = text.StartsWith("Inspecting") || text.StartsWith("Applying") || text.StartsWith("Replacing"); CancelDownload.IsEnabled = text.StartsWith("Downloading") || text.StartsWith("Inspecting ZIP"); }, Persist, operation.Token); }
        catch (OperationCanceledException) { app.UpdateState = "Download cancelled or timed out. Installed files were not changed."; StatusLabel.Text = app.UpdateState; SafePersist(); }
        catch (Exception ex) { app.UpdateState = "Update stopped: " + ex.Message; StatusLabel.Text = app.UpdateState; SafePersist(); }
        finally { operation.Dispose(); operation = null; busy = false; Progress.IsIndeterminate = false; Progress.Visibility = Visibility.Collapsed; CancelDownload.Visibility = Visibility.Collapsed; Render(); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void Release_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } app) return;
        try { Process.Start(new ProcessStartInfo(Repositories.Resolve(app.Repo).ReleasesUrl) { UseShellExecute = true }); } catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    private void Downloaded_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } app || !File.Exists(app.DownloadedPath)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{app.DownloadedPath}\"") { UseShellExecute = true }); } catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    private void AppFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } app) return;
        try { var folder = AppLocationDraft.From(app).Folder; if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("App folder was not found. Check App settings."); Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    private async void Recovery_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app) return;
        if (MessageBox.Show(this, "Restore files from the last GROUT backup?\n\nThis overwrites later changes to those files and removes files added by that update. Close the app first.\n\n" + app.LastBackup, "Restore previous files", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        busy = true;
        try
        {
            var previous = await Task.Run(() => FileUpdates.Restore(app.LastBackup));
            if (previous != null) previous.Apply(app);
            else { app.Installed = ""; app.InstalledProvenance = "Files restored; installed version is unknown. Check or enter it manually."; }
            app.LastBackup = ""; app.UpdateState = "Previous files restored"; InstalledVersions.Reconcile(app); SafePersist();
        }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
        finally { busy = false; Render(); }
    }
    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } app || busy) return;
        try
        {
            var registered = app.UpdateMethod is "ReplaceExe" or "Zip" ? null : AppUninstall.Registered(app, InstallDiscovery.ReadRegistry());
            if (registered != null)
            {
                var command = AppUninstall.Command(registered.Uninstall);
                if (MessageBox.Show(this, $"Uninstall {registered.Name}?\n\nWindows will open its uninstaller. Follow its prompts to finish.\n\n{command.FileName}\n{command.Arguments}\n\nGROUT will keep this library entry until you choose Stop tracking.", "Uninstall app", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                Process.Start(command); StatusLabel.Text = "Uninstaller opened. When finished, choose Stop tracking to remove the library entry.";
            }
            else if (app.UpdateMethod == "ReplaceExe")
            {
                var draft = AppLocationDraft.From(app);
                if (draft.Conflict) throw new InvalidOperationException("Resolve the app location conflict in App settings first.");
                var target = SetupPaths.PortableExe(draft.Folder, draft.FileName);
                if (!File.Exists(target)) throw new FileNotFoundException("App EXE was not found. Check App settings.");
                if (MessageBox.Show(this, $"Remove the portable app EXE?\n\n{target}\n\nClose the app first. This moves only this EXE to the Recycle Bin. Settings, other files, backups and the library entry will remain.", "Remove portable app", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(target, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                StatusLabel.Text = "App EXE moved to the Recycle Bin. Choose Stop tracking to remove the library entry.";
            }
            else if (app.UpdateMethod == "Zip")
            {
                var draft = AppLocationDraft.From(app);
                if (draft.Conflict) throw new InvalidOperationException("Resolve the app location conflict in App settings first.");
                var target = AppUninstall.PortableFolder(draft.Folder);
                if (MessageBox.Show(this, $"Remove {app.AppName} and its entire app folder?\n\n{target}\n\nClose the app first. ALL files and subfolders here, including settings and any files you added, will move to the Recycle Bin. Check that this folder belongs only to this app.\n\nThe library entry will remain until you choose Stop tracking.", "Remove portable app folder", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(target, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                StatusLabel.Text = "App folder moved to the Recycle Bin. Choose Stop tracking to remove the library entry.";
            }
            else
            {
                MessageBox.Show(this, "No unique Windows uninstaller was found for this app folder. Windows Installed apps will open so you can select it.", "Uninstall app");
                Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
            }
        }
        catch (Exception ex) { StatusLabel.Text = "Could not uninstall: " + ex.Message; }
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app) return;
        if (MessageBox.Show(this, "Stop tracking " + app.Repo + "? Installed files and backups will stay in place.", "Stop tracking", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        apps.Remove(app); SafePersist(); Render();
    }
}
