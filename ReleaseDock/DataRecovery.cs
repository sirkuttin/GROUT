using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

internal static class DataRecovery
{
    public static FrameworkElement View(string path, Exception failure, Action retry, Action exit, Action<string>? openFolder = null)
    {
        var panel = new StackPanel { Margin = new Thickness(28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Ui.Text("GROUT could not load its saved data", 26));
        panel.Children.Add(Ui.Text("Your saved files have been left unchanged. Updates and saving are paused until this is fixed.", muted: true));
        panel.Children.Add(Ui.Text("File needing attention", 18));
        var file = new TextBox { Text = path, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        System.Windows.Automation.AutomationProperties.SetName(file, "File needing attention"); panel.Children.Add(file);
        panel.Children.Add(Ui.Text(failure.Message, muted: true));
        panel.Children.Add(Ui.Text("Open the data folder, correct this file or replace it with a known-good copy, then choose Retry loading. GROUT will validate it before using it. Deleting the file starts with an empty library or default settings; it does not recover the old data.", muted: true));
        var actions = new WrapPanel(); var feedback = Ui.Text("", muted: true);
        actions.Children.Add(Ui.Button("Open data folder", (_, _) =>
        {
            try { var folder = Path.GetDirectoryName(Path.GetFullPath(path))!; if (openFolder != null) openFolder(folder); else Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); feedback.Text = ""; }
            catch (Exception ex) { feedback.Text = "Could not open the data folder: " + ex.Message; }
        }));
        actions.Children.Add(Ui.Button("Retry loading", (_, _) => retry()));
        actions.Children.Add(Ui.Button("Exit GROUT", (_, _) => exit()));
        panel.Children.Add(actions); panel.Children.Add(feedback);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
