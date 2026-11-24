using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Controls;
using ReleaseDock.Json;

namespace ReleaseDock;

internal static class MessageRoutingTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    public static void Run()
    {
        Assertions = 0; var window = new MainWindow(preview: true);
        try
        {
            var list = (ListBox)window.FindName("Library"); var entries = (ObservableCollection<TrackedApp>)list.ItemsSource;
            var app = (TrackedApp)list.SelectedItem; var footer = (TextBlock)window.FindName("StatusLabel"); var before = footer.Text;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            void Render() => typeof(MainWindow).GetMethod("Render", flags)!.Invoke(window, null);
            app.ActiveOperation = "Waiting for installer. Stop waiting leaves the installer running."; Render();
            Check(app.DisplayStatus == "Updating…" && ((TextBlock)window.FindName("StateLabel")).Text == "Updating…", "Active item and sidebar identify ongoing work");
            Check(((TextBlock)window.FindName("UpdateStateLabel")).Text.Contains("Stop waiting") && footer.Text == before, "Operation progress belongs to app detail without a footer duplicate");
            var other = new TrackedApp { Repo = "example/other", Installed = "1.0", Latest = "1.0" }; entries.Add(other); list.SelectedItem = other;
            Check(!((TextBlock)window.FindName("UpdateStateLabel")).Text.Contains("Stop waiting") && app.DisplayStatus == "Updating…", "Progress remains attached to its app when selection changes");
            list.SelectedItem = app; app.ActiveOperation = ""; app.UpdateState = "Update stopped: simulated access denied"; Render();
            Check(((TextBlock)window.FindName("UpdateStateLabel")).Text.Contains("simulated access denied") && footer.Text == before, "Operation failure remains visible on affected app instead of distant footer");
            app.ActiveOperation = "Downloading a package";
            var saved = JsonSerializer.Serialize(app);
            Check(!saved.Contains("ActiveOperation") && !saved.Contains("Downloading a package"), "Transient progress is not persisted as a resumable operation");
            app.ActiveOperation = ""; app.UpdateState = ""; Render();
            Check(((TextBlock)window.FindName("UpdateStateLabel")).Visibility == System.Windows.Visibility.Collapsed, "Resolved normal state has no routine progress/success message");
            var legacy = new TrackedApp { Repo = app.Repo, UpdateState = "\nFound SteamlessController version 1.25 in Windows installed apps: C:\\Program Files\\SteamlessController" };
            var restored = JsonSerializer.Deserialize<TrackedApp>(JsonSerializer.Serialize(legacy))!;
            app.UpdateState = restored.UpdateState; Render();
            Check(((TextBlock)window.FindName("UpdateStateLabel")).Visibility == System.Windows.Visibility.Collapsed, "Previously saved discovery success is hidden after reload");
            app.UpdateState = "Installation not verified — recheck the version or mark it manually" + restored.UpdateState; Render();
            Check(((TextBlock)window.FindName("UpdateStateLabel")).Text == "Installation not verified — recheck the version or mark it manually", "Legacy success is removed while a separate installation warning remains visible");
            app.UpdateState = "Found App, but it still reports 1.0. The expected release is 2.0."; Render();
            Check(((TextBlock)window.FindName("UpdateStateLabel")).Text == app.UpdateState, "Discovery version mismatch remains visible");
            var self = (TextBlock)window.FindName("SelfUpdateLabel"); self.Text = new string('x', 1000); self.Measure(new System.Windows.Size(10000, 100));
            Check(self.DesiredSize.Width <= 246 && self.TextTrimming == System.Windows.TextTrimming.CharacterEllipsis, "Self-update feedback cannot expand footer without bound");
        }
        finally { window.Close(); }
    }
}
