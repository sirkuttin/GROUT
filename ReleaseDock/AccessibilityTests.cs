using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace ReleaseDock;

internal static class AccessibilityTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    public static void Run()
    {
        Assertions = 0;
        var window = new MainWindow(preview: true);
        try
        {
            window.PreviewModLibrary();
            var list = (ListBox)window.FindName("Library");
            var parent = (TrackedApp)list.SelectedItem;
            Check(AutomationProperties.GetName(list) == "Library", "Library has a meaningful accessible name");
            Check(AutomationProperties.GetName((TextBox)window.FindName("LibrarySearch")) == "Search library" && AutomationProperties.GetName((TextBox)window.FindName("InstalledInput")) == "Installed version", "Core editable controls expose their purpose");
            Check(parent.LibraryAutomationName.Contains("expanded") && !parent.LibraryAutomationName.Contains("ReleaseDock.TrackedApp"), "Accessible branch description includes real item and effective state");
            window.NavigateLibrary(Key.Left);
            Check(!parent.Expanded && list.SelectedItem == parent && parent.LibraryAutomationName.Contains("collapsed"), "Left collapses an expanded branch without changing selection");
            window.NavigateLibrary(Key.Right);
            Check(parent.Expanded && list.SelectedItem == parent, "Right expands a collapsed branch before entering children");
            window.NavigateLibrary(Key.Right); var child = (TrackedApp)list.SelectedItem;
            Check(child.ParentId == parent.Id && child.TreeVisible, "Right enters first visible child of expanded branch");
            window.NavigateLibrary(Key.Left);
            Check(list.SelectedItem == parent, "Left from leaf selects its visible parent");
            window.NavigateLibrary(Key.Left); var storedExpansion = parent.Expanded;
            ((TextBox)window.FindName("LibrarySearch")).Text = "Auto Deposit";
            list.SelectedItem = parent; window.NavigateLibrary(Key.Right);
            Check(((TrackedApp)list.SelectedItem).AppName == "Auto Deposit" && parent.Expanded == storedExpansion, "Filtered keyboard navigation uses visible results without rewriting saved collapse state");
            window.NavigateLibrary(Key.Left); window.NavigateLibrary(Key.Left);
            Check(parent.Expanded == storedExpansion, "Left navigation cannot silently collapse forced-open filtered branches");
            ((TextBox)window.FindName("LibrarySearch")).Text = "";
            Check(!parent.Expanded && !parent.TreeFilterExpanded, "Clearing filter restores original collapsed branch");
            Check(!window.NavigateLibrary(Key.Tab), "Branch navigation does not consume Tab or trap focus");
            var choices = Ui.Choice(["Every hour", "Every day"]);
            Check(choices.Items.Cast<ComboBoxItem>().All(i => AutomationProperties.GetName(i).Length > 0), "Custom option content retains explicit accessible labels");
        }
        finally { window.Close(); }
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            var sample = new MainWindow(preview: true);
            try
            {
                Ui.FitToWorkArea(sample, new Size(1920, 1040), scale, scale);
                Check(sample.Width * scale <= 1920 && sample.Height * scale <= 1040 && sample.MinWidth <= sample.MaxWidth && sample.MinHeight <= sample.MaxHeight, "Window limits fit simulated 1920x1040 work area at " + scale);
                var root = (FrameworkElement)sample.Content;
                var available = new Size(sample.Width - 20, sample.Height - 40);
                root.Measure(available); root.Arrange(new Rect(available)); root.UpdateLayout();
                foreach (var name in new[] { "CheckButton", "AddAppButton", "CheckSelfButton", "SettingsButton" })
                {
                    var button = (Button)sample.FindName(name); var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                    Check(button.ActualWidth > 0 && bounds.Left >= -1 && bounds.Right <= available.Width + 1 && bounds.Top >= -1 && bounds.Bottom <= available.Height + 1, "Essential control stays inside simulated layout at " + scale + ": " + name);
                }
            }
            finally { sample.Close(); }
        }
    }
}
