namespace ReleaseDock;

internal static class ReleaseChecks
{
    public static async Task Fetch(ReleaseClient client, TrackedApp app, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        try
        {
            var release = await client.Latest(app.Repo, app.IncludePrereleases, app.NexusFileId, app.WebsiteDownloadUrl, cancel);
            cancel.ThrowIfCancellationRequested();
            InstalledVersions.Reconcile(app);
            app.Release = release; app.Latest = release.Tag;
            if (release.SelectedFileId > 0) { app.NexusFileId = release.SelectedFileId; app.AssetName = release.Assets.First(a => a.FileId == app.NexusFileId).Name; }
            app.Error = app.NexusFileId > 0 && Repositories.Resolve(app.Repo).Host == "nexusmods.com" && release.SelectedFileId == 0 ? "The tracked Nexus file is no longer available. Choose its replacement in Settings." : "";
            app.CheckedAt = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
        catch (Exception ex) { app.Error = ex is OperationCanceledException ? "Check timed out" : ex.Message; }
    }

    public static async Task Run(ReleaseClient client, IEnumerable<TrackedApp> entries, Action<TrackedApp> completed, CancellationToken cancel)
    {
        var apps = entries.Where(a => !a.IsGroup).ToList();
        for (var i = 0; i < apps.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            await Fetch(client, apps[i], cancel);
            completed(apps[i]);
            cancel.ThrowIfCancellationRequested();
            if (i + 1 < apps.Count) await Task.Delay(400, cancel);
        }
    }
}
