using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;

namespace ReleaseDock;

internal static class InstallerWaitTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
    }
    private static async Task<T> Within<T>(Task<T> task)
    {
        if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new Exception("Installer wait did not detach promptly.");
        return await task;
    }
    public static async Task Run()
    {
        Assertions = 0;
        var root = Path.Combine(Path.GetTempPath(), "GROUT-installer-wait-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var owner = new Window(); using var client = new ReleaseClient(new Handler());
        try
        {
            string Work(string category) { var path = Path.Combine(root, category, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
            var app = new TrackedApp { Repo = "example/tool", UpdateMethod = "Installer", Installed = "1.0", Latest = "2.0", PendingInstallerRelease = "obsolete" };
            var execution = new TaskCompletionSource<bool>(); using var stop = new CancellationTokenSource(); var waiting = false;
            var outcome = await Within(UpdateRunner.Run(owner, app, new("Setup.exe", "https://github.com/example/tool/setup.exe", 3, null), client, (_, _) => { }, () => { }, stop.Token,
                prompts: new UpdatePrompts { RunInstaller = (_, _) => true, ExecuteInstaller = _ => execution.Task }, newWork: Work,
                discovery: new InstallDiscovery(() => []), installerWaiting: value => { waiting = value; if (value) stop.Cancel(); }));
            Check(outcome == UpdateOutcome.InstallerWaitStopped && !waiting && !execution.Task.IsCompleted, "Stop waiting detaches without completing or cancelling installer execution");
            Check(app.Installed == "1.0" && app.PendingInstallerRelease == "" && app.VersionRecord == null && app.InstallerWaitStopped, "Detached installer cannot claim a new applied release");
            Check(File.Exists(app.DownloadedPath) && app.UpdateState.Contains("may still be running"), "Detached wait keeps payload and explains next action");
            var store = new Store(Path.Combine(root, "apps.json")); store.Save([app]); var restored = store.Load().Single();
            Check(restored.InstallerWaitStopped && restored.Installed == "1.0" && restored.PendingInstallerRelease == "", "Unconfirmed state survives restart without a successful installer claim");
            // Even if another detector reports the latest version, a pending external
            // installer may still need its package until explicit resolution.
            restored.Installed = restored.Latest; StorageCleanup.Sweep([restored], root);
            Check(File.Exists(restored.DownloadedPath), "Cleanup preserves package while installer wait is unresolved");
            execution.SetResult(true); await Task.Yield();
            Check(app.Installed == "1.0" && app.VersionRecord == null && app.PendingInstallerRelease == "", "Late successful exit does not mutate app state after detaching");
            InstallerWait.ConfirmVersion(restored); StorageCleanup.Sweep([restored], root);
            Check(!restored.InstallerWaitStopped && restored.UpdateState == "" && !File.Exists(restored.DownloadedPath), "Explicit version confirmation resolves pending warning and permits normal cleanup");
            var lateFailure = new TaskCompletionSource<bool>(); using var cancel = new CancellationTokenSource();
            var detached = InstallerWait.Complete(lateFailure.Task, cancel.Token); cancel.Cancel();
            Check(!await Within(detached), "Late failure wait detaches promptly"); lateFailure.SetException(new IOException("simulated installer failure")); await Task.Yield();
            Check(await InstallerWait.Complete(Task.CompletedTask, CancellationToken.None), "Already completed installer proceeds to normal verification");
            try { await InstallerWait.Complete(Task.FromException(new IOException("failed installer")), CancellationToken.None); throw new Exception("Failed installer accepted"); } catch (IOException) { Assertions++; }
            using var beforeLaunch = new CancellationTokenSource(); beforeLaunch.Cancel(); var launched = false;
            try { await UpdateRunner.Run(owner, new() { Repo = "example/tool", UpdateMethod = "Installer", Latest = "2.0" }, new("Setup.exe", "https://github.com/example/tool/setup.exe", 3, null), client, (_, _) => { }, () => { }, beforeLaunch.Token, prompts: new UpdatePrompts { RunInstaller = (_, _) => true, ExecuteInstaller = _ => { launched = true; return Task.CompletedTask; } }, newWork: Work); } catch (OperationCanceledException) { }
            Check(!launched, "Cancellation before installer launch does not start a process");
            // Real benign, hidden helper validates process detachment, not a third-party installer.
            using var process = Process.Start(new ProcessStartInfo("powershell.exe") { Arguments = "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"", UseShellExecute = false, CreateNoWindow = true })!;
            try
            {
                var processWait = process.WaitForExitAsync(); using var stopProcessWait = new CancellationTokenSource();
                var detachedProcess = InstallerWait.Complete(processWait, stopProcessWait.Token); stopProcessWait.Cancel();
                Check(!await Within(detachedProcess) && !process.HasExited, "Stopping wait leaves a real isolated helper process alive");
            }
            finally { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
        }
        finally { owner.Close(); Directory.Delete(root, true); }
    }
}
