using System.Net;
using System.Net.Http;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class WizardTests
{
    public static int Assertions { get; private set; }
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T typed) yield return typed;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }
    private static void Check(bool condition, string message) { Assertions++; if (!condition) throw new Exception(message); }
    private sealed class Handler(bool noReleases = false) : HttpMessageHandler
    {
        public List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!; Requests.Add(uri.PathAndQuery);
            if (!uri.AbsolutePath.Contains("/releases")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            if (noReleases) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var json = """{"tag_name":"v2","html_url":"https://github.com/test/app/releases/tag/v2","body":"notes","draft":false,"published_at":"2026-10-01T00:00:00Z","assets":[{"name":"App-2.0-x64.exe","browser_download_url":"https://github.com/test/app/releases/download/v2/app.exe","size":3}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(uri.Query.Length > 0 ? "[" + json + "]" : json) });
        }
    }
    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Check(condition(), "Wizard navigation did not complete");
    }
    public static async Task Run()
    {
        Assertions = 0;
        var handler = new Handler(); using var github = new ReleaseClient(handler);
        var window = new SetupWindow(github, []);
        try
        {
            var input = Children<TextBox>(window).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link"); input.Text = "https://github.com/test/app/releases";
            var next = Children<Button>(window).Single(b => Equals(b.Content, "Next →"));
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => Children<Button>(window).Any(b => Equals(b.Content, "Add App")));
            Check(window.Result.Repo == "test/app" && window.Result.Latest == "v2", "Wizard resolves URL and release");
            Check(window.Result.Installed == "", "Tracking setup never assumes latest is installed");
            Check(Children<ComboBox>(window).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Release download").SelectedIndex == 0, "Single compatible file selected");
            Check(handler.Requests.Count == 2, "Lookup verifies project and release without duplicate calls");
            var back = Children<Button>(window).Single(b => Equals(b.Content, "← Back")); back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Children<TextBox>(window).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text == "test/app", "Back preserves repository");
            Children<CheckBox>(window).Single().IsChecked = true;
            Children<Button>(window).Single(b => Equals(b.Content, "Next →")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => Children<Button>(window).Any(b => Equals(b.Content, "Add App")));
            Check(handler.Requests.Last().Contains("?per_page=100"), "Preview selection rechecks correct channel");
        }
        finally { window.Close(); }
        using var missing = new ReleaseClient(new Handler(true));
        var manual = new SetupWindow(missing, []);
        try
        {
            Children<TextBox>(manual).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text = "test/app";
            Children<Button>(manual).Single(b => Equals(b.Content, "Next →")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => Children<Button>(manual).Any(b => Equals(b.Content, "Add App")));
            Check(manual.Result.Release == null, "Existing app can continue without a release");
        }
        finally { manual.Close(); }
        var fresh = new SetupWindow(missing, []);
        try
        {
            Children<TextBox>(fresh).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text = "test/app";
            Children<RadioButton>(fresh).Single(b => Equals(b.Content, "Install this app")).IsChecked = true;
            Children<Button>(fresh).Single(b => Equals(b.Content, "Next →")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(40);
            Check(!Children<Button>(fresh).Any(b => Equals(b.Content, "Download and install")), "New install cannot proceed without a release");
        }
        finally { fresh.Close(); }
        var root = Path.Combine(Path.GetTempPath(), "GROUT-tray-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        new Preferences { CheckHours = 0, Quiet = true, TrayExplained = true }.Save(Path.Combine(root, "settings.json"));
        new Store(Path.Combine(root, "apps.json")).Save([new TrackedApp { Repo = "test/app", Installed = "v1", Latest = "v2", AssetName = "app.exe", Release = new("v2", "", "", [new("app.exe", "", 0, null)]) }]);
        var main = new MainWindow(dataDirectory: root) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        try
        {
            main.Show();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(main.TrayVisible, "Native tray icon is active");
            main.Close();
            Check(!main.IsVisible && main.TrayVisible, "Close hides the window and keeps tray active");
            main.Show();
            Check(main.IsVisible, "Hidden main window can reopen");
            Check(Children<Button>(main).Single(b => Equals(b.Content, "Update")).IsEnabled, "Cached checked release enables Update after restart");
            Check(!Children<TextBlock>(main).Any(t => t.Text.Contains("Last successful check:")), "Footer omits polling timestamps");
            var status = (TextBlock)main.FindName("StatusLabel");
            var settings = (Button)main.FindName("SettingsButton");
            status.Text = ""; main.UpdateLayout();
            var footerY = settings.TranslatePoint(new System.Windows.Point(), main).Y;
            foreach (var message in new[] { "App added.", new string('W', 500) })
            {
                status.Text = message; main.UpdateLayout();
                Check(System.Math.Abs(settings.TranslatePoint(new System.Windows.Point(), main).Y - footerY) < 0.1, "Status messages do not move footer controls");
                Check(status.TranslatePoint(new System.Windows.Point(status.ActualWidth, 0), main).X <= settings.TranslatePoint(new System.Windows.Point(), main).X, "Status stays left of footer controls");
            }
            main.StopWindow();
            Check(!main.TrayVisible, "Explicit stop removes tray icon");
        }
        finally { main.StopWindow(); Directory.Delete(root, true); }
    }
}
