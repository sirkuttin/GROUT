namespace ReleaseDock;

internal static class InstallerWait
{
    public static void ConfirmVersion(TrackedApp app)
    {
        if (!app.InstallerWaitStopped) return;
        app.InstallerWaitStopped = false; app.UpdateState = ""; app.InstallDiscoveryNote = "";
    }
    // Detach only the wait: never cancel/kill an external installer. Its late outcome
    // is observed, but cannot mutate the app or create an installation receipt.
    public static async Task<bool> Complete(Task execution, CancellationToken stop)
    {
        var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = stop.Register(() => stopped.TrySetResult(true));
        await Task.WhenAny(execution, stopped.Task);
        if (execution.IsCompleted) { await execution; return true; }
        _ = execution.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return false;
    }
}
