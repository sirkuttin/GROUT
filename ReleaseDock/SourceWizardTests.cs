using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class SourceWizardTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool condition, string reason) { Assertions++; if (!condition) throw new Exception(reason); }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) { if (child is T value) yield return value; foreach (var descendant in Find<T>(child)) yield return descendant; }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var uri = request.RequestUri!;
            var payload = !uri.AbsolutePath.Contains("/releases") ? "{}" : uri.Host == "gitlab.com"
                ? """[{"tag_name":"v2.0","released_at":"2026-10-01T00:00:00Z","description":"GitLab notes","assets":{"links":[{"name":"App.exe","url":"https://downloads.example.org/App.exe"}]}}]"""
                : """{"tag_name":"v2.0","html_url":"https://codeberg.org/owner/app/releases/tag/v2.0","body":"Codeberg notes","assets":[{"name":"App.exe","browser_download_url":"https://codeberg.org/owner/app/releases/download/v2.0/App.exe","size":3}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json") });
        }
    }
    public static async Task Run()
    {
        Assertions = 0;
        var handler = new Handler(); using var client = new ReleaseClient(handler);
        foreach (var project in new[] { "https://codeberg.org/owner/app/releases", "https://gitlab.com/group/subgroup/app/-/releases" })
        {
            var window = new SetupWindow(client, []);
            try
            {
                Find<TextBox>(window).Single().Text = project;
                Find<Button>(window).Single(b => Equals(b.Content, "Next →")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 100 && !Find<Button>(window).Any(b => Equals(b.Content, "Add App")); i++) await Task.Delay(20);
                Check(Find<Button>(window).Any(b => Equals(b.Content, "Add App")), "Source link reaches existing-app setup");
                Check(window.Result.Repo == Repositories.Parse(project) && window.Result.Release?.Tag == "v2.0", "Wizard retains canonical provider identity and release");
                Check(window.Result.Release!.Assets.Single().OriginHost == new Uri(project).Host, "Wizard download provenance belongs to detected source");
                Check(Find<TextBlock>(window).Any(t => t.Text.Contains(Repositories.Resolve(project).Name)), "Source name is visible in setup heading");
                if (project.Contains("gitlab.com")) Check(Find<TextBlock>(window).Any(t => t.Text.Contains("size unknown")), "GitLab unknown size is explained rather than negative");
            }
            finally { window.Close(); }
        }
        var before = handler.Requests;
        var duplicate = new SetupWindow(client, ["https://codeberg.org/owner/app"]) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        try
        {
            Find<TextBox>(duplicate).Single().Text = "https://codeberg.org/OWNER/APP/releases/tag/v2";
            _ = duplicate.Dispatcher.InvokeAsync(() => Find<Button>(duplicate).Single(b => Equals(b.Content, "Next →")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(duplicate.ShowDialog() == true && duplicate.ExistingRepo.Equals("https://codeberg.org/owner/app", StringComparison.OrdinalIgnoreCase), "Duplicate provider link opens existing app even with casing/release URL differences");
            Check(handler.Requests == before, "Duplicate app does not fetch or add a second entry");
        }
        finally { duplicate.Close(); }
    }
}
