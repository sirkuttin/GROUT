using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

internal static class ReleaseCheckTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) { Calls++; return send(request, cancel); }
    }
    private static HttpResponseMessage Release() => new(HttpStatusCode.OK) { Content = new StringContent("{\"tag_name\":\"v2.0.0\",\"html_url\":\"https://github.com/example/app/releases/tag/v2.0.0\",\"body\":\"\",\"assets\":[]}", System.Text.Encoding.UTF8, "application/json") };
    private static async Task Within(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new Exception("Cancellation test did not finish promptly.");
        await task;
    }
    public static async Task Run()
    {
        Assertions = 0;
        foreach (var source in new[] { "example/app", "https://codeberg.org/example/app", "https://gitlab.com/example/app", "https://d2rloader.net/hub/plugins/example", WebsiteSource.SourceKey("https://example.com/") })
        {
            using var stop = new CancellationTokenSource(); var entered = new TaskCompletionSource<bool>();
            var handler = new Handler(async (_, cancel) => { entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, cancel); return Release(); });
            using var client = new ReleaseClient(handler);
            var app = new TrackedApp { Repo = source, WebsiteDownloadUrl = "https://example.com/App-1.0.0.exe", Installed = "v1.0.0", Latest = "previous", Error = "previous issue", CheckedAt = DateTimeOffset.UtcNow.AddDays(-1) };
            var checkedAt = app.CheckedAt;
            var request = ReleaseChecks.Fetch(client, app, stop.Token); await Within(entered.Task); stop.Cancel();
            try { await Within(request); throw new Exception("Cancelled request completed normally."); } catch (OperationCanceledException) { }
            Check(app.Latest == "previous" && app.Error == "previous issue" && app.CheckedAt == checkedAt, "User cancellation preserves previous release/error/time for " + source);
            Check(handler.Calls == 1, "Cancellation reaches the active provider request without retries");
        }
        var pending = new TaskCompletionSource<bool>();
        var calls = 0;
        var batchHandler = new Handler(async (_, cancel) =>
        {
            if (++calls == 2) { pending.TrySetResult(true); await Task.Delay(Timeout.Infinite, cancel); }
            return Release();
        });
        using var batchClient = new ReleaseClient(batchHandler);
        var window = new MainWindow(preview: true, releaseClient: batchClient);
        try
        {
            var list = (ListBox)window.FindName("Library"); var entries = (ObservableCollection<TrackedApp>)list.ItemsSource;
            entries.Clear();
            var first = new TrackedApp { Repo = "example/first", Installed = "v1.0.0" };
            var second = new TrackedApp { Repo = "example/second", Installed = "v1.0.0", Latest = "old", Error = "old error" };
            var third = new TrackedApp { Repo = "example/third", Installed = "v1.0.0", Latest = "untouched" };
            entries.Add(first); entries.Add(second); entries.Add(third); list.SelectedItem = first;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var preferences = (Preferences)typeof(MainWindow).GetField("preferences", flags)!.GetValue(window)!;
            var previousSuccess = DateTimeOffset.UtcNow.AddDays(-2); preferences.LastSuccess = previousSuccess;
            var pausedUntil = DateTimeOffset.UtcNow.AddHours(2); preferences.RetryAfter = pausedUntil;
            var task = window.CheckLibrary(); await Within(pending.Task);
            var button = (Button)window.FindName("CheckButton");
            Check(Equals(button.Content, "Cancel checks") && button.IsEnabled, "Active batch exposes cancellation at its original control");
            typeof(MainWindow).GetMethod("Check_Click", flags)!.Invoke(window, [window, new RoutedEventArgs()]);
            await Within(task);
            Check(batchHandler.Calls == 2 && first.Latest == "v2.0.0" && first.CheckedAt != null, "Cancellation retains completed results and never starts third request");
            Check(second.Latest == "old" && second.Error == "old error" && second.CheckedAt == null && third.Latest == "untouched" && third.Error == "", "Active and unattempted items are not relabelled failed or current");
            Check(preferences.LastSuccess == previousSuccess && preferences.RetryAfter == pausedUntil && preferences.LastAttempt != null, "Cancelled batch records attempt without successful completion or clearing retry policy");
            Check(Equals(button.Content, "Check now") && button.IsEnabled && !((TextBlock)window.FindName("CountLabel")).Text.Contains("Checking"), "Cancelled batch restores controls and normal count without footer success");
            await Within(window.CheckLibrary());
            Check(third.Latest == "v2.0.0" && preferences.LastSuccess > previousSuccess && preferences.RetryAfter == null, "Next complete batch runs normally and advances successful-check state");
        }
        finally { window.Close(); }
        using var timeoutClient = new ReleaseClient(new Handler((_, _) => throw new TaskCanceledException()));
        var timedOut = new TrackedApp { Repo = "example/timeout", Installed = "v1.0.0" };
        await ReleaseChecks.Fetch(timeoutClient, timedOut);
        Check(timedOut.Error == "Check timed out", "Provider timeout remains distinct from deliberate cancellation");
        using var alreadyStopped = new CancellationTokenSource(); alreadyStopped.Cancel();
        var unusedHandler = new Handler((_, _) => Task.FromResult(Release())); using var unusedClient = new ReleaseClient(unusedHandler);
        try { await ReleaseChecks.Run(unusedClient, [timedOut], _ => { }, alreadyStopped.Token); } catch (OperationCanceledException) { }
        Check(unusedHandler.Calls == 0, "Already cancelled batch sends no requests");
    }
}
