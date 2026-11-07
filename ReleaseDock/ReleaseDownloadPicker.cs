using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class ReleaseDownloadPicker
{
    internal static bool Show(Window owner, TrackedApp app, Action<Window>? ready = null)
    {
        var window = Ui.Window("Choose update download", 690, 520); window.Owner = owner;
        var panel = Ui.Panel(window);
        panel.Children.Add(Ui.Text("Choose a download for " + app.AppName, 24));
        panel.Children.Add(Ui.Text(ReleaseFiles.SelectionExplanation(app), muted: true));
        var list = new ListBox { Background = System.Windows.Media.Brushes.Transparent, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0,16,0,16), MinHeight = 100, MaxHeight = 260 };
        foreach (var asset in ReleaseFiles.UpdateCandidates(app)) list.Items.Add(new ListBoxItem { Content = ReleaseFiles.FileLabel(asset), Tag = asset, Padding = new Thickness(8) });
        if (list.Items.Count == 1) list.SelectedIndex = 0;
        panel.Children.Add(list); panel.Children.Add(Ui.Text("This saves your download choice. Select Update afterward to install it.", muted: true));
        var error = Ui.Text(""); panel.Children.Add(error);
        var actions = new WrapPanel(); panel.Children.Add(actions);
        actions.Children.Add(Ui.Button("Cancel", (_, _) => window.DialogResult = false));
        var choose = Ui.Button("Use this download", (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: Asset asset }) return;
            try { ReleaseFiles.Validate(app, asset); ReleaseFiles.Select(app, asset); window.DialogResult = true; }
            catch (Exception ex) { error.Text = ex.Message; }
        });
        choose.IsDefault = true; choose.IsEnabled = list.SelectedIndex >= 0; actions.Children.Add(choose);
        list.SelectionChanged += (_, _) => choose.IsEnabled = list.SelectedIndex >= 0;
        window.Loaded += (_, _) => ready?.Invoke(window);
        return window.ShowDialog() == true;
    }
}
