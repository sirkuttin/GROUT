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
    private readonly ReleaseClient github;
    private readonly SelfUpdater selfUpdater = new();
    private SelfRelease? selfRelease;
    private bool selfChecking, checkingApps;
    private CancellationTokenSource? checkCancellation;
    private readonly Store store;
    private readonly string preferencesPath;
    private readonly string libraryPath;
    private readonly Action shutdownApplication;
    private readonly MenuDismissal menuDismissal = new();
    private object? libraryContent;
    internal bool LibraryReady { get; private set; }
    private Preferences preferences = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private bool busy, exiting;
    private bool uiReady, filtering;
    private string section = "Library";
    private CancellationTokenSource? operation;
    private readonly bool preview;
    private TrackedApp? versionInputItem;
    private string versionInputBaseline = "";
    private readonly Dictionary<string, string> versionCheckErrors = [];
    private TrackedApp? Current => Library.SelectedItem as TrackedApp;
    public MainWindow(bool preview = false, bool startHidden = false, string? dataDirectory = null, ReleaseClient? releaseClient = null, Action? shutdown = null)
    {
        shutdownApplication = shutdown ?? (() => Application.Current.Shutdown());
        github = releaseClient ?? new ReleaseClient();
        this.preview = preview;
        dataDirectory ??= AppPaths.Data;
        libraryPath = Path.Combine(dataDirectory, "apps.json");
        store = new Store(libraryPath);
        preferencesPath = Path.Combine(dataDirectory, "settings.json");
        InitializeComponent(); Library.ItemsSource = apps; uiReady = true;
        var treeView = (System.Windows.Data.ListCollectionView)System.Windows.Data.CollectionViewSource.GetDefaultView(apps);
        treeView.Filter = value => value is TrackedApp node && node.TreeVisible;
        treeView.CustomSort = new TreeOrderComparer();
        Ui.ConstrainToScreen(this);
        libraryContent = Content;
        menuDismissal.Connect(visible => MenuClickShield.Visibility = visible ? Visibility.Visible : Visibility.Collapsed);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && menuDismissal.ShieldVisible) { menuDismissal.Dispose(); e.Handled = true; } };
        Deactivated += (_, _) => menuDismissal.Dispose();
        PreviewMouseDown += (_, e) => { if (menuDismissal.ConsumeWindowClick(e.ChangedButton, System.Windows.Input.MouseButtonState.Pressed)) { treeDragCandidate = null; e.Handled = true; } };
        PreviewMouseUp += (_, e) => { if (menuDismissal.ConsumeWindowClick(e.ChangedButton, System.Windows.Input.MouseButtonState.Released)) { treeDragCandidate = null; e.Handled = true; } };
        Closed += (_, _) => { menuDismissal.Dispose(); timer.Stop(); if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); tray = null; } trayIcon?.Dispose(); trayIcon = null; github.Dispose(); selfUpdater.Dispose(); };
        Closing += (_, _) => { if (!LibraryReady && IsVisible) Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown())); };
        if (preview)
        {
            LibraryReady = true;
            apps.Add(new() { Repo = "example/desktop-app", Installed = "v1.0.0", Latest = "v1.1.0", AssetName = "DesktopApp-x64.exe", UpdateMethod = "Installer", Release = new("v1.1.0", "", "A faster launch and several useful improvements.", [new("DesktopApp-x64.exe", "", 0, null)]) });
            Library.SelectedIndex = 0; Render(); StatusLabel.Text = "Preview data • Your library is untouched"; return;
        }
        if (LoadLibrary()) StartLibrary(startHidden);
    }
    private bool LoadLibrary()
    {
        var reading = preferencesPath;
        try
        {
            var loadedPreferences = Preferences.Load(preferencesPath);
            reading = libraryPath;
            var loadedApps = store.Load();
            foreach (var app in loadedApps) if (!app.IsGroup) Repositories.Parse(app.Repo);
            LibraryTree.Prepare(loadedApps, false, "");
            preferences = loadedPreferences; apps.Clear(); foreach (var app in loadedApps) apps.Add(app);
            Content = libraryContent; SidebarColumn.Width = new GridLength(preferences.SidebarWidth); LibraryReady = true; return true;
        }
        catch (Exception ex)
        {
            LibraryReady = false;
            Content = DataRecovery.View(reading, ex, () => { if (LoadLibrary()) StartLibrary(false); }, () => Application.Current.Shutdown());
            return false;
        }
    }
    private void StartLibrary(bool startHidden)
    {
        if (preferences.RetryAfter is { } legacyPause && legacyPause > DateTimeOffset.UtcNow) preferences.SourceBackoffs["github.com"] = legacyPause;
        preferences.RetryAfter = null; github.RestoreBackoffs(preferences.SourceBackoffs);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open GROUT", null, (_, _) => Dispatcher.Invoke(ShowMain));
        menu.Items.Add("Check now", null, (_, _) => Dispatcher.InvokeAsync(async () => await CheckAll(true)));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => { ShowMain(); Settings_Click(this, new()); }));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Quit));
        trayIcon = Ui.LoadTrayIcon();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "GROUT", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
        tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(ShowMain);
        Closing += (_, e) =>
        {
            if (exiting) return;
            if (!preferences.CloseToTray)
            {
                if (busy || OwnedWindows.Count > 0) { e.Cancel = true; StatusLabel.Text = "Finish the current operation or close the open dialog before exiting."; return; }
                exiting = true; Dispatcher.BeginInvoke(shutdownApplication); return;
            }
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
        Activated += (_, _) => { if (!busy && OwnedWindows.Count == 0) ReconcileAll(); };
        timer.Tick += async (_, _) => { if (busy || treeDragging || OwnedWindows.Count > 0) return; ReconcileAll(); if (preferences.IsDue(DateTimeOffset.UtcNow) && apps.Count > 0) await CheckAll(false); await CheckSelf(false); };
        timer.Start();
        async void Ready()
        {
            if (apps.Count > 0) Library.SelectedIndex = 0;
            ReconcileAll(); Render();
            if (startHidden) Hide();
            if (preferences.IsDue(DateTimeOffset.UtcNow) && apps.Count > 0) await CheckAll(false);
            await CheckSelf(false);
        }
        if (IsLoaded) Dispatcher.BeginInvoke(new Action(Ready)); else Loaded += (_, _) => Ready();
        Application.Current.SessionEnding += (_, _) => { exiting = true; };
        Render();
    }
    private sealed class TreeOrderComparer : System.Collections.IComparer { public int Compare(object? x, object? y) => ((TrackedApp)x!).TreeOrder.CompareTo(((TrackedApp)y!).TreeOrder); }
    internal void PreviewModLibrary()
    {
        if (!preview) return;
        var d2r = new TrackedApp { IsGroup = true, CustomName = "D2R" }; apps.Add(d2r);
        var reimagined = new TrackedApp { ParentId = d2r.Id, Repo = "D2R-Reimagined/d2r-reimagined-mod", CustomName = "Reimagined", ItemKind = "Mod", Installed = "3.1.1", Latest = "3.1.1", UpdateMethod = "Zip", UpdateTarget = @"D:\Games\D2R\mods\Reimagined.mpq", AppFolder = @"D:\Games\D2R\mods\Reimagined.mpq", DefaultChildFolder = @"D:\Games\D2R\d2rloader" }; apps.Add(reimagined);
        foreach (var name in new[] { "Auto Deposit", "Remote Stash" }) apps.Add(new() { ParentId = reimagined.Id, Repo = "https://d2rloader.net/hub/plugins/" + (name == "Auto Deposit" ? "reimagined-only-auto-deposit" : "remote-stash"), CustomName = name, UpdateMethod = "Zip", UpdateTarget = @"D:\Games\D2R\d2rloader\" + (name == "Auto Deposit" ? "auto-deposit" : "remote-stash"), AppFolder = @"D:\Games\D2R\d2rloader\" + (name == "Auto Deposit" ? "auto-deposit" : "remote-stash"), Installed = "1.0", Latest = name == "Remote Stash" ? "2.0" : "1.0", AssetName = "Mod.zip", Release = new(name == "Remote Stash" ? "2.0" : "1.0", "", "", [new("Mod.zip", "", 0, null)]) });
        Render(); Library.SelectedItem = reimagined; Render();
    }
    internal void PreviewCollapsedGroups()
    {
        PreviewModLibrary(); var steam = new TrackedApp { IsGroup = true, CustomName = "Steam Frame", Expanded = false }; apps.Add(steam);
        apps.Add(new() { ParentId = steam.Id, Repo = "example/framedrop", CustomName = "FrameDrop", Installed = "1.0", Latest = "1.0" });
        foreach (var node in apps) if (node.IsGroup) node.Expanded = false;
        Render(); Library.SelectedItem = apps.First(n => n.AppName == "D2R"); Render();
    }
    private void RefreshLibrary()
    {
        filtering = true;
        try
        {
            LibraryTree.Prepare(apps, section == "Updates", LibrarySearch.Text.Trim());
            System.Windows.Data.CollectionViewSource.GetDefaultView(apps).Refresh();
            if (Library.SelectedItem == null && Library.Items.Count > 0) Library.SelectedIndex = 0;
        }
        finally { filtering = false; }
    }
    private void UpdatesOnly_Click(object sender, RoutedEventArgs e) { if (!uiReady) return; section = UpdatesOnlyFilter.IsChecked == true ? "Updates" : "Library"; Render(); }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (uiReady && !filtering) Render(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (uiReady && !filtering) Render(); }
    private void ShowAddMenu(TrackedApp? parent)
    {
        var menu = new ContextMenu();
        void Add(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => { menuDismissal.Dispose(); action(); }; menu.Items.Add(item); }
        Add("Add app…", () => OpenSetup(null, false, parent)); Add("Add mod…", () => OpenSetup(null, true, parent));
        Add("New group…", () => { var group = new TrackedApp { IsGroup = true, CustomName = "New group", ParentId = parent?.Id ?? "" }; if (LibraryTree.Edit(this, group, apps)) { apps.Add(group); if (parent != null) parent.Expanded = true; SafePersist(); Render(); Library.SelectedItem = group; } });
        menu.PlacementTarget = AddAppButton; menuDismissal.Open(menu);
    }
    private void AddChild_Click(object sender, RoutedEventArgs e) { if (!busy && !preview) ShowAddMenu(Current); }
    private void Organize_Click(object sender, RoutedEventArgs e)
    {
        if (busy || preview || Current is not { } node) return;
        OrganizeNode(node);
    }
    private void OrganizeNode(TrackedApp node)
    {
        if (busy || preview) return;
        if (LibraryTree.Edit(this, node, apps)) { SafePersist(); Render(); }
    }
    internal void ToggleGroupHeader(TrackedApp node)
    {
        if (!node.IsGroup || !apps.Contains(node) || !node.CanToggleTree) return;
        node.Expanded = !node.Expanded; SafePersist(); Render();
    }
    private void ExpandNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrackedApp node } && node.CanToggleTree) { node.Expanded = !node.Expanded; SafePersist(); Render(); e.Handled = true; }
    }
    private void TreeKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None && NavigateLibrary(e.Key)) e.Handled = true;
    }
    internal bool NavigateLibrary(System.Windows.Input.Key key)
    {
        if (Current is not { } node || key is not (System.Windows.Input.Key.Left or System.Windows.Input.Key.Right)) return false;
        TrackedApp? target = null;
        if (key == System.Windows.Input.Key.Right)
        {
            if (node.CanToggleTree && !node.Expanded) { node.Expanded = true; SafePersist(); Render(); }
            else target = apps.Where(n => n.ParentId == node.Id && n.TreeVisible).OrderBy(n => n.TreeOrder).FirstOrDefault();
        }
        else if (node.CanToggleTree && node.Expanded) { node.Expanded = false; SafePersist(); Render(); }
        else target = apps.FirstOrDefault(n => n.Id == node.ParentId && n.TreeVisible);
        var keepFocus = Library.IsKeyboardFocusWithin;
        if (target != null) Library.SelectedItem = target;
        if (Library.SelectedItem != null) Library.ScrollIntoView(Library.SelectedItem);
        if (keepFocus) Dispatcher.BeginInvoke(new Action(() => { if (Library.IsKeyboardFocusWithin && Library.ItemContainerGenerator.ContainerFromItem(Library.SelectedItem) is ListBoxItem row) row.Focus(); }));
        return true;
    }
    private void TreeRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Library, source) is ListBoxItem { DataContext: TrackedApp node } item)
        { treeContextNode = node; SelectMenuNode(node); }
    }
    internal void SelectMenuNode(TrackedApp node)
    {
        if (apps.Contains(node)) Library.SelectedItem = node;
    }
    private TrackedApp? treeContextNode;
    private void TreeMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        var node = treeContextNode ?? Current; treeContextNode = null;
        if (busy || preview || node == null) { e.Handled = true; return; }
        var menu = CreateTreeMenu(node); Library.ContextMenu = menu; menu.PlacementTarget = Library; menuDismissal.Open(menu); e.Handled = true;
    }
    internal ContextMenu CreateTreeMenu(TrackedApp node)
    {
        var menu = new ContextMenu();
        void Add(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => { menuDismissal.Dispose(); action(); }; menu.Items.Add(item); }
        Add("Add app here…", () => OpenSetup(null, false, node)); Add("Add mod here…", () => OpenSetup(null, true, node));
        Add("New group here…", () => { var group = new TrackedApp { IsGroup = true, CustomName = "New group", ParentId = node.Id }; if (LibraryTree.Edit(this, group, apps)) { apps.Add(group); node.Expanded = true; SafePersist(); Render(); } });
        Add("Name, parent and child folder…", () => OrganizeNode(node));
        Add(node.IsGroup ? "Remove group" : "Stop tracking", () => RemoveNode(node));
        return menu;
    }
    private void TreeOverflow_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (busy || preview || sender is not Button { Tag: TrackedApp node } button) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (busy || !apps.Contains(node)) return;
            var menu = PrepareOverflowMenu(node); menuDismissal.Open(menu);
        }), DispatcherPriority.Input);
    }
    internal ContextMenu PrepareOverflowMenu(TrackedApp node)
    {
        SelectMenuNode(node);
        Library.UpdateLayout();
        Button? FindOptions(DependencyObject root)
        {
            if (root is Button candidate && ReferenceEquals(candidate.Tag, node) && Equals(candidate.Content, "⋯")) return candidate;
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            { var found = FindOptions(System.Windows.Media.VisualTreeHelper.GetChild(root, i)); if (found != null) return found; }
            return null;
        }
        var anchor = Library.ItemContainerGenerator.ContainerFromItem(node) is DependencyObject row ? FindOptions(row) : null;
        var menu = CreateTreeMenu(node);
        if (anchor != null)
        {
            anchor.ContextMenu = menu;
            menu.PlacementRectangle = new Rect(anchor.TranslatePoint(new Point(), Library), anchor.RenderSize);
        }
        // The row/button may be replaced by selection refresh. Anchor the popup
        // to the stable list and use the current overflow button's rectangle.
        menu.PlacementTarget = Library; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        return menu;
    }
    private void LibraryLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SidebarColumn.MaxWidth = Math.Max(220, e.NewSize.Width - 364);
    }
    private void SidebarResize_Completed(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => SaveSidebarWidth();
    private void SidebarResize_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right) SaveSidebarWidth();
    }
    internal void SaveSidebarWidth()
    {
        if (!LibraryReady || preview) return;
        preferences.SidebarWidth = Math.Max(220, Math.Min(800, SidebarColumn.ActualWidth));
        try { preferences.Save(preferencesPath); } catch (Exception ex) { StatusLabel.Text = "Could not save sidebar width: " + ex.Message; }
    }
    private void ShowMain() { Show(); WindowState = WindowState.Normal; Activate(); }
    private async Task CheckSelf(bool manual)
    {
        if (busy || selfChecking || preview || OwnedWindows.Count > 0) return;
        if (!manual && (!preferences.SelfUpdateChecks || preferences.SelfUpdateLastAttempt is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromDays(1))) return;
        selfChecking = true; preferences.SelfUpdateLastAttempt = DateTimeOffset.UtcNow; SelfUpdateLabel.Text = "Checking GROUT updates…"; Render();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            selfRelease = await selfUpdater.Check(timeout.Token);
            InstallSelfButton.Visibility = selfRelease != null ? Visibility.Visible : Visibility.Collapsed;
            SelfUpdateLabel.Text = selfRelease != null ? $"GROUT {selfRelease.Version} is available" : $"GROUT {SelfUpdater.CurrentLabel} · No newer release";
            if (!manual && selfRelease != null && preferences.SelfUpdateNotifiedTag != selfRelease.Tag)
            {
                if (!preferences.Quiet) tray?.ShowBalloonTip(8000, "GROUT update available", $"GROUT {selfRelease.Version} is ready. Open GROUT to update and restart.", Forms.ToolTipIcon.Info);
                preferences.SelfUpdateNotifiedTag = selfRelease.Tag;
            }
        }
        catch (Exception ex) { selfRelease = null; SelfUpdateLabel.Text = ex is OperationCanceledException ? "GROUT update check timed out · Try again" : "GROUT update check failed · Try again"; SelfUpdateLabel.ToolTip = ex.Message; }
        finally { selfChecking = false; SafePersist(); Render(); }
    }
    private async void CheckSelf_Click(object sender, RoutedEventArgs e) => await CheckSelf(true);
    private async void InstallSelf_Click(object sender, RoutedEventArgs e)
    {
        if (busy || selfChecking || selfRelease is not { } release || OwnedWindows.Count > 0 || preview) return;
        if (MessageBox.Show(this, $"Update GROUT from {SelfUpdater.CurrentLabel} to {release.Version} and restart?\n\nThe verified GitHub release will replace:\n{Framework.ProcessPath}\n\nYour library and settings will be preserved.", "Update GROUT", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        busy = true; operation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        var folder = AppPaths.NewWork("self-update"); Process? helper = null;
        Progress.IsIndeterminate = true; Progress.Visibility = CancelDownload.Visibility = Visibility.Visible; Render();
        try
        {
            SelfUpdateLabel.Text = "Downloading and verifying GROUT update…";
            var package = await selfUpdater.Download(release, folder, operation.Token);
            CancelDownload.IsEnabled = false;
            SelfUpdateLabel.Text = "Preparing GROUT restart…";
            helper = await SelfUpdateHandoff.Start(package.Path, package.Hash, folder);
            SafePersist(); StopWindow(); Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            if (helper != null) { try { helper.Kill(); await helper.WaitForExitAsync(); } catch { } }
            StorageCleanup.DeleteFolder(folder, SelfUpdateHandoff.WorkRoot);
            SelfUpdateLabel.Text = ex is OperationCanceledException ? "GROUT update cancelled. Your current version is unchanged." : "GROUT update stopped: " + ex.Message;
        }
        finally
        {
            helper?.Dispose(); operation?.Dispose(); operation = null; busy = false;
            Progress.IsIndeterminate = false; Progress.Visibility = CancelDownload.Visibility = Visibility.Collapsed; CancelDownload.IsEnabled = true; Render();
        }
    }
    private void Quit()
    {
        if (busy || OwnedWindows.Count > 0) { ShowMain(); MessageBox.Show(this, "Finish the current operation or close the open dialog before exiting.", "GROUT"); return; }
        StopWindow(); Application.Current.Shutdown();
    }
    internal void StopWindow() { exiting = true; Close(); }
    internal bool TrayVisible => tray?.Visible == true;
    private void Persist() { if (preview || !LibraryReady) return; if (!busy && OwnedWindows.Count == 0) StorageCleanup.Sweep(apps); store.Save(apps); preferences.SourceBackoffs = github.Backoffs; preferences.Save(preferencesPath); Library.Items.Refresh(); }
    private void SafePersist() { try { Persist(); } catch (Exception ex) { StatusLabel.Text = "Could not save: " + ex.Message; } }
    private void Render()
    {
        if (!uiReady || filtering || !LibraryReady) return;
        RefreshLibrary();
        var app = Current;
        TargetContextLabel.Text = app?.IsMod == true ? (app.TargetLabel.Length > 0 ? "Group: " + app.TargetLabel + "\n" : "") + "Install folder: " + AppLocationDraft.From(app).Folder : "";
        TargetContextLabel.Visibility = app?.IsMod == true ? Visibility.Visible : Visibility.Collapsed;
        UninstallButton.Content = app?.IsMod == true ? "Remove mod files…" : "Uninstall…";
        AppSettingsButton.Content = "Settings"; AppFolderButton.Content = app?.IsMod == true ? "Open mod folder" : "Open app folder";
        AddAppButton.Content = "+ Add…";
        CheckSelfButton.IsEnabled = InstallSelfButton.IsEnabled = !busy && !selfChecking;
        CheckSelfButton.Visibility = selfRelease == null ? Visibility.Visible : Visibility.Collapsed;
        InstallSelfButton.Visibility = selfRelease != null ? Visibility.Visible : Visibility.Collapsed;
        if (SelfUpdateLabel.Text.Length == 0) SelfUpdateLabel.Text = "GROUT " + SelfUpdater.CurrentLabel;
        var visibleScope = apps.Where(a => !a.IsGroup).ToList();
        CountLabel.Text = checkingApps ? "Checking for updates…" : $"{visibleScope.Count} tracked item(s) · {visibleScope.Count(a => a.CanUpdate && ReleaseFiles.Match(a) != null)} update(s) ready";
        CheckButton.Content = checkingApps ? checkCancellation?.IsCancellationRequested == true ? "Cancelling…" : "Cancel checks" : "Check now"; UpdatesOnlyFilter.Content = "Updates only (" + visibleScope.Count(a => a.CanUpdate) + ")";
        AddAppButton.IsEnabled = AppSettingsButton.IsEnabled = SettingsButton.IsEnabled = !busy;
        CheckButton.IsEnabled = checkingApps ? checkCancellation?.IsCancellationRequested == false : !busy;
        VersionSection.IsEnabled = UninstallButton.IsEnabled = RecoveryButton.IsEnabled = RemoveButton.IsEnabled = !busy;
        Details.Visibility = app == null || app.IsGroup ? Visibility.Collapsed : Visibility.Visible;
        AppTitle.Text = app?.IsGroup == true ? "Select an app" : app?.AppName ?? (section == "Updates" ? "No matching updates." : "No matching items.");
        SourceLabel.Text = app?.IsGroup == true ? "" : app?.RepositoryUrl ?? "";
        VersionLabel.Text = app?.IsGroup == true ? "" : app?.Versions ?? "Choose Add App to track an existing installation or install something new from a supported release host.";
        StateLabel.Text = app?.ActiveOperation.Length > 0 ? "Updating…" : app?.DisplayStatus ?? "";
        var missing = app?.InstallationMissing == true && !app.UninstallPending;
        MissingBanner.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        StateLabel.Visibility = VersionSection.Visibility = MethodLabel.Visibility = missing ? Visibility.Collapsed : Visibility.Visible;
        MissingMessage.Text = "The app may have been moved or uninstalled. Find it again or stop tracking it.\n\n" + app?.LocalStatus;
        StateLabel.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(app?.StatusColor ?? "#76DCC6"));
        VersionSection.IsEnabled = UninstallButton.IsEnabled = RecoveryButton.IsEnabled = AppSettingsButton.IsEnabled = !busy && !missing && app?.UninstallPending != true;
        FindInstallationButton.IsEnabled = !busy;
        RemoveButton.Visibility = missing ? Visibility.Collapsed : Visibility.Visible;
        // Background refreshes must not replace a draft, including after focus moves to Save.
        if (versionInputItem != app || InstalledInput.Text == versionInputBaseline)
        {
            versionInputItem = app;
            InstalledInput.Text = versionInputBaseline = app?.Installed ?? "";
        }
        LocalLabel.Text = app?.LocalStatus ?? "";
        VersionCheckError.Text = app != null && versionCheckErrors.TryGetValue(app.Id, out var versionError) ? versionError : "";
        var asset = app != null ? ReleaseFiles.Match(app) : null;
        UpdateButton.IsEnabled = !busy && app?.CanUpdate == true && (asset != null || ReleaseFiles.UpdateCandidates(app).Count > 0) && (asset?.OriginHost != "nexusmods.com" || NexusMods.Enabled);
        UpdateButton.Content = app?.CanUpdate == true && asset == null && ReleaseFiles.UpdateCandidates(app).Count > 0 ? "Choose download…" : asset?.OriginHost == "nexusmods.com" && (preview || !github.Nexus.CanDownload) ? "Open Nexus download" : "Update";
        UpdateButton.ToolTip = busy ? "Wait for the current operation to finish." : app?.Error.Length > 0 ? "The release check failed. Check now or review App settings." : app is { AutoReconcile: true, LocalError: true } ? "Installed version could not be verified. Recheck it or enter it manually." : app?.IsOlderRelease == true ? "The available version is older than the installed version. Automatic downgrades are disabled; review the release channel in App settings." : app?.Installed.Length == 0 ? "Set the installed version or configure detection first." : app?.IsCurrent == true ? "This app is up to date." : asset == null ? "Choose the download file to continue with this update." : "Apply the configured update method.";
        MethodLabel.Text = app == null ? "" : ReleaseFiles.Label(app.UpdateMethod, app.IsMod) + (app.UpdateMethod is "Zip" or "ReplaceExe" ? " → " + app.UpdateTarget : "");
        UpdateStateLabel.Text = app == null || missing ? "" : string.Join("\n", new[] { app.Error, app.Release?.SourceWarning ?? "", ReleaseFiles.SelectionExplanation(app), app.ActiveOperation.Length > 0 ? app.ActiveOperation : app.UpdateIssue }.Where(s => s.Length > 0));
        UpdateStateLabel.Visibility = UpdateStateLabel.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadedButton.Visibility = app?.UpdateMethod == "Manual" && File.Exists(app.DownloadedPath) ? Visibility.Visible : Visibility.Collapsed;
        AppFolderButton.Visibility = app != null && AppLocationDraft.From(app).Folder.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AppFolderButton.IsEnabled = !busy && !missing && app != null && Directory.Exists(AppLocationDraft.From(app).Folder);
        RecoveryButton.Visibility = app != null && File.Exists(Path.Combine(app.LastBackup, "recovery.json")) ? Visibility.Visible : Visibility.Collapsed;
        KeepTrackingButton.Visibility = app?.UninstallPending == true ? Visibility.Visible : Visibility.Collapsed;
        Notes.Text = app?.Release?.Notes ?? "Check for releases to load notes.";
        CheckedLabel.Text = app?.CheckedAt is { } time ? $"Release checked {time.ToLocalTime():g}" : "Release not checked yet";

    }
    private void ReconcileAll()
    {
        if (busy || treeDragging || preview || OwnedWindows.Count > 0) return;
        foreach (var app in apps)
        {
            if (app.IsGroup) continue;
            InstalledVersions.Reconcile(app);
            if (app.UpdateState.Length > 0 && app.UpdateIssue.Length == 0) app.UpdateState = "";
        }
        CheckPendingUninstalls();
        SafePersist();
        if (!InstalledInput.IsKeyboardFocusWithin) Render();
    }
    private Task Fetch(TrackedApp app) => ReleaseChecks.Fetch(github, app);
    private async Task CheckAll(bool manual)
    {
        if (busy || treeDragging || preview || OwnedWindows.Count > 0) return;
        await CheckLibrary();
    }
    internal async Task CheckLibrary()
    {
        busy = true; checkingApps = true; preferences.LastAttempt = DateTimeOffset.UtcNow;
        checkCancellation = new CancellationTokenSource();
        var checking = apps.Where(a => !a.IsGroup).ToList();
        var alerts = new List<TrackedApp>();
        try
        {
            Render();
            await ReleaseChecks.Run(github, checking, app =>
            {
                if (ReleaseFiles.TakeNotification(app, preferences.Quiet)) alerts.Add(app);
                Library.Items.Refresh(); Render();
            }, checkCancellation.Token);
            if (checking.All(a => a.Error.Length == 0)) { preferences.LastSuccess = DateTimeOffset.UtcNow; preferences.RetryAfter = null; }
            Persist();
        }
        catch (OperationCanceledException) when (checkCancellation.IsCancellationRequested) { SafePersist(); }
        catch (Exception ex) { StatusLabel.Text = ex.Message; SafePersist(); }
        finally
        {
            checkCancellation.Dispose(); checkCancellation = null; checkingApps = false; busy = false; Render();
            if (alerts.Count > 0) tray?.ShowBalloonTip(8000, $"{alerts.Count} app update(s) available", string.Join("\n", alerts.Take(4).Select(a => a.Repo + " · " + a.Latest)), Forms.ToolTipIcon.Info);
        }
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (busy || preview) return;
        ShowAddMenu(null);
    }
    private void OpenSetup(string? link, bool mod, TrackedApp? parent = null)
    {
        var wizard = new SetupWindow(github, apps.Where(a => !a.IsGroup).Select(a => a.Repo), trackedApps: apps, initialLink: link, mod: mod, parentId: parent?.Id ?? "", childDefaultFolder: parent?.DefaultChildFolder ?? "") { Owner = this };
        if (wizard.ShowDialog() != true) return;
        if (wizard.ExistingRepo.Length > 0) { Library.SelectedItem = apps.First(a => a.Repo.Equals(wizard.ExistingRepo, StringComparison.OrdinalIgnoreCase)); return; }
        section = "Library"; UpdatesOnlyFilter.IsChecked = false; LibrarySearch.Text = ""; if (parent != null) parent.Expanded = true;
        wizard.Result.InstallationBefore = []; apps.Add(wizard.Result); SafePersist(); Library.SelectedItem = wizard.Result; Render();
    }
    private async void AppSettings_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { IsGroup: false } app || preview) return;
        var wizard = new SetupWindow(github, apps.Where(a => a != app && !a.IsGroup).Select(a => a.Repo), app, trackedApps: apps.Where(a => a != app)) { Owner = this };
        if (wizard.ShowDialog() != true) return;
        var index = apps.IndexOf(app); apps[index] = wizard.Result; Library.SelectedItem = wizard.Result; busy = true;
        try { await Fetch(wizard.Result); SafePersist(); } finally { busy = false; Render(); }
    }
    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        if (checkingApps) { checkCancellation?.Cancel(); Render(); return; }
        if (OwnedWindows.Count == 0) await CheckAll(true);
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (busy || preview || OwnedWindows.Count > 0) return;
        try { SettingsWindow.Show(this, preferences); }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    private void Selected(object sender, SelectionChangedEventArgs e) { ActionErrorLabel.Text = ""; ActionErrorLabel.ToolTip = null; Render(); VersionSection.IsExpanded = Current is { } app && (app.Installed.Length == 0 || app.LocalError); }
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
            panel.Children.Add(Ui.Button("Choose app EXE…", (_, _) => { var picker = new Microsoft.Win32.OpenFileDialog { Title = "Find the installed app", Filter = "Application|*.exe" }; if (picker.ShowDialog(dialog) == true) Choose(Framework.GetDirectoryName(picker.FileName)!, Path.GetFileName(picker.FileName)); }));
            panel.Children.Add(Ui.Button("Choose app folder…", (_, _) => { var picker = new FolderPicker { Title = "Find the installed app folder" }; if (picker.ShowDialog(dialog) == true) Choose(picker.FolderName, ""); }));
            panel.Children.Add(Ui.Button("Cancel", (_, _) => dialog.DialogResult = false));
            dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            if (dialog.ShowDialog() == true) { SafePersist(); ActionErrorLabel.Text = ""; }
        }
        catch (Exception ex) { ActionErrorLabel.Text = "Could not find installation: " + ex.Message; }
        finally { busy = false; Render(); }
    }
    private void Recheck_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app) return;
        versionCheckErrors.Remove(app.Id);
        if (app.AutoReconcile) { ReconcileAll(); if (!app.LocalError && !app.InstallationMissing) { InstallerWait.ConfirmVersion(app); SafePersist(); Render(); } return; }
        try { var found = InstalledVersions.Read(app.InstallLocation, InstalledVersions.UsesD2RLoaderManifest(app)); app.Installed = found.Version; app.InstalledProvenance = "Version checked from " + found.Source; InstalledVersions.Reconcile(app); InstallerWait.ConfirmVersion(app); SafePersist(); }
        catch (Exception ex) { versionCheckErrors[app.Id] = "Could not read the installed version. Last recorded version kept. " + ex.Message; }
        Render();
    }
    private void Mark_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app || app.Latest.Length == 0) return;
        versionCheckErrors.Remove(app.Id);
        InstallerWait.ConfirmVersion(app);
        app.Installed = app.Latest; app.PartialUpdate = false; app.AutoReconcile = false; if (app.VersionRecord is { } record) app.VersionRecord = record with { Version = app.Installed, Evidence = "Manual" }; app.InstalledProvenance = "Manually marked installed"; app.LocalStatus = app.InstalledProvenance; app.UpdateState = ""; versionInputItem = null; SafePersist(); Render();
    }
    private void SaveVersion_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app) return;
        versionCheckErrors.Remove(app.Id);
        InstallerWait.ConfirmVersion(app);
        app.Installed = InstalledInput.Text.Trim(); app.PartialUpdate = false; app.AutoReconcile = false; if (app.VersionRecord is { } record) app.VersionRecord = record with { Version = app.Installed, Evidence = "Manual" }; app.InstalledProvenance = "Manual version tracking"; app.LocalStatus = app.InstalledProvenance; versionInputItem = null; SafePersist(); Render();
    }
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { CanUpdate: true } app || preview) return;
        var asset = ReleaseFiles.Match(app);
        if (asset == null) { if (ReleaseDownloadPicker.Show(this, app)) { SafePersist(); Render(); } return; }
        if (asset.OriginHost == "nexusmods.com" && !github.Nexus.CanDownload)
        {
            try { Process.Start(new ProcessStartInfo(NexusMods.FilePage(app.Repo, asset)) { UseShellExecute = true }); ActionErrorLabel.Text = ""; }
            catch (Exception ex) { ActionErrorLabel.Text = "Could not open the Nexus download page."; ActionErrorLabel.ToolTip = ex.Message; }
            return;
        }
        busy = true; Progress.Visibility = Visibility.Visible; Render();
        operation = new(); CancelDownload.Visibility = Visibility.Visible;
        var activeOperation = operation;
        var waitingForInstaller = false;
        try { await UpdateRunner.Run(this, app, asset, github, (text, value) => { if (!ReferenceEquals(operation, activeOperation)) return; app.ActiveOperation = text; Render(); Progress.Value = value; Progress.IsIndeterminate = waitingForInstaller || text.StartsWith("Inspecting") || text.StartsWith("Applying") || text.StartsWith("Replacing"); CancelDownload.IsEnabled = waitingForInstaller || text.StartsWith("Downloading") || text.StartsWith("Inspecting ZIP"); }, Persist, operation.Token, installerWaiting: waiting => { waitingForInstaller = waiting; CancelDownload.Content = waiting ? "Stop waiting" : "Cancel download"; CancelDownload.IsEnabled = waiting; }); }
        catch (OperationCanceledException) { app.UpdateState = "Download cancelled or timed out. Installed files were not changed."; SafePersist(); }
        catch (Exception ex) { app.UpdateState = "Update stopped: " + ex.Message; SafePersist(); }
        finally { app.ActiveOperation = ""; operation.Dispose(); operation = null; busy = false; Progress.IsIndeterminate = false; Progress.Visibility = Visibility.Collapsed; CancelDownload.Visibility = Visibility.Collapsed; CancelDownload.Content = "Cancel download"; Render(); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void Repository_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } app) return;
        try { Process.Start(new ProcessStartInfo(app.RepositoryUrl) { UseShellExecute = true }); ActionErrorLabel.Text = ""; } catch (Exception ex) { ActionErrorLabel.Text = "Couldn't open the repository in your browser."; ActionErrorLabel.ToolTip = ex.Message; }
    }
    private void Release_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } app) return;
        try { Process.Start(new ProcessStartInfo(Repositories.Resolve(app.Repo).ReleasesUrl) { UseShellExecute = true }); ActionErrorLabel.Text = ""; } catch (Exception ex) { ActionErrorLabel.Text = "Couldn't open the releases page in your browser."; ActionErrorLabel.ToolTip = ex.Message; }
    }
    private void Downloaded_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } app || !File.Exists(app.DownloadedPath)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{app.DownloadedPath}\"") { UseShellExecute = true }); ActionErrorLabel.Text = ""; } catch (Exception ex) { ActionErrorLabel.Text = "Could not show the downloaded file."; ActionErrorLabel.ToolTip = ex.Message; }
    }
    private void AppFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } app) return;
        try { var folder = AppLocationDraft.From(app).Folder; if (!Directory.Exists(folder)) { InstalledVersions.Reconcile(app); Render(); return; } Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); ActionErrorLabel.Text = ""; }
        catch (Exception ex) { ActionErrorLabel.Text = "Could not open the app folder."; ActionErrorLabel.ToolTip = ex.Message; }
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
            app.LastBackup = ""; app.UpdateState = ""; InstalledVersions.Reconcile(app); SafePersist();
        }
        catch (Exception ex) { app.UpdateState = "Could not restore interrupted files: " + ex.Message; SafePersist(); }
        finally { busy = false; Render(); }
    }
    internal void CheckPendingUninstalls()
    {
        var pending = apps.Where(a => a.UninstallPending).ToList();
        if (pending.Count == 0) return;
        var registrations = InstallDiscovery.ReadRegistry();
        foreach (var app in pending)
        {
            if (app.UninstallRegistrationId.Length > 0 && !AppUninstall.RegistrationGone(app.UninstallRegistrationId)) continue;
            var index = apps.IndexOf(app);
            try
            {
                if (AppUninstall.Complete(apps, app, registrations, Persist))
                {
                    Library.SelectedIndex = Math.Min(index, apps.Count - 1);
                }
            }
            catch (Exception ex) { StatusLabel.Text = "Uninstall finished, but saving the library needs a retry: " + ex.Message; }
        }
    }
    private async Task WatchUninstaller(TrackedApp app, Process process)
    {
        try
        {
            await process.WaitForExitAsync();
            if (!apps.Contains(app) || !app.UninstallPending) return;
            CheckPendingUninstalls();
            if (apps.Contains(app))
            {
                if (process.ExitCode is not (0 or 3010))
                {
                    AppUninstall.KeepTracking(app); app.UpdateState = "Uninstall was cancelled or failed; the app is still tracked.";
                }
                else app.UpdateState = "Uninstall is not confirmed yet. GROUT is watching for removal. If you cancelled it, choose Keep tracking.";
                SafePersist();
            }
        }
        catch (Exception ex) { app.UpdateState = "Could not verify uninstall: " + ex.Message; SafePersist(); }
        finally { process.Dispose(); Render(); }
    }
    private void KeepTracking_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { UninstallPending: true } app) return;
        AppUninstall.KeepTracking(app); app.UpdateState = "";
        SafePersist(); Render();
    }
    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { IsGroup: false } app || busy || app.UninstallPending) return;
        try
        {
            var registered = app.UpdateMethod is "ReplaceExe" or "Zip" ? null : AppUninstall.Registered(app, InstallDiscovery.ReadRegistry());
            if (registered != null)
            {
                var installedFolder = AppLocationDraft.From(app).Folder;
                if (installedFolder.Length > 0 && LibraryTree.OverlapsOtherInstallation(app, installedFolder, apps)) throw new InvalidOperationException("This installation contains other tracked items. Resolve their saved destinations before uninstalling the parent.");
                var command = AppUninstall.Command(registered.Uninstall);
                if (MessageBox.Show(this, $"Uninstall {registered.Name}?\n\nWindows will open its uninstaller. Follow its prompts to finish. GROUT will remove this entry after removal is confirmed.\n\n{command.FileName}\n{command.Arguments}", "Uninstall app", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var draft = AppLocationDraft.From(app);
                var target = draft.FileName.Length > 0 ? Path.Combine(draft.Folder, draft.FileName) : draft.Folder;
                AppUninstall.Begin(app, target, registered.Id); Persist();
                var process = Process.Start(command) ?? throw new IOException("Windows did not start the uninstaller.");
                _ = WatchUninstaller(app, process);
            }
            else if (app.UpdateMethod == "ReplaceExe")
            {
                var draft = AppLocationDraft.From(app);
                if (draft.Conflict) throw new InvalidOperationException("Resolve the app location conflict in App settings first.");
                var target = SetupPaths.PortableExe(draft.Folder, draft.FileName);
                if (!File.Exists(target)) throw new FileNotFoundException("App EXE was not found. Check App settings.");
                if (MessageBox.Show(this, $"Remove {app.AppName}?\n\n{target}\n\nClose the app first. This moves only this EXE to the Recycle Bin and removes its GROUT entry. Settings and other files remain.", "Remove portable app", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                busy = true; AppUninstall.Begin(app, target); Persist(); Render();
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(target, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                CheckPendingUninstalls();
            }
            else if (app.UpdateMethod == "Zip")
            {
                var draft = AppLocationDraft.From(app);
                if (draft.Conflict) throw new InvalidOperationException("Resolve the app location conflict in App settings first.");
                var target = AppUninstall.ZipFolder(app);
                if (LibraryTree.OverlapsOtherInstallation(app, target, apps)) throw new InvalidOperationException("This folder contains other tracked installations. Uninstall them separately or correct their saved folders before removing this parent.");
                if (MessageBox.Show(this, $"Remove {app.AppName} and its entire app folder?\n\n{target}\n\nClose the app first. ALL files and subfolders here, including settings and any files you added, will move to the Recycle Bin. Check that this folder belongs only to this app. Its GROUT entry will also be removed.", "Remove portable app folder", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                busy = true; AppUninstall.Begin(app, target); Persist(); Render();
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(target, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                CheckPendingUninstalls();
            }
            else
            {
                MessageBox.Show(this, "No unique Windows uninstaller was found. Windows Installed apps will open so you can select the app. GROUT will watch its saved location and remove this entry when removal is confirmed. Choose Keep tracking if you cancel.", "Uninstall app");
                var draft = AppLocationDraft.From(app);
                var target = draft.FileName.Length > 0 ? Path.Combine(draft.Folder, draft.FileName) : draft.Folder;
                if (Framework.IsPathFullyQualified(target)) { AppUninstall.Begin(app, target); Persist(); }
                Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
            }
        }
        catch (Exception ex) { if (!AppUninstall.CanConfirm(app, InstallDiscovery.ReadRegistry())) AppUninstall.KeepTracking(app); app.UpdateState = "Uninstall did not finish: " + ex.Message; SafePersist(); }
        finally { busy = false; Render(); }
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Current is not { } app) return;
        RemoveNode(app);
    }
    private void RemoveNode(TrackedApp app)
    {
        if (busy || !apps.Contains(app)) return;
        var prompt = app.IsGroup ? (Title: "Remove group", Message: "Remove " + app.AppName + "? Its children will move up one level. No installed files will change.") : AppUninstall.TrackingPrompt(app);
        if (MessageBox.Show(this, prompt.Message, prompt.Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        LibraryTree.RemoveKeepingChildren(apps, app); SafePersist(); Render();
    }
}
