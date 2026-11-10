using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace ReleaseDock;

internal static class CloseBehaviorTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool ok, string reason) { Assertions++; if (!ok) throw new Exception(reason); }
    public static async Task Run()
    {
        Assertions = 0;
        var root = Path.Combine(Path.GetTempPath(), "GROUT-close-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "settings.json");
            File.WriteAllText(path, "{\"CheckHours\":0,\"SelfUpdateChecks\":false}");
            Check(Preferences.Load(path).CloseToTray, "Older settings preserve close-to-tray default");
            foreach (var closeToTray in new[] { true, false })
            {
                new Preferences { CheckHours = 0, SelfUpdateChecks = false, TrayExplained = true, CloseToTray = closeToTray }.Save(path);
                Check(Preferences.Load(path).CloseToTray == closeToTray, "Close preference survives persistence");
                var shutdown = false; var closed = false;
                var window = new MainWindow(dataDirectory: root, shutdown: () => shutdown = true); window.Closed += (_, _) => closed = true;
                try
                {
                    window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Check(closeToTray ? !closed && window.TrayVisible && !shutdown : closed && !window.TrayVisible && shutdown, "Close follows selected tray/exit behavior and exit removes tray resources");
                }
                finally { if (!closed) window.StopWindow(); }
            }
            new Preferences { CheckHours = 0, SelfUpdateChecks = false, CloseToTray = false }.Save(path);
            var shutdownBlocked = false; var guarded = new MainWindow(dataDirectory: root, shutdown: () => shutdownBlocked = true); var guardedClosed = false; guarded.Closed += (_, _) => guardedClosed = true;
            try
            {
                var busy = typeof(MainWindow).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!;
                busy.SetValue(guarded, true); guarded.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(!guardedClosed && !shutdownBlocked && guarded.TrayVisible, "Exit-on-close cannot interrupt an active operation");
                busy.SetValue(guarded, false);
                new System.Windows.Interop.WindowInteropHelper(guarded).EnsureHandle();
                var child = new Window { Owner = guarded };
                try { guarded.Close(); Check(!guardedClosed && !shutdownBlocked, "Exit-on-close cannot discard an open dialog"); }
                finally { child.Close(); }
                guarded.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(guardedClosed && shutdownBlocked && !guarded.TrayVisible, "Exit-on-close works once the operation/dialog is finished");
            }
            finally { if (!guardedClosed) guarded.StopWindow(); }
        }
        finally { Directory.Delete(root, true); }
    }
}
