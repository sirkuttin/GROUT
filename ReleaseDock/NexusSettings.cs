using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReleaseDock;

internal static class NexusSettings
{
    internal static void Show(Window owner)
    {
        if (!NexusMods.Enabled) return;
        var window = Ui.Window("Nexus Mods connection", 680, 560); window.Owner = owner;
        var closed = false; window.Closed += (_, _) => closed = true;
        var panel = Ui.Panel(window);
        panel.Children.Add(Ui.Text("Connect Nexus Mods", 26));
        panel.Children.Add(Ui.Text("Testing integration: use your own personal API key. It is encrypted for your Windows account and kept only on this computer.", muted: true));
        panel.Children.Add(Ui.Button("Open Nexus API access", (_, _) => Process.Start(new ProcessStartInfo("https://www.nexusmods.com/users/myaccount?tab=api") { UseShellExecute = true })));
        var existing = NexusCredentials.Load();
        var status = Ui.Text(existing == null ? "Not connected" : "Connected as " + existing.Name + (existing.Premium ? " · Premium" : " · Free account")); panel.Children.Add(status);
        panel.Children.Add(Ui.Text("API key"));
        var password = new PasswordBox { Padding = new Thickness(10, 8, 10, 8), Background = new SolidColorBrush(Color.FromRgb(20, 32, 51)), Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 14) };
        System.Windows.Automation.AutomationProperties.SetName(password, "Nexus API key"); panel.Children.Add(password);
        panel.Children.Add(Ui.Text("All connected accounts can check versions. Direct downloads require Nexus Premium; free accounts open the selected file on Nexus to download in a browser. ZIP extraction and EXE replacement work like other GROUT sources. FOMOD installers, load orders and mod dependencies are not handled.", muted: true));
        var error = Ui.Text("", muted: true); panel.Children.Add(error);
        var actions = new WrapPanel();
        actions.Children.Add(Ui.Button("Connect", async (_, _) =>
        {
            actions.IsEnabled = password.IsEnabled = false; error.Text = "Checking Nexus account…";
            try
            {
                using var nexus = new NexusMods(); var account = await nexus.Validate(password.Password);
                if (closed) return;
                NexusCredentials.Save(account); password.Clear();
                status.Text = "Connected as " + account.Name + (account.Premium ? " · Premium" : " · Free account"); error.Text = "";
            }
            catch (Exception ex) { error.Text = ex.Message; }
            finally { actions.IsEnabled = password.IsEnabled = true; }
        }));
        actions.Children.Add(Ui.Button("Disconnect", (_, _) => { try { NexusCredentials.Disconnect(); password.Clear(); status.Text = "Not connected"; error.Text = ""; } catch (Exception ex) { error.Text = ex.Message; } }));
        actions.Children.Add(Ui.Button("Close", (_, _) => window.Close())); panel.Children.Add(actions);
        window.ShowDialog();
    }
}
