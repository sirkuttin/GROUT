using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ReleaseDock;

public sealed class AppSettingsPage
{
    private static readonly string[] Methods = ["Manual", "ReplaceExe", "Installer", "Zip"];
    private readonly TrackedApp app;
    private readonly AppLocationDraft draft;
    private readonly TextBox folder, file, manual, appName;
    private readonly TextBlock zipSelection;
    private readonly StackPanel zipPanel;
    private readonly CheckBox automatic, previews;
    private readonly ComboBox method, assets;
    private readonly TextBlock detected, sourceLabel, action;
    private readonly StackPanel filePanel, manualPanel;
    private readonly Window owner;
    private string checkedSource = "", checkedVersion = "", checkedFile = "";
    private bool customName, updatingName;
    private readonly TextBox? repository;
    private readonly TextBox? websiteDownload;
    public string WebsiteDownloadUrl => websiteDownload?.Text.Trim() ?? app.WebsiteDownloadUrl;
    public string RepositoryKey => repository == null ? app.Repo : WebsiteSource.SourceKey(repository.Text);
    public StackPanel Panel { get; } = new();
    private static void Name(DependencyObject control, string label) => System.Windows.Automation.AutomationProperties.SetName(control, label);
    public AppSettingsPage(Window owner, TrackedApp app, bool allowRepositoryEdit = false, ReleaseClient? client = null)
    {
        this.owner = owner; this.app = app; draft = AppLocationDraft.From(app);
        if (allowRepositoryEdit)
        {
            Panel.Children.Add(Ui.Text("Repository URL", 18));
            repository = Ui.Input(app.RepositoryUrl); Name(repository, "Repository URL"); Panel.Children.Add(repository);
            Panel.Children.Add(Ui.Text("Change this when the project moves. Installed files and version are kept; the new source is checked before saving. You may need to choose a new release download afterward.", muted: true));
        }
        if (allowRepositoryEdit || app.Repo.Length > 0 && Repositories.Resolve(app.Repo).IsWebsite)
        {
            var websitePanel = new StackPanel(); websitePanel.Children.Add(Ui.Text("Website download URL", 18)); websiteDownload = Ui.Input(app.WebsiteDownloadUrl); Name(websiteDownload, "Website download URL"); websitePanel.Children.Add(websiteDownload);
            websitePanel.Children.Add(Ui.Text("The versioned download link used to find future releases on the website page.", muted: true)); Panel.Children.Add(websitePanel);
            void ShowWebsite() { try { websitePanel.Visibility = Repositories.Resolve(RepositoryKey).IsWebsite ? Visibility.Visible : Visibility.Collapsed; } catch { websitePanel.Visibility = Visibility.Collapsed; } }
            if (repository != null) repository.TextChanged += (_, _) => ShowWebsite(); ShowWebsite();
        }
        var kind = new ComboBox { ItemsSource = new[] { "Application", "Mod" }, SelectedIndex = app.IsMod ? 1 : 0, Padding = new Thickness(8), Margin = new Thickness(0,0,0,12) };
        LibraryOrganization.ReadableCombo(kind); Name(kind, "Item type"); Panel.Children.Add(Ui.Text("Item type", 18)); Panel.Children.Add(kind);
        kind.SelectionChanged += (_, _) => { app.ItemKind = kind.SelectedIndex == 1 ? "Mod" : "Application"; };
        Panel.Children.Add(Ui.Text(app.IsMod ? "Mod name" : "App name", 18));
        appName = Ui.Input(app.AppName); Name(appName, "App name"); Panel.Children.Add(appName);
        customName = !string.IsNullOrWhiteSpace(app.CustomName);
        appName.TextChanged += (_, _) => { if (!updatingName) customName = !string.IsNullOrWhiteSpace(appName.Text); };
        Panel.Children.Add(Ui.Button("Use detected name", (_, _) => { customName = false; Refresh(); }));
        Panel.Children.Add(Ui.Text(app.IsMod ? "Install folder" : "App folder", 18));
        folder = Ui.Input(draft.Folder); Name(folder, "App folder"); Panel.Children.Add(folder);
        Panel.Children.Add(Ui.Button("Browse…", (_, _) => { var picker = new FolderPicker { Title = "Choose the app folder" }; if (picker.ShowDialog(owner) == true) folder.Text = picker.FolderName; }));
        if (draft.Conflict)
        {
            var conflict = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
            conflict.Children.Add(Ui.Text("Your saved version source and update destination differ. Choose a location before saving.", muted: true));
            conflict.Children.Add(Ui.Text("Version source: " + draft.OriginalSource + "\nUpdate destination: " + draft.OriginalTarget));
            void Resolve(bool existing) { draft.Resolve(existing); var chosenFolder = draft.Folder; var chosenFile = draft.FileName; folder.Text = chosenFolder; file!.Text = chosenFile; conflict.Visibility = Visibility.Collapsed; Refresh(); }
            conflict.Children.Add(Ui.Button("Use existing app folder for both", (_, _) => Resolve(true)));
            conflict.Children.Add(Ui.Button("Keep update destination and separate version source", (_, _) => Resolve(false))); Panel.Children.Add(conflict);
        }
        filePanel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; filePanel.Children.Add(Ui.Text("App file", 18));
        file = Ui.Input(draft.FileName); Name(file, "App file"); filePanel.Children.Add(file); Panel.Children.Add(filePanel);
        Panel.Children.Add(Ui.Text("Updates", 18)); method = Ui.Choice(Methods.Select(key => ReleaseFiles.Label(key, app.IsMod)), Math.Max(0, Array.IndexOf(Methods, app.UpdateMethod))); Name(method, "Update method"); Panel.Children.Add(method);
        zipPanel = new StackPanel(); zipPanel.Children.Add(Ui.Text("ZIP contents", 18));
        zipSelection = Ui.Text(ZipContentPicker.Label(app), muted: true); zipPanel.Children.Add(zipSelection);
        var chooseContents = Ui.Button("Choose ZIP contents…", (_, _) =>
        {
            if (client == null || (assets!.SelectedItem as ComboBoxItem)?.Tag is not Asset selected) return;
            using var prepared = ZipContentPicker.Show(owner, app, selected, client);
            zipSelection.Text = ZipContentPicker.Label(app);
        }); chooseContents.IsEnabled = client != null; zipPanel.Children.Add(chooseContents);
        zipPanel.Children.Add(Ui.Button("Use entire ZIP", (_, _) => { app.ZipSubfolder = ""; app.ZipSourceIsFile = false; zipSelection.Text = ZipContentPicker.Label(app); }));
        zipPanel.Children.Add(Ui.Text("Choose the whole archive, a folder’s contents or one file. Future updates use that same selection.", muted: true)); Panel.Children.Add(zipPanel);
        Panel.Children.Add(Ui.Text("Release download", 18)); assets = new ComboBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 14) }; Name(assets, "Release download");
        var files = AssetCompatibility.Candidates(app.Release?.Assets ?? [], app.ShowAllDownloads);
        foreach (var asset in files) assets.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = ReleaseFiles.FileLabel(asset), Foreground = System.Windows.Media.Brushes.Black }, Tag = asset });
        var match = ReleaseFiles.Match(app); assets.SelectedIndex = match != null ? files.IndexOf(match) : files.Count == 1 ? 0 : -1; Panel.Children.Add(assets);
        var allDownloads = Ui.Check("Show all downloads (including other operating systems)", app.ShowAllDownloads); Panel.Children.Add(allDownloads);
        allDownloads.Click += (_, _) =>
        {
            var selected = (assets.SelectedItem as ComboBoxItem)?.Tag as Asset; app.ShowAllDownloads = allDownloads.IsChecked == true;
            files = AssetCompatibility.Candidates(app.Release?.Assets ?? [], app.ShowAllDownloads); assets.Items.Clear();
            foreach (var asset in files) assets.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = ReleaseFiles.FileLabel(asset), Foreground = System.Windows.Media.Brushes.Black }, Tag = asset });
            assets.SelectedIndex = selected != null && files.Contains(selected) ? files.IndexOf(selected) : files.Count == 1 ? 0 : -1;
        };
        action = Ui.Text("", muted: true); Panel.Children.Add(action);
        Panel.Children.Add(Ui.Text("Installed version", 18)); detected = Ui.Text("", muted: true); Panel.Children.Add(detected);
        Expander? advancedSection = null;
        var versionActions = new WrapPanel(); versionActions.Children.Add(Ui.Button("Check version", (_, _) => CheckVersion(true)));
        versionActions.Children.Add(Ui.Button("Enter version manually", (_, _) => { automatic!.IsChecked = false; Refresh(); advancedSection!.IsExpanded = true; _ = owner.Dispatcher.InvokeAsync(() => { manual!.BringIntoView(); manual.Focus(); }); })); Panel.Children.Add(versionActions);
        var advanced = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        automatic = Ui.Check("Read installed version automatically", app.AutoReconcile); advanced.Children.Add(automatic);
        manualPanel = new StackPanel(); manualPanel.Children.Add(Ui.Text("Installed version", muted: true)); manual = Ui.Input(app.Installed); Name(manual, "Manual installed version"); manualPanel.Children.Add(manual); advanced.Children.Add(manualPanel);
        sourceLabel = Ui.Text("", muted: true); advanced.Children.Add(sourceLabel);
        advanced.Children.Add(Ui.Button("Choose version file…", (_, _) => { var picker = new OpenFileDialog { Title = "Choose a version source", Filter = "Version sources|*.exe;*.json" }; if (picker.ShowDialog(owner) == true) { draft.SourceOverride = string.Equals(Framework.GetDirectoryName(picker.FileName), folder.Text, StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(picker.FileName) : picker.FileName; automatic.IsChecked = true; CheckVersion(); } }));
        advanced.Children.Add(Ui.Button("Use app location for version", (_, _) => { draft.SourceOverride = ""; CheckVersion(); }));
        previews = Ui.Check("Include preview releases", app.IncludePrereleases); advanced.Children.Add(previews);
        if (app.Repo.Length > 0 && Repositories.Resolve(app.Repo).Host == "nexusmods.com") previews.Visibility = Visibility.Collapsed;
        advancedSection = new Expander { Header = "Advanced settings", Foreground = System.Windows.Media.Brushes.White, Content = advanced, Margin = new Thickness(0, 14, 0, 0) }; Panel.Children.Add(advancedSection);
        folder.TextChanged += (_, _) => Refresh(); file.TextChanged += (_, _) => Refresh(); method.SelectionChanged += (_, _) => Refresh(); automatic.Click += (_, _) => CheckVersion();
        Refresh(); if (app.AutoReconcile) CheckVersion();
    }
    private string Method => Methods[Math.Max(0, method.SelectedIndex)];
    private void ReadLocation() { draft.Folder = folder.Text.Trim(); draft.FileName = file.Text.Trim(); }
    private void Refresh()
    {
        zipPanel.Visibility = Method == "Zip" ? Visibility.Visible : Visibility.Collapsed;
        ReadLocation(); filePanel.Visibility = Method == "ReplaceExe" || draft.FileName.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!customName)
        {
            updatingName = true;
            try { appName.Text = AppNames.Suggest(new TrackedApp { Repo = app.Repo, AppFolder = draft.Folder, AppFile = draft.FileName, InstallLocation = draft.Folder }); }
            finally { updatingName = false; }
        }
        manualPanel.Visibility = automatic.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        sourceLabel.Text = "Version source: " + (draft.VersionSource.Length > 0 ? draft.VersionSource : "App location (not set)");
        detected.Text = automatic.IsChecked == true ? checkedSource == draft.VersionSource && checkedSource.Length > 0 ? checkedVersion + " · Automatic detection\nRead from " + Path.GetFileName(checkedFile) : "Not checked for this location · Automatic detection" : (manual.Text.Length > 0 ? manual.Text : "Unknown") + " · Manual tracking";
        try
        {
            action.Text = Method switch
            {
                "ReplaceExe" => "Updates will replace:\n" + SetupPaths.PortableExe(draft.Folder, draft.FileName) + "",
                "Zip" => "ZIP updates will be extracted into:\n" + draft.Folder + "\nFiles included in the ZIP overwrite existing files.",
                "Installer" => "The installer controls its destination. App folder identifies the installation to check afterward.",
                _ => "Download the update and open its folder. Apply the update yourself."
            };
            if (draft.SourceOverride.Length > 0) action.Text += "\nVersion detection uses: " + draft.VersionSource;
        }
        catch (Exception ex) { action.Text = ex.Message; }
    }
    private void CheckVersion(bool force = false)
    {
        Refresh();
        if (automatic.IsChecked != true && !force) return;
        try { var found = InstalledVersions.ReadForApp(app, draft.VersionSource); checkedSource = draft.VersionSource; checkedVersion = found.Version; checkedFile = found.Source; app.Installed = found.Version; if (automatic.IsChecked != true) app.InstalledProvenance = "Version checked from " + found.Source; manual.Text = found.Version; detected.Text = found.Version + (automatic.IsChecked == true ? " · Automatic detection" : " · Checked now (manual tracking)") + "\nRead from " + Path.GetFileName(found.Source); }
        catch (Exception ex) { detected.Text = "Installed version unverified\n" + ex.Message; }
    }
    public void Save(bool validate = true)
    {
        var source = RepositoryKey;
        var websiteSeed = websiteDownload?.Text.Trim() ?? app.WebsiteDownloadUrl;
        if (source.Length > 0 && Repositories.Resolve(source).IsWebsite && validate) WebsiteSource.Url(websiteSeed);
        if (source.Length > 0 && Repositories.Resolve(source).IsWebsite) app.WebsiteDownloadUrl = websiteSeed; else app.WebsiteDownloadUrl = "";
        var sourceChanged = !source.Equals(app.Repo, StringComparison.OrdinalIgnoreCase);
        app.CustomName = customName ? appName.Text.Trim() : "";
        if (!customName) app.InvalidateSuggestedName();
        if (!validate) { CaptureDraft(); return; }
        var previousLocation = app.InstallLocation;
        ReadLocation(); draft.Apply(app, Method);
        if (!string.Equals(previousLocation, app.InstallLocation, StringComparison.OrdinalIgnoreCase) && app.VersionRecordError.Length > 0) { app.VersionRecord = null; app.VersionRecordError = ""; }
        if (Method == "Zip") app.ZipSubfolder = FileUpdates.NormalizeZipSubfolder(app.ZipSubfolder);
        app.AutoReconcile = automatic.IsChecked == true; app.IncludePrereleases = previews.IsChecked == true;
        if (app.Installed != manual.Text.Trim()) app.InstalledProvenance = "Manually entered installed version";
        app.Installed = manual.Text.Trim();
        if (!app.AutoReconcile && app.VersionRecord is { } record) app.VersionRecord = record with { Version = app.Installed, Evidence = "Manual" };
        if (assets.SelectedItem is ComboBoxItem { Tag: Asset asset }) { ReleaseFiles.Select(app, asset); if (validate && !sourceChanged) ReleaseFiles.Validate(app, asset); }
        else if (validate && !sourceChanged && Method != "Manual") throw new InvalidOperationException("Choose a release download or use Download only.");
        InstalledVersions.Reconcile(app);
        if (sourceChanged)
        {
            app.VersionRecord = null; app.VersionRecordError = ""; app.PendingInstallerRelease = "";
            app.Repo = source; app.Release = null; app.Latest = ""; app.CheckedAt = null; app.Error = "";
            app.AssetName = ""; app.NexusFileId = 0; app.D2RManagedFolder = ""; app.LastNotifiedTag = ""; app.DownloadedPath = ""; app.DownloadedTag = "";
            app.UpdateState = "Repository changed — checking the new source";
        }
    }
    public void CaptureDraft()
    {

        app.CustomName = customName ? appName.Text.Trim() : "";
        if (!customName) app.InvalidateSuggestedName();
        ReadLocation(); app.AppFolder = draft.Folder; app.AppFile = draft.FileName; app.VersionSourceOverride = draft.SourceOverride;
        app.PendingLocationConflict = draft.Conflict; app.UpdateMethod = Method;
        if (app.Installed != manual.Text.Trim()) app.InstalledProvenance = "Manually entered installed version";
        app.Installed = manual.Text.Trim(); app.AutoReconcile = automatic.IsChecked == true; app.IncludePrereleases = previews.IsChecked == true;
        if (!draft.Conflict)
        {
            app.InstallLocation = draft.SourceOverride.Length > 0 ? Framework.IsPathFullyQualified(draft.SourceOverride) ? draft.SourceOverride : draft.Folder + Path.DirectorySeparatorChar + draft.SourceOverride : draft.FileName.Length > 0 ? draft.Folder + Path.DirectorySeparatorChar + draft.FileName : draft.Folder;
            app.UpdateTarget = Method == "ReplaceExe" ? draft.Folder + Path.DirectorySeparatorChar + draft.FileName : Method == "Zip" ? draft.Folder : "";
        }
        if (assets.SelectedItem is ComboBoxItem { Tag: Asset asset }) ReleaseFiles.Select(app, asset);
    }
}
