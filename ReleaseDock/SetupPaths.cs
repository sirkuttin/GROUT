using System.IO;

namespace ReleaseDock;

public static class SetupPaths
{
    public static string PortableExe(string folder, string filename)
    {
        folder = folder.Trim().Trim('"'); filename = filename.Trim();
        if (!Path.IsPathFullyQualified(folder)) throw new InvalidOperationException("Choose a full folder path to save the app in.");
        if (File.Exists(folder) || folder.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a folder here, not an EXE file.");
        if (!filename.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || filename != Path.GetFileName(filename) || filename.Contains('\\') || filename.Contains('/')) throw new InvalidOperationException("Enter just an EXE filename, such as MyApp.exe.");
        FileUpdates.SafeRelative(filename);
        return Path.GetFullPath(Path.Combine(folder, filename));
    }
    public static void ClearExistingTracking(TrackedApp app)
    {
        app.Installed = ""; app.InstallLocation = ""; app.AutoReconcile = false;
        app.LocalError = false; app.LocalStatus = ""; app.InstalledProvenance = "";
        app.UpdateTarget = ""; app.UpdateMethod = "Manual"; app.UpdateState = "";
        app.PendingLocationConflict = false;
    }
}
