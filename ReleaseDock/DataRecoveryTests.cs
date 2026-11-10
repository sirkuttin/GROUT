using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReleaseDock;

internal static class DataRecoveryTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var match in Find<T>(child)) yield return match;
    }
    private static void Click(DependencyObject root, string label) => Find<Button>(root).Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    public static void Run()
    {
        Assertions = 0;
        var root = Path.Combine(Path.GetTempPath(), "GROUT-data-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            foreach (var bad in new[] { "null", "{broken", "[{\"Repo\":\"https://invalid.test/nope\"}]", "[{\"Id\":\"cycle\",\"ParentId\":\"cycle\",\"IsGroup\":true}]" })
            {
                var library = Path.Combine(root, "apps.json"); File.WriteAllText(library, bad);
                var receiptFolder = Path.Combine(root, "versions"); Directory.CreateDirectory(receiptFolder); var receipt = Path.Combine(receiptFolder, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(receipt, "original receipt bytes");
                var window = new MainWindow(dataDirectory: root);
                try
                {
                    Check(!window.LibraryReady && !window.TrayVisible && Find<Button>((DependencyObject)window.Content).Any(b => Equals(b.Content, "Retry loading")), "Invalid data offers recovery without initializing updates or tray");
                    Check(Find<TextBox>((DependencyObject)window.Content).Single().Text == library, "Recovery identifies exact library file");
                    typeof(MainWindow).GetMethod("SafePersist", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                    Click((DependencyObject)window.Content, "Retry loading");
                    Check(!window.LibraryReady && File.ReadAllText(library) == bad && File.ReadAllText(receipt) == "original receipt bytes", "Invalid retry/persist preserves original configuration and receipts");
                    File.WriteAllText(library, "[{\"Repo\":\"example/recovered\",\"Installed\":\"1.2.3\"}]");
                    Click((DependencyObject)window.Content, "Retry loading");
                    var list = (ListBox)window.FindName("Library");
                    Check(window.LibraryReady && list.Items.Cast<TrackedApp>().Single().Repo == "example/recovered", "Corrected file retries into usable original library view");
                }
                finally { window.StopWindow(); }
                File.Delete(receipt);
            }
            File.WriteAllText(Path.Combine(root, "settings.json"), "null");
            var settingsWindow = new MainWindow(dataDirectory: root);
            try
            {
                Check(!settingsWindow.LibraryReady && Find<TextBox>((DependencyObject)settingsWindow.Content).Single().Text.EndsWith("settings.json"), "Damaged settings identify settings rather than the library");
                File.Delete(Path.Combine(root, "settings.json")); Click((DependencyObject)settingsWindow.Content, "Retry loading");
                Check(settingsWindow.LibraryReady, "Missing settings permit defaults while damaged settings do not");
            }
            finally { settingsWindow.StopWindow(); }
            var opened = ""; var retries = 0; var exits = 0;
            var panel = DataRecovery.View(Path.Combine(root, "apps.json"), new IOException("test issue"), () => retries++, () => exits++, folder => opened = folder);
            Click(panel, "Open data folder"); Click(panel, "Retry loading"); Click(panel, "Exit GROUT");
            Check(opened == root && retries == 1 && exits == 1, "Recovery buttons call explicit folder/retry/exit actions without replacement writes");
            var failingOpen = DataRecovery.View(Path.Combine(root, "apps.json"), new IOException("test issue"), () => { }, () => { }, _ => throw new IOException("denied"));
            Click(failingOpen, "Open data folder");
            Check(Find<TextBlock>(failingOpen).Any(t => t.Text.Contains("Could not open the data folder")), "Folder-opening failure stays beside recovery actions");
        }
        finally { Directory.Delete(root, true); }
    }
}
