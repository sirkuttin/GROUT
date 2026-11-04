using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class WebsiteSourceTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) { if (child is T value) yield return value; foreach (var value2 in Find<T>(child)) yield return value2; }
    }
    public static async Task Run()
    {
        Assertions = 0;
        const string page = "https://framedropvr.com/", seed = "https://framedropvr.com/downloads/FrameDrop-Setup-1.0.37.exe";
        const string html = """<a href="downloads/FrameDrop-Setup-1.0.37.exe">Download</a><a href='/downloads/FrameDrop-Setup-1.0.38.exe'>New</a><a href=/downloads/FrameDrop-Setup-1.0.38.exe>Duplicate</a><a href='https://evil.example/downloads/FrameDrop-Setup-9.0.0.exe'>Wrong host</a><a href='/downloads/Other-9.0.0.exe'>Wrong app</a>""";
        var key = WebsiteSource.SourceKey(page); var source = Repositories.Resolve(key);
        Check(source.IsWebsite && source.WebUrl == page && source.Key == key && source.ReleasesUrl == page, "Website identity preserves public page URL separately from internal key");
        var found = WebsiteSource.Parse(page, seed, html);
        Check(found.Tag == "1.0.38" && found.Title == "FrameDrop" && found.Assets.Single().Url == "https://framedropvr.com/downloads/FrameDrop-Setup-1.0.38.exe", "Version template matches relative links and newest version, excluding another host/app");
        Check(WebsiteSource.Parse(page, seed, "<a href='downloads/FrameDrop-Setup-1.0.9.exe'>Old</a><a href='downloads/FrameDrop-Setup-1.0.37.exe'>New</a>").Tag == "1.0.37", "Numeric version ordering rather than lexicographic ordering");
        var app = new TrackedApp { Repo = key, WebsiteDownloadUrl = seed, Installed = "1.0.37", Latest = found.Tag, Release = found, AssetName = "FrameDrop-Setup-1.0.37.exe" };
        Check(app.CanUpdate && ReleaseFiles.Match(app)?.Url == found.Assets[0].Url && app.AppName == "FrameDrop", "Existing asset family and update state support changed website filenames");
        var saved = Json.JsonSerializer.Deserialize<TrackedApp>(Json.JsonSerializer.Serialize(app))!;
        Check(saved.WebsiteDownloadUrl == seed && saved.RepositoryUrl == page, "Page and template persist across restarts");
        foreach (var bad in new[] { "http://example.com/", "https://user:secret@example.com/", "https://127.0.0.1/", "https://192.168.1.1/", "https://localhost/", "https://example.com/#download" })
        {
            try { WebsiteSource.Url(bad); throw new Exception("Unsafe website URL accepted"); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Assertions++; }
        }
        foreach (var missing in new[] { "", "<a href='/downloads/Other-1.0.37.exe'>Wrong app</a>", "<script>const download='downloads/FrameDrop-Setup-1.0.37.exe'</script>", "<!-- <a href='downloads/FrameDrop-Setup-1.0.37.exe'>Old</a> -->", "<a data-href='downloads/FrameDrop-Setup-1.0.37.exe'>Not a link</a>" })
        {
            try { WebsiteSource.Parse(page, seed, missing); throw new Exception("Missing anchor accepted"); } catch (InvalidOperationException) { Assertions++; }
        }
        try { WebsiteSource.Parse(page, "https://framedropvr.com/latest.exe", html); throw new Exception("Unversioned seed accepted"); } catch (ArgumentException) { Assertions++; }
        using var client = new ReleaseClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") }));
        await client.VerifyRepository(key);
        Check((await client.Latest(key, websiteDownloadUrl: seed)).Tag == "1.0.38", "Website HTML accepted through actual release client");
        var downloaded = Path.Combine(Path.GetTempPath(), "GROUT-website-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var files = new ReleaseClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }));
            await files.Download(found.Assets[0], downloaded, new Progress<double>(), CancellationToken.None);
            Check(File.ReadAllBytes(downloaded).SequenceEqual(new byte[] { 1, 2, 3 }), "Website asset uses the common verified download pipeline");
        }
        finally { if (File.Exists(downloaded)) File.Delete(downloaded); }
        using var unsafeClient = new ReleaseClient(new Handler(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://127.0.0.1/") } }));
        try { await unsafeClient.Latest(key, websiteDownloadUrl: seed); throw new Exception("Unsafe redirect accepted"); } catch (InvalidOperationException) { Assertions++; }
        var wizard = new SetupWindow(client, []) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        try
        {
            Find<TextBox>(wizard).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text = page;
            Find<TextBox>(wizard).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Current download URL").Text = seed;
            Find<Button>(wizard).Single(b => Equals(b.Content, "Next →")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(wizard.Result.Repo == key && wizard.Result.WebsiteDownloadUrl == seed && wizard.Result.Latest == "1.0.38", "Real Add App flow accepts website page and download template");
            Find<Button>(wizard).Single(b => Equals(b.Content, "← Back")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Find<TextBox>(wizard).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text == page && Find<TextBox>(wizard).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Current download URL").Text == seed, "Back preserves both public URLs without exposing an internal source key");
        }
        finally { wizard.Close(); }
        var owner = new Window();
        try
        {
            var editor = new AppSettingsPage(owner, saved, true, client);
            Check(Find<TextBox>(editor.Panel).Any(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Website download URL" && t.Text == seed), "Website download template is editable in App settings");
            Find<TextBox>(editor.Panel).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Website download URL").Text = found.Assets[0].Url;
            editor.Save();
            Check(saved.WebsiteDownloadUrl == found.Assets[0].Url, "Changed website template is saved");
        }
        finally { owner.Close(); }
    }
}
