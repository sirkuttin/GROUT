using System.Windows;
using System.Windows.Controls;
using ReleaseDock.Json;

namespace ReleaseDock;

public static class SettingsWindow
{
    public static bool Show(Window owner, Preferences preferences)
    {
        var window = Ui.Window("GROUT settings", 640, 560); window.Owner = owner;
        var panel = Ui.Panel(window);
        panel.Children.Add(Ui.Text("Settings", 28));
        panel.Children.Add(Ui.Text("G.R.O.U.T. " + SelfUpdater.CurrentLabel + " · MIT license", muted: true));
        panel.Children.Add(Ui.Button("Licenses and third-party notices", (_, _) =>
        {
            var licenses = Ui.Window("GROUT licenses", 780, 650); licenses.Owner = window;
            var assembly = typeof(SettingsWindow).Assembly;
            var text = string.Join("\n\n", assembly.GetManifestResourceNames().Where(n => n.Contains(".Licenses.")).OrderBy(n => n).Select(n => { using var stream = assembly.GetManifestResourceStream(n)!; using var reader = new System.IO.StreamReader(stream); return n + "\n\n" + reader.ReadToEnd(); }));
            licenses.Content = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) };
            licenses.ShowDialog();
        }));
        if (NexusMods.Enabled) panel.Children.Add(Ui.Button("Nexus Mods connection…", (_, _) => NexusSettings.Show(window)));
        panel.Children.Add(Ui.Text("Check for app updates"));
        var hours = new[] { 0, 1, 6, 24 };
        var frequency = Ui.Choice(["Manual only", "Every hour", "Every 6 hours", "Every day"], Array.IndexOf(hours, preferences.CheckHours)); panel.Children.Add(frequency);
        System.Windows.Automation.AutomationProperties.SetName(frequency, "Check for app updates");
        var quiet = Ui.Check("Quiet mode — no update notifications", preferences.Quiet); panel.Children.Add(quiet);
        var selfUpdates = Ui.Check("Check for GROUT updates automatically (daily)", preferences.SelfUpdateChecks); panel.Children.Add(selfUpdates);
        var startup = Ui.Check("Start GROUT with Windows", Preferences.StartsWithWindows()); panel.Children.Add(startup);
        panel.Children.Add(Ui.Text("When I close GROUT"));
        var closeBehavior = Ui.Choice(["Keep running in the tray", "Exit GROUT"], preferences.CloseToTray ? 0 : 1); panel.Children.Add(closeBehavior);
        System.Windows.Automation.AutomationProperties.SetName(closeBehavior, "When I close GROUT");
        panel.Children.Add(Ui.Text("Keeping GROUT in the tray allows scheduled checks to continue. Exiting stops checks. Finish active operations and close dialogs before exiting. Notifications follow Windows notification and Do Not Disturb settings. Startup opens in the tray.", muted: true));
        panel.Children.Add(Ui.Text("Keep the app in a permanent folder before enabling Windows startup. Moving the executable requires saving this setting again.", muted: true));
        var error = Ui.Text("", muted: true); panel.Children.Add(error);
        var row = new WrapPanel(); row.Children.Add(Ui.Button("Cancel", (_, _) => window.DialogResult = false));
        row.Children.Add(Ui.Button("Save settings", (_, _) =>
        {
            try
            {
                var draft = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences))!;
                draft.CheckHours = hours[frequency.SelectedIndex]; draft.Quiet = quiet.IsChecked == true;
                draft.SelfUpdateChecks = selfUpdates.IsChecked == true;
                draft.CloseToTray = closeBehavior.SelectedIndex == 0;
                var previousStartup = Preferences.StartupCommand();
                try { draft.Save(); Preferences.SetStartup(startup.IsChecked == true); }
                catch
                {
                    try { preferences.Save(); Preferences.SetStartupCommand(previousStartup); }
                    catch (Exception recovery) { throw new InvalidOperationException("Saving settings failed and restoring the previous settings also needs attention: " + recovery.Message); }
                    throw;
                }
                preferences.CheckHours = draft.CheckHours; preferences.Quiet = draft.Quiet; preferences.SelfUpdateChecks = draft.SelfUpdateChecks; preferences.CloseToTray = draft.CloseToTray; window.DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        })); panel.Children.Add(row);
        return window.ShowDialog() == true;
    }
}
