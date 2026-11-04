using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class PortableInstallTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T found) yield return found;
            foreach (var nested in Find<T>(child)) yield return nested;
        }
    }
    private static Button Button(Window window, string label) => Find<Button>(window).Single(b => Equals(b.Content, label));
    private static TextBox Input(Window window, string name) => Find<TextBox>(window).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == name);
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Check(condition(), "Portable installation did not reach expected state");
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.Contains("/releases/download/")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(PortableExeFixture.Bytes()) });
            if (!uri.AbsolutePath.Contains("/releases")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"tag_name":"v1.2.12","html_url":"https://github.com/test/portable/releases/tag/v1.2.12","body":"","assets":[{"name":"CircuitLordVRModInstaller.exe","browser_download_url":"https://github.com/test/portable/releases/download/v1.2.12/App.exe","size":1024}]}""") });
        }
    }
    private static async Task StartNew(SetupWindow window)
    {
        Find<TextBox>(window).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text = "test/portable";
        Find<RadioButton>(window).Single(r => Equals(r.Content, "Install this app")).IsChecked = true;
        Button(window, "Next →").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await Until(() => Find<Button>(window).Any(b => Equals(b.Content, "Choose how to use this EXE")));
        Check(!Find<TextBlock>(window).Any(t => t.Text == "Where is it installed?" || t.Text.Contains("manual fallback")), "New installation contains no existing-install fields");
        Check(!Find<CheckBox>(window).Any(c => (c.Content as string)?.Contains("reconcile", StringComparison.OrdinalIgnoreCase) == true), "New portable page has no reconciliation controls");
    }
    private static void ChoosePortable(SetupWindow window, string folder)
    {
        Find<ComboBox>(window).Last().SelectedIndex = 1;
        Input(window, "Save in folder").Text = folder;
    }
    private static bool? FinishDialog(SetupWindow window, string label)
    {
        window.ShowActivated = false; window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000; window.Top = -20000;
        _ = window.Dispatcher.InvokeAsync(() => Button(window, label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
        timer.Start();
        try { return window.ShowDialog(); } finally { timer.Stop(); }
    }
    public static async Task Run()
    {
        Assertions = 0;
        var root = Path.Combine(Path.GetTempPath(), "GROUT-portable-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string Work(string category) { var folder = Path.Combine(root, category, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
        using var github = new ReleaseClient(new Handler());
        try
        {
            var reviewCalls = 0;
            var prompts = new UpdatePrompts { LeaveSetup = _ => true, ReviewFiles = (_, _, _, _) => { reviewCalls++; return true; }, RunInstaller = (_, _) => false };
            var window = new SetupWindow(github, [], updatePrompts: prompts, newWork: Work);
            try
            {
                await StartNew(window);
                ChoosePortable(window, "relative-folder");
                Check(!Button(window, "Save and add app").IsEnabled, "Invalid folder disables Save beside visible validation");
                var folder = Path.Combine(root, "apps"); Input(window, "Save in folder").Text = folder;
                Check(Input(window, "Filename").Text == "CircuitLordVRModInstaller.exe", "Filename comes from release, despite Installer in its name");
                var target = Path.Combine(folder, "CircuitLordVRModInstaller.exe");
                Check(window.Result.UpdateTarget == target && Button(window, "Save and add app").IsEnabled, "Folder derives exact EXE target and enables save");
                Check(FinishDialog(window, "Save and add app") == true, "Actual portable save finishes dialog successfully");
                Check(File.ReadAllBytes(target).SequenceEqual(PortableExeFixture.Bytes()) && reviewCalls == 0, "New EXE is saved without launching or unnecessary overwrite review");
                Check(window.Result.Installed == "v1.2.12" && window.Result.AutoReconcile && window.Result.VersionRecord != null && window.Result.IsCurrent, "EXE without metadata records applied release without false update");
                Check(window.Result.InstalledProvenance.Contains("Saved from GitHub release"), "Applied-release tracking has explicit provenance");
                var store = new Store(Path.Combine(root, "library", "apps.json")); store.Save([window.Result]);
                Check(store.Load().Single().InstallLocation == target && store.Load().Single().IsCurrent, "Completed result adds to persistent library with correct exact file");
            }
            finally { window.Close(); }
            var overwriteReviews = 0;
            var retryPrompts = new UpdatePrompts { LeaveSetup = _ => true, ReviewFiles = (_, _, _, _) => { overwriteReviews++; throw new Exception("Unexpected review screen"); }, RunInstaller = (_, _) => false };
            var retry = new SetupWindow(github, [], updatePrompts: retryPrompts, newWork: Work);
            try
            {
                await StartNew(retry);
                var folder = Path.Combine(root, "retry"); Directory.CreateDirectory(folder);
                var target = Path.Combine(folder, "CircuitLordVRModInstaller.exe"); File.WriteAllText(target, "original");
                ChoosePortable(retry, folder);
                Check(FinishDialog(retry, "Save and add app") == true && File.ReadAllBytes(target).SequenceEqual(PortableExeFixture.Bytes()), "Existing portable EXE is overwritten without a review step");
                Check(overwriteReviews == 0 && retry.Result.LastBackup == "", "Overwrite review never invoked and temporary rollback cleaned");
            }
            finally { retry.Close(); }
            var declined = new SetupWindow(github, [], updatePrompts: prompts, newWork: Work);
            try
            {
                await StartNew(declined);
                Find<ComboBox>(declined).Last().SelectedIndex = 2;
                Button(declined, "Download and run installer").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Until(() => Find<TextBlock>(declined).Any(t => t.Text.Contains("Nothing was installed or added")));
                Check(declined.Result.Installed == "" && !Find<Button>(declined).Any(b => Equals(b.Content, "Add to library")), "Declined installer does not advance to completion");
                Check(!Find<TextBlock>(declined).Any(t => t.Text == "Where is it installed?"), "Declined installer does not show existing-install fields");
            }
            finally { declined.Close(); }
            var intent = new SetupWindow(github, [], updatePrompts: prompts, newWork: Work);
            try
            {
                Find<TextBox>(intent).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text = "test/portable";
                Button(intent, "Next →").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Until(() => Find<Button>(intent).Any(b => Equals(b.Content, "Add App")));
                intent.Result.Installed = "1.0.36.0"; intent.Result.InstallLocation = @"C:\OtherApp.exe";
                // The visible manual field is what Back saves, so simulate an entered prior version there too.
                Input(intent, "Manual installed version").Text = "1.0.36.0";
                Button(intent, "← Back").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Find<RadioButton>(intent).Single(r => Equals(r.Content, "Install this app")).IsChecked = true;
                Button(intent, "Next →").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Until(() => Find<Button>(intent).Any(b => Equals(b.Content, "Choose how to use this EXE")));
                Check(intent.Result.Installed == "" && intent.Result.InstallLocation == "", "Switching to new install clears existing version/location");
            }
            finally { intent.Close(); }
            var changed = new SetupWindow(github, [], updatePrompts: prompts, newWork: Work);
            try
            {
                Find<TextBox>(changed).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text = "test/first";
                Button(changed, "Next →").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Until(() => Find<Button>(changed).Any(b => Equals(b.Content, "Add App")));
                Input(changed, "Manual installed version").Text = "1.0.36.0";
                Button(changed, "← Back").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Find<TextBox>(changed).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text = "test/second";
                Button(changed, "Next →").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Until(() => Find<Button>(changed).Any(b => Equals(b.Content, "Add App")));
                Check(changed.Result.Repo == "test/second" && changed.Result.Installed == "" && Input(changed, "Manual installed version").Text == "", "Changing repository does not carry previous version fields");
            }
            finally { changed.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }
}

