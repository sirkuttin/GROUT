using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class DownloadChoiceTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) { if (child is T value) yield return value; foreach (var descendant in Find<T>(child)) yield return descendant; }
    }
    public static void Run()
    {
        Assertions = 0;
        var replacement = new Asset("D2R.Reimagined.Loader.-.3.1.1.R17.zip", "", 334909549, null);
        var app = new TrackedApp { Repo = "D2R-Reimagined/d2r-reimagined-mod", Installed = "3.1.0", Latest = "3.1.1", AssetName = "D2R.Reimagined.Loader.-.3.1.0.zip", UpdateMethod = "Zip", UpdateTarget = @"D:\Games\Reimagined", Release = new("3.1.1", "", "", [replacement]) };
        Check(ReleaseFiles.Match(app) == replacement && ReleaseFiles.SelectionExplanation(app).Length == 0, "Actual Reimagined filename with added R17 revision matches existing ZIP family");
        app.Release.Assets.Add(replacement with { Name = "D2R.Reimagined.Loader.-.3.1.1.R16.zip" });
        Check(ReleaseFiles.Match(app) == null, "Multiple revision candidates are never guessed");
        app.Release = new("3.1.1", "", "", [replacement with { Name = "D2R.Reimagined.ModOnly.-.3.1.1.R17.zip" }]);
        Check(ReleaseFiles.Match(app) == null && app.DisplayStatus == "Choose download" && app.StatusColor == "#F0D495", "Changed edition requires explicit selection with attention styling");
        Check(ReleaseFiles.SelectionExplanation(app).Contains(app.AssetName) && ReleaseFiles.SelectionExplanation(app).Contains("replacement"), "Unmatched-file explanation states the saved file and recovery action");
        app.Release.Assets.Add(new("Installer.exe", "", 1, null));
        Check(ReleaseFiles.UpdateCandidates(app).Count == 1, "ZIP choice excludes installers without changing the selected update method");
        var owner = new Window();
        try
        {
            owner.ShowActivated = false; owner.WindowStartupLocation = WindowStartupLocation.Manual; owner.Left = -20000; owner.Top = -20000; owner.Show();
            var previous = app.AssetName;
            Check(!ReleaseDownloadPicker.Show(owner, app, window => { window.Left = -20000; window.Top = -20000; Find<Button>(window).Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }) && app.AssetName == previous, "Cancel preserves configured download");
            Check(ReleaseDownloadPicker.Show(owner, app, window => { window.Left = -20000; window.Top = -20000; Find<Button>(window).Single(b => Equals(b.Content, "Use this download")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }), "Direct choose-download dialog accepts a compatible selection");
            Check(app.AssetName.Contains("ModOnly") && ReleaseFiles.Match(app) != null && app.Installed == "3.1.0" && app.DownloadedPath.Length == 0, "Selection only updates remembered file, never installs or marks a version");
        }
        finally { owner.Close(); }
        app.Release = new("3.1.1", "", "", []);
        Check(app.DisplayStatus == "No compatible download" && ReleaseFiles.SelectionExplanation(app).Contains("Open the release page"), "No-compatible-file state gives a different recovery action");
        var legacy = new TrackedApp { Repo = "https://d2rloader.net/hub/plugins/remote-stash", AppFolder = @"D:\Games\D2R\remote-stash" };
        Check(legacy.TargetLabel.Length == 0, "Existing unnamed mods do not display internal placeholders or Custom scope");
        legacy.TargetName = "D2R Reimagined";
        Check(legacy.TargetLabel == "D2R Reimagined", "Optional named target shown without unexplained scope suffix");
        var preview = new MainWindow(true);
        try
        {
            var item = (TrackedApp)((ListBox)preview.FindName("Library")).SelectedItem; item.ItemKind = "Mod"; item.AppFolder = legacy.AppFolder;
            ((ListBox)preview.FindName("Library")).SelectedItem = null; ((ListBox)preview.FindName("Library")).SelectedItem = item;
            var context = ((TextBlock)preview.FindName("TargetContextLabel")).Text;
            Check(context == "Install folder: " + legacy.AppFolder && !context.Contains("Custom"), "Unnamed installation detail shows actual location without a setup warning");
            item.AssetName = "Old-name.zip"; item.Latest = "v9.0"; item.UpdateMethod = "Zip"; item.Release = new("v9.0", "", "", [new("New-name.zip", "", 1, null)]);
            ((ListBox)preview.FindName("Library")).SelectedItem = null; ((ListBox)preview.FindName("Library")).SelectedItem = item;
            Check(((Button)preview.FindName("UpdateButton")).IsEnabled && Equals(((Button)preview.FindName("UpdateButton")).Content, "Choose download…"), "Missing match exposes an enabled direct action instead of disabled Update");
        }
        finally { preview.Close(); }
    }
}
