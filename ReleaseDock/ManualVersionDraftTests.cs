using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class ManualVersionDraftTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private static void Invoke(MainWindow window, string method, params object[] arguments) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, arguments);
    public static void Run()
    {
        Assertions = 0;
        var window = new MainWindow(true);
        try
        {
            var list = (ListBox)window.FindName("Library");
            var input = (TextBox)window.FindName("InstalledInput");
            var first = (TrackedApp)list.SelectedItem;
            input.Text = "  drafted-2.0  ";
            Invoke(window, "Render");
            Check(input.Text == "  drafted-2.0  " && first.Installed == "v1.0.0", "Unfocused unsaved version draft survives an unrelated render without modifying the recorded version");
            first.Installed = "background-1.1";
            Invoke(window, "Render");
            Check(input.Text == "  drafted-2.0  ", "A background model change does not silently discard the draft");
            Invoke(window, "SaveVersion_Click", window, new RoutedEventArgs());
            Check(first.Installed == "drafted-2.0" && input.Text == "drafted-2.0" && !first.AutoReconcile, "Explicit Save commits and displays the trimmed draft");
            first.Installed = "background-2.1";
            Invoke(window, "Render");
            Check(input.Text == "background-2.1", "A clean input follows subsequent model changes");
            input.Text = "discarded-by-mark";
            Invoke(window, "Mark_Click", window, new RoutedEventArgs());
            Check(first.Installed == first.Latest && input.Text == first.Latest, "Mark latest explicitly replaces a pending draft with its confirmed version");
            window.PreviewModLibrary();
            var other = (TrackedApp)list.SelectedItem;
            input.Text = "other-draft";
            list.SelectedItem = first;
            Check(input.Text == first.Installed, "Selecting a different item resets the draft to that item's version");
            list.SelectedItem = other;
            Check(input.Text == other.Installed, "Returning to an item does not resurrect a discarded draft");
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GROUT-recheck-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(root);
            try
            {
                list.SelectedItem = first; first.AutoReconcile = false; first.InstallLocation = root;
                var previous = first.Installed; var footer = (TextBlock)window.FindName("StatusLabel"); var footerBefore = footer.Text;
                Invoke(window, "Recheck_Click", window, new RoutedEventArgs());
                var feedback = (TextBlock)window.FindName("VersionCheckError");
                Check(feedback.Text.Contains("Last recorded version kept") && first.Installed == previous && !first.AutoReconcile, "Failed one-time version check shows local feedback and preserves manual version");
                Invoke(window, "Render");
                Check(feedback.Text.Length > 0 && footer.Text == footerBefore, "Local version failure survives refresh without a distant footer message");
                list.SelectedItem = other;
                Check(feedback.Text.Length == 0, "Version feedback does not leak to another app");
                list.SelectedItem = first;
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "release-manifest.json"), "{\"version\":\"9.8.7\"}");
                Invoke(window, "Recheck_Click", window, new RoutedEventArgs());
                Check(first.Installed == "9.8.7" && feedback.Text.Length == 0 && footer.Text == footerBefore, "Successful retry clears local error without a routine success footer");
            }
            finally { System.IO.Directory.Delete(root, true); }
        }
        finally { window.Close(); }
    }
}
