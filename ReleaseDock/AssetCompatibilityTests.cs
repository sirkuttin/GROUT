using System.Runtime.InteropServices;
namespace ReleaseDock;
internal static class AssetCompatibilityTests
{
    public static int Assertions { get; private set; }
    public static void Run()
    {
        Assertions = 0;
        void Check(bool condition, string reason) { Assertions++; if (!condition) throw new Exception(reason); }
        var files = new[] { "airshipper-installer.exe", "airshipper.exe", "macos-client-aarch64.zip", "macos-client-x86_64.zip", "linux-client-x86_64.zip" }.Select(n => new Asset(n, "https://example.test/" + n, -1, null)).ToList();
        Check(AssetCompatibility.Candidates(files, architecture: Architecture.X64).Select(a => a.Name).SequenceEqual(files.Take(2).Select(a => a.Name)), "Airshipper shows only Windows EXEs");
        Check(AssetCompatibility.Candidates(files, true, Architecture.X64).Count == 5, "Explicit override retains all platform downloads");
        foreach (var name in new[] { "app-linux.zip", "app-osx.zip", "app-darwin.zip", "app-android.zip", "app-sources.zip", "app-symbols.zip", "app-arm64.exe" })
            Check(!AssetCompatibility.ForWindows(name, Architecture.X64), "Hide incompatible file: " + name);
        foreach (var name in new[] { "app.exe", "app.zip", "Macaw.exe", "app-win-x64.zip", "app-x86.exe", "app-universal.zip" })
            Check(AssetCompatibility.ForWindows(name, Architecture.X64), "Keep Windows or unlabelled candidate: " + name);
        Check(!AssetCompatibility.ForWindows("app-x86_64.zip", Architecture.X86), "x86_64 is not x86");
        Check(AssetCompatibility.ForWindows("app-arm64.zip", Architecture.Arm64), "Native ARM64 download available");
        var app = new TrackedApp { AssetName = files[2].Name, Release = new Release("v1", "", "", files) };
        Check(ReleaseFiles.Match(app) == null, "Previously selected incompatible file cannot silently update");
        app.ShowAllDownloads = true;
        Check(ReleaseFiles.Match(app)?.Name == files[2].Name, "Explicit override permits unusual selection");
    }
}
