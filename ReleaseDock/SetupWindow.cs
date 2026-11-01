using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ReleaseDock;

public sealed class SetupWindow : Window
{
    private readonly ReleaseClient github;
    private readonly HashSet<string> existing;
    private readonly StackPanel body;
    private readonly bool editing;
    private readonly UpdatePrompts? updatePrompts;
    private readonly Func<string, string>? newWork;
    private readonly InstallDiscovery installDiscovery;
    private bool installerCompletion;
    private bool working;
    private bool newInstall;
    private bool installAttempted;
    private bool methodChosen;
    private bool firstScreen, closed;
    private bool completed;
    private CancellationTokenSource? operation;
    private string verifiedRepo = "", lookedUpRepo = "";
    private bool lookedUpChannel;
    private readonly System.Windows.Threading.DispatcherTimer lookupTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private Button? nextButton;
    private TextBlock projectInfo = Ui.Text("");
    private StackPanel destinationPanel = new();
    private readonly TextBox repo = Ui.Input();
    private readonly CheckBox previews = Ui.Check("Include preview releases");
    private readonly RadioButton installed = new() { Content = "Track an app already installed", IsChecked = true, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 12) };
    private readonly RadioButton fresh = new() { Content = "Install this app", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 12) };
    private TextBox location = Ui.Input(), version = Ui.Input(), target = Ui.Input();
    private CheckBox automatic = Ui.Check("Automatically reconcile the installed version");
    private ComboBox assets = new(), method = new();
    private TextBlock detection = Ui.Text(""), summary = Ui.Text(""), message = Ui.Text("");
    public TrackedApp Result { get; private set; } = new();
    public string ExistingRepo { get; private set; } = "";
    private static readonly string[] Methods = ["Manual", "ReplaceExe", "Installer", "Zip"];

    public SetupWindow(ReleaseClient client, IEnumerable<string> tracked, TrackedApp? edit = null, bool preview = false, string previewStage = "existing", UpdatePrompts? updatePrompts = null, Func<string, string>? newWork = null, InstallDiscovery? discovery = null)
    {
        installDiscovery = discovery ?? new();
        this.updatePrompts = updatePrompts; this.newWork = newWork;
        github = client; existing = tracked.ToHashSet(StringComparer.OrdinalIgnoreCase); editing = edit != null;
        Title = editing ? "App settings" : "Add App"; Width = 740; Height = 840; MinWidth = 600; MinHeight = 650;
        Ui.ConstrainToScreen(this);
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(12, 20, 34));
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 14; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        body = Ui.Panel(this);
        System.Windows.Automation.AutomationProperties.SetName(repo, "Project link");
        Closing += (_, e) =>
        {
            if (working && !firstScreen) { e.Cancel = true; message.Text = "Wait for the current operation to finish. Your setup will stay here."; }
            else if (!completed && installAttempted && !(updatePrompts ?? new()).LeaveSetup(this)) e.Cancel = true;
        };
        Closed += (_, _) => { closed = true; lookupTimer.Stop(); };
        lookupTimer.Tick += async (_, _) => { lookupTimer.Stop(); if (firstScreen && !working && !closed) await Lookup(); };
        repo.TextChanged += (_, _) => { if (firstScreen) { lookupTimer.Stop(); lookupTimer.Start(); } };
        previews.Click += (_, _) => { if (firstScreen) { lookupTimer.Stop(); lookupTimer.Start(); } };
        if (edit != null)
        {
            Result = JsonSerializer.Deserialize<TrackedApp>(JsonSerializer.Serialize(edit))!; Result.Release = edit.Release; Result.Error = edit.Error;
            ScreenTwo();
        }
        else if (preview)
        {
            Result = new() { Repo = "example/desktop-app", Installed = "v1.0.0", Latest = "v1.1.0", InstallLocation = @"C:\Apps\DesktopApp", UpdateTarget = @"C:\Apps\DesktopApp", UpdateMethod = "Installer", Release = new("v1.1.0", "", "", [new("DesktopApp-1.1.0-x64.exe", "", 100000, null)]) };
            if (previewStage == "first") { ScreenOne(); lookupTimer.Stop(); }
            else { newInstall = previewStage is "install" or "portable"; if (newInstall) { SetupPaths.ClearExistingTracking(Result); Result.UpdateTarget = @"C:\Apps"; if (previewStage == "portable") Result.UpdateMethod = "ReplaceExe"; } ScreenTwo(); }
        }
        else ScreenOne();
    }
    private void ScreenOne()
    {
        firstScreen = true;
        BeginPage();
        body.Children.Add(Ui.Text("ADD APP · 1 OF 2", 12, true));
        body.Children.Add(Ui.Text("Find your app", 28));
        body.Children.Add(Ui.Text("Paste a GitHub, Codeberg or GitLab.com project or release link.", muted: true));
        repo.Text = Result.Repo; previews.IsChecked = Result.IncludePrereleases;
        body.Children.Add(Ui.Text("Project link")); body.Children.Add(repo); body.Children.Add(previews);
        projectInfo = Ui.Text("The project and latest release will appear here.", muted: true); body.Children.Add(projectInfo);
        body.Children.Add(Ui.Text("What would you like to do?", 18));
        installed.IsChecked = !newInstall; fresh.IsChecked = newInstall;
        body.Children.Add(installed); body.Children.Add(fresh);
        message = Ui.Text("Preview-only projects need Include preview releases enabled. Existing apps can be tracked even without a usable Windows download.", muted: true); body.Children.Add(message);
        var row = new WrapPanel();
        row.Children.Add(Ui.Button("Cancel", (_, _) => Close()));
        nextButton = Ui.Button("Next →", async (_, _) =>
        {
            if (working) return;
            lookupTimer.Stop();
            try
            {
                var parsed = Repositories.Parse(repo.Text);
                if (existing.Contains(parsed)) { ExistingRepo = parsed; DialogResult = true; return; }
                if (lookedUpRepo != parsed || lookedUpChannel != (previews.IsChecked == true)) await Lookup();
                if (closed || verifiedRepo != parsed || Repositories.Parse(repo.Text) != parsed) return;
                var wantsNew = fresh.IsChecked == true;
                if (wantsNew && !newInstall) { SetupPaths.ClearExistingTracking(Result); methodChosen = false; installerCompletion = false; }
                newInstall = wantsNew;
                if (newInstall && Result.Release == null) { message.Text = "Choose a channel with a release before installing, or select Track an app already installed."; return; }
                ScreenTwo();
            }
            catch (Exception ex) { message.Text = ex.Message; }
        });
        row.Children.Add(nextButton); body.Children.Add(row);
        if (repo.Text.Length > 0) { lookupTimer.Stop(); lookupTimer.Start(); }
    }
    private async Task Lookup()
    {
        if (working || closed) return;
        working = true;
        if (nextButton != null) nextButton.IsEnabled = false;
        var input = repo.Text; var channel = previews.IsChecked == true;
        try
        {
            var parsed = Repositories.Parse(input);
            if (existing.Contains(parsed)) { projectInfo.Text = "Already in your library. Choose Open app."; nextButton!.Content = "Open app"; return; }
            nextButton!.Content = "Next →";
            projectInfo.Text = "Finding app…";
            if (verifiedRepo != parsed) { verifiedRepo = ""; await github.VerifyRepository(parsed); verifiedRepo = parsed; }
            if (Result.Repo.Length > 0 && !Result.Repo.Equals(parsed, StringComparison.OrdinalIgnoreCase)) { Result = new(); installAttempted = false; methodChosen = false; installerCompletion = false; }
            Result.Repo = parsed; Result.IncludePrereleases = channel; Result.Release = null; Result.Latest = "";
            try
            {
                var release = await github.Latest(parsed, channel);
                Result.Release = release; Result.Latest = release.Tag; Result.CheckedAt = DateTimeOffset.UtcNow; Result.Error = "";
                var source = Repositories.Resolve(parsed);
                projectInfo.Text = $"{source.Name} · {source.Path}\nLatest: {release.Tag}\n{AssetCompatibility.Candidates(release.Assets).Count} Windows candidate download(s)" + (source.Host == "gitlab.com" ? "\nGitLab preview releases are identified by tags such as beta, rc or preview." : "");
            }
            catch (Exception ex) { Result.Error = ex.Message; projectInfo.Text = parsed + "\n" + ex.Message + "\nYou can still track an existing installation manually."; }
            lookedUpRepo = parsed; lookedUpChannel = channel;
        }
        catch (Exception ex) { projectInfo.Text = ex.Message; lookedUpRepo = ""; }
        finally
        {
            working = false;
            if (nextButton != null) nextButton.IsEnabled = true;
            if (!closed && firstScreen && (input != repo.Text || channel != (previews.IsChecked == true))) { lookedUpRepo = ""; lookupTimer.Start(); }
        }
    }
    private void ScreenTwo()
    {
        firstScreen = false; lookupTimer.Stop();
        if (newInstall) { if (installerCompletion) InstallerCompletion(); else NewInstallScreen(); return; }
        BeginPage();
        body.Children.Add(Ui.Text(editing ? "APP SETTINGS" : "ADD APP · 2 OF 2", 12, true));
        body.Children.Add(Ui.Text(Result.ProjectLabel, 26));
        body.Children.Add(Ui.Text("Latest release: " + (Result.Latest.Length > 0 ? Result.Latest : "unavailable"), muted: true));
        var editor = new AppSettingsPage(this, Result, allowRepositoryEdit: editing); body.Children.Add(editor.Panel);
        message = Ui.Text("", muted: true);
        var row = new WrapPanel();
        row.Children.Add(Ui.Button(editing ? "Cancel" : "← Back", (_, _) =>
        {
            try { if (editing) Close(); else { editor.Save(false); ScreenOne(); } }
            catch (Exception ex) { message.Text = ex.Message; }
        }));
        row.Children.Add(Ui.Button(editing ? "Save settings" : "Add App", async (_, _) =>
        {
            if (working) return;
            working = true; row.IsEnabled = false;
            try
            {
                var source = editor.RepositoryKey;
                if (editing && !source.Equals(Result.Repo, StringComparison.OrdinalIgnoreCase))
                {
                    if (existing.Contains(source)) throw new InvalidOperationException("That repository is already tracked. Choose its existing library entry instead.");
                    message.Text = "Checking the new repository…"; await github.VerifyRepository(source);
                }
                editor.Save(); working = false; completed = true; DialogResult = true;
            }
            catch (Exception ex) { message.Text = ex.Message; }
            finally { working = false; row.IsEnabled = true; }
        }));
        FixedFooter(row);
    }
    private void BeginPage()
    {
        if (body.Parent is ScrollViewer old) old.Content = null;
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        body.Children.Clear();
    }
    private void FixedFooter(WrapPanel actions)
    {
        if (body.Parent is ScrollViewer old) old.Content = null;
        var shell = new Grid(); shell.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; shell.Children.Add(scroll);
        var footer = new StackPanel { Margin = new Thickness(28, 0, 28, 24) }; footer.Children.Add(message); footer.Children.Add(actions); Grid.SetRow(footer, 1); shell.Children.Add(footer); Content = shell;
    }
    private void NewInstallScreen()
    {
        BeginPage();
        body.Children.Add(Ui.Text("ADD APP · 2 OF 2", 12, true));
        body.Children.Add(Ui.Text("Install " + Result.ProjectLabel, 26));
        body.Children.Add(Ui.Text("Release " + Result.Latest, muted: true));
        body.Children.Add(Ui.Text("Download", 18));
        var files = AssetCompatibility.Candidates(Result.Release?.Assets ?? [], Result.ShowAllDownloads);
        assets = new ComboBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 16) };
        System.Windows.Automation.AutomationProperties.SetName(assets, "Release download");
        foreach (var asset in files) assets.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = $"{asset.Name} · {ReleaseFiles.SizeText(asset)}", Foreground = System.Windows.Media.Brushes.Black }, Tag = asset });
        var matched = ReleaseFiles.Match(Result);
        assets.SelectedIndex = matched != null ? files.IndexOf(matched) : files.Count == 1 ? 0 : -1;
        body.Children.Add(assets);
        var allDownloads = Ui.Check("Show all downloads (including other operating systems)", Result.ShowAllDownloads); body.Children.Add(allDownloads);
        var configuration = new StackPanel(); body.Children.Add(configuration);
        message = Ui.Text("", muted: true);
        var actions = new WrapPanel();
        var back = Ui.Button("← Back", (_, _) => { if (!working) ScreenOne(); }); actions.Children.Add(back);
        var finish = Ui.Button("Choose a download", (_, _) => { }); actions.Children.Add(finish);
        finish.IsDefault = true;
        var cancel = Ui.Button("Cancel download", (_, _) => operation?.Cancel()); cancel.Visibility = Visibility.Collapsed; actions.Children.Add(cancel);
        FixedFooter(actions);
        ComboBox? use = null; TextBox? folder = null, filename = null;
        string selectedMethod = "";
        string destinationFolder = Directory.Exists(Result.UpdateTarget) || !Result.UpdateTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Result.UpdateTarget : Path.GetDirectoryName(Result.UpdateTarget) ?? "";
        if (destinationFolder.Length == 0) destinationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "apps");
        if (Result.UpdateMethod == "Zip" && Result.UpdateTarget.Length > 0 && Path.GetDirectoryName(Result.UpdateTarget) is { Length: > 0 } zipParent && string.Equals(SetupPaths.PortableZip(zipParent, Result.AppName), Result.UpdateTarget, StringComparison.OrdinalIgnoreCase)) destinationFolder = zipParent;
        string chosenFilename = Result.UpdateTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(Result.UpdateTarget) : "";
        void Configure()
        {
            configuration.Children.Clear(); message.Text = "";
            if ((assets.SelectedItem as ComboBoxItem)?.Tag is not Asset asset)
            {
                finish.IsEnabled = false; finish.Content = "Choose a download";
                if (files.Count == 0)
                {
                    configuration.Children.Add(Ui.Text("No matching Windows downloads were found. Use Show all downloads to review unusual filenames, open the releases page, or go Back to change the channel.", muted: true));
                    configuration.Children.Add(Ui.Button("Open releases", (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Repositories.Resolve(Result.Repo).ReleasesUrl) { UseShellExecute = true }); } catch (Exception ex) { message.Text = ex.Message; } }));
                }
                return;
            }
            Result.AssetName = asset.Name;
            var ext = Path.GetExtension(asset.Name).ToLowerInvariant();
            var keys = ext == ".exe" ? new[] { "", "ReplaceExe", "Installer" } : ext == ".zip" ? new[] { "Zip" } : ext == ".msi" ? new[] { "Installer" } : new[] { "Manual" };
            var labels = ext == ".exe" ? new[] { "Choose how to use this EXE…", "Save as a portable app", "Run as an installer" } : ext == ".zip" ? new[] { "Extract into a folder" } : ext == ".msi" ? new[] { "Run the installer" } : new[] { "Download for manual installation" };
            configuration.Children.Add(Ui.Text(ext == ".exe" ? "How should this EXE be used?" : "Install method", 18));
            use = Ui.Choice(labels, Math.Max(0, Array.IndexOf(keys, Result.UpdateMethod))); configuration.Children.Add(use);
            System.Windows.Automation.AutomationProperties.SetName(use, "Install method");
            var destination = new StackPanel(); configuration.Children.Add(destination);
            void ChooseMethod()
            {
                selectedMethod = keys[use.SelectedIndex]; Result.UpdateMethod = selectedMethod;
                destination.Children.Clear(); folder = null; filename = null; message.Text = "";
                if (selectedMethod is "ReplaceExe" or "Zip")
                {
                    destination.Children.Add(Ui.Text(selectedMethod == "ReplaceExe" ? "Save in folder" : "Create app folder inside", 18));
                    folder = Ui.Input(destinationFolder); System.Windows.Automation.AutomationProperties.SetName(folder, "Save in folder"); destination.Children.Add(folder);
                    destination.Children.Add(Ui.Button("Browse…", (_, _) => { var picker = new OpenFolderDialog { Title = "Choose where to save the app" }; if (picker.ShowDialog(this) == true) folder.Text = picker.FolderName; }));
                    if (selectedMethod == "ReplaceExe")
                    {
                        destination.Children.Add(Ui.Text("Filename", muted: true)); filename = Ui.Input(chosenFilename.Length > 0 ? chosenFilename : asset.Name); System.Windows.Automation.AutomationProperties.SetName(filename, "Filename"); destination.Children.Add(filename);
                    }
                    var path = Ui.Text("", muted: true); destination.Children.Add(path);
                    void Validate()
                    {
                        try
                        {
                            destinationFolder = folder.Text; chosenFilename = filename?.Text ?? "";
                            var finalPath = selectedMethod == "ReplaceExe" ? SetupPaths.PortableExe(folder.Text, filename!.Text) : SetupPaths.PortableZip(folder.Text, Result.AppName);
                            Result.UpdateTarget = finalPath;
                            ReleaseFiles.Validate(Result, asset);
                            path.Text = (selectedMethod == "ReplaceExe" ? "Will save:\n" : "Will extract into:\n") + finalPath; message.Text = ""; finish.IsEnabled = !working;
                        }
                        catch (Exception ex) { Result.UpdateTarget = ""; path.Text = ""; message.Text = ex.Message; finish.IsEnabled = false; }
                    }
                    folder.TextChanged += (_, _) => Validate(); if (filename != null) filename.TextChanged += (_, _) => Validate(); Validate();
                    finish.Content = selectedMethod == "ReplaceExe" ? "Save and add app" : "Extract and add app";
                    destination.Children.Add(Ui.Text(selectedMethod == "ReplaceExe" ? "The EXE will be saved here without being run. Future updates replace this same file." : "GROUT will create a subfolder named after the app. Future updates use that app folder. Files included in the ZIP overwrite existing files.", muted: true));
                }
                else
                {
                    finish.IsEnabled = selectedMethod.Length > 0 && !working;
                    finish.Content = selectedMethod == "Installer" ? "Download and run installer" : selectedMethod == "Manual" ? "Download" : "Choose how to use this EXE";
                    destination.Children.Add(Ui.Text(selectedMethod == "Installer" ? "The installer will ask where to install the app. Afterward, you can choose its installed location for tracking." : "", muted: true));
                }
            }
            use.SelectionChanged += (_, _) => ChooseMethod(); ChooseMethod();
        }
        assets.SelectionChanged += (_, _) => { chosenFilename = ""; Configure(); }; Configure();
        allDownloads.Click += (_, _) =>
        {
            var chosen = (assets.SelectedItem as ComboBoxItem)?.Tag as Asset;
            Result.ShowAllDownloads = allDownloads.IsChecked == true;
            files = AssetCompatibility.Candidates(Result.Release?.Assets ?? [], Result.ShowAllDownloads);
            assets.Items.Clear();
            foreach (var asset in files) assets.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = $"{asset.Name} · {ReleaseFiles.SizeText(asset)}", Foreground = System.Windows.Media.Brushes.Black }, Tag = asset });
            assets.SelectedIndex = chosen != null && files.Contains(chosen) ? files.IndexOf(chosen) : files.Count == 1 ? 0 : -1;
            Configure();
        };
        finish.Click += async (_, _) =>
        {
            if (working || selectedMethod.Length == 0 || (assets.SelectedItem as ComboBoxItem)?.Tag is not Asset asset) return;
            working = true; finish.IsEnabled = false; back.IsEnabled = false; assets.IsEnabled = false; configuration.IsEnabled = false;
            operation = new(); cancel.Visibility = Visibility.Visible;
            try
            {
                Result.UpdateMethod = selectedMethod; Result.AssetName = asset.Name;
                var outcome = await UpdateRunner.Run(this, Result, asset, github, (text, progress) => { message.Text = text + (progress < 100 ? $" {progress:F0}%" : ""); cancel.IsEnabled = text.StartsWith("Downloading") || text.StartsWith("Inspecting ZIP"); }, () => { }, operation.Token, newInstall: true, prompts: updatePrompts, newWork: newWork, discovery: installDiscovery);
                installAttempted = Result.DownloadedPath.Length > 0;
                if (outcome == UpdateOutcome.Deferred) { message.Text = "Nothing was installed or added. Your download is ready; you can retry."; return; }
                if (outcome == UpdateOutcome.FilesApplied)
                {
                    if (selectedMethod == "Zip") { Result.InstallLocation = Result.UpdateTarget; Result.AutoReconcile = true; InstalledVersions.Reconcile(Result); }
                    working = false; completed = true; DialogResult = true; return;
                }
                if (outcome == UpdateOutcome.InstallerReturned && InstallDiscovery.CanFinish(Result))
                {
                    working = false; completed = true; DialogResult = true; return;
                }
                installerCompletion = true; working = false; InstallerCompletion();
            }
            catch (OperationCanceledException) { message.Text = "Download cancelled. Nothing was added. Retry when ready."; }
            catch (Exception ex) { message.Text = ex.Message; }
            finally { operation?.Dispose(); operation = null; working = false; finish.IsEnabled = true; back.IsEnabled = true; assets.IsEnabled = true; configuration.IsEnabled = true; cancel.Visibility = Visibility.Collapsed; }
        };
    }
    private void InstallerCompletion()
    {
        BeginPage();
        body.Children.Add(Ui.Text("ADD APP · 2 OF 2", 12, true));
        body.Children.Add(Ui.Text(Result.UpdateMethod == "Installer" ? "Finish tracking your installation" : "Download saved", 26));
        if (Result.UpdateMethod == "Installer")
        {
            var discoveryInfo = Ui.Text(Result.InstallDiscoveryNote, muted: true); body.Children.Add(discoveryInfo);
            var candidates = new StackPanel(); body.Children.Add(candidates);
            void ShowCandidates()
            {
                candidates.Children.Clear();
                foreach (var candidate in Result.InstallCandidates)
                    candidates.Children.Add(Ui.Button("Use " + candidate.Label, (_, _) => { InstallDiscovery.Apply(Result, candidate); completed = true; DialogResult = true; }));
            }
            ShowCandidates();
            body.Children.Add(Ui.Button("Find installation", async (_, _) =>
            {
                if (working) return; working = true; discoveryInfo.Text = "Looking in Windows installed apps…";
                try
                {
                    Result.InstallCandidates = await Task.Run(() => installDiscovery.Find(Result, Result.InstallationBefore));
                    if (InstallDiscovery.ClearMatch(Result.InstallCandidates) is { } match)
                    {
                        InstallDiscovery.Apply(Result, match);
                        if (InstallDiscovery.CanFinish(Result)) { working = false; completed = true; DialogResult = true; return; }
                    }
                    discoveryInfo.Text = Result.InstallCandidates.Count > 0 ? "Choose the matching installation below." : "No matching registered installation found. Choose its folder or EXE below."; ShowCandidates();
                }
                catch (Exception ex) { discoveryInfo.Text = ex.Message; }
                finally { working = false; }
            }));
        }
        body.Children.Add(Ui.Text(Result.UpdateMethod == "Installer" ? "Choose where the app was installed, or leave the version unknown and set it later. Installer exit does not confirm installation." : "Install or copy the downloaded files yourself, then choose their location for tracking. You can also leave the version unknown and set it later.", muted: true));
        location = Ui.Input(Result.InstallLocation); version = Ui.Input(Result.Installed); automatic = Ui.Check("Detect installed version automatically", Result.AutoReconcile); detection = Ui.Text("", muted: true);
        body.Children.Add(Ui.Text("Installed folder or EXE (optional)")); body.Children.Add(location);
        var picks = new WrapPanel(); picks.Children.Add(Ui.Button("Folder…", (_, _) => PickLocation(false))); picks.Children.Add(Ui.Button("File…", (_, _) => PickLocation(true))); picks.Children.Add(Ui.Button("Check version", (_, _) => Detect())); body.Children.Add(picks); body.Children.Add(detection);
        body.Children.Add(automatic); body.Children.Add(Ui.Text("Installed version (optional)")); body.Children.Add(version);
        version.TextChanged += (_, _) => { if (version.IsKeyboardFocusWithin) automatic.IsChecked = false; };
        message = Ui.Text("", muted: true);
        var row = new WrapPanel(); row.Children.Add(Ui.Button("← Back", (_, _) => { installerCompletion = false; NewInstallScreen(); }));
        row.Children.Add(Ui.Button("Add to library", (_, _) => { Result.InstallLocation = location.Text.Trim(); Result.Installed = version.Text.Trim(); Result.AutoReconcile = automatic.IsChecked == true; if (Result.AutoReconcile) InstalledVersions.Reconcile(Result); completed = true; DialogResult = true; }));
        FixedFooter(row);
    }
    private void PickLocation(bool file)
    {
        if (file) { var picker = new OpenFileDialog { Filter = "Version sources|*.exe;*.json|All files|*.*" }; if (picker.ShowDialog(this) != true) return; location.Text = picker.FileName; }
        else { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) != true) return; location.Text = picker.FolderName; }
        Detect();
        if (newInstall) return;
        if (string.IsNullOrWhiteSpace(target.Text)) target.Text = location.Text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Directory.Exists(location.Text) ? location.Text : "";
        SuggestMethod();
    }
    private void Detect()
    {
        try { var found = InstalledVersions.Read(location.Text); version.Text = found.Version; detection.Text = $"Detected {found.Version}\nRead from {found.Source}"; automatic.IsChecked = true; }
        catch (Exception ex) { detection.Text = ex.Message + " Enter a version manually or choose another source."; automatic.IsChecked = false; }
    }
    private void ReadForm()
    {
        if (Result.Installed != version.Text.Trim()) Result.InstalledProvenance = "Manually entered installed version";
        Result.InstallLocation = location.Text.Trim().Trim('"'); Result.Installed = version.Text.Trim(); Result.AutoReconcile = automatic.IsChecked == true;
        Result.UpdateTarget = target.Text.Trim().Trim('"'); Result.UpdateMethod = Methods[Math.Max(0, method.SelectedIndex)];
        Result.AssetName = ((assets.SelectedItem as ComboBoxItem)?.Tag as Asset)?.Name ?? Result.AssetName;
    }
    private void SuggestMethod()
    {
        if (editing || methodChosen || (assets.SelectedItem as ComboBoxItem)?.Tag is not Asset asset) return;
        var ext = Path.GetExtension(asset.Name).ToLowerInvariant();
        method.SelectedIndex = ext switch { ".zip" => 3, ".msi" => 2, ".exe" when location.Text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) => 1, ".exe" => 2, _ => 0 };
    }
}
