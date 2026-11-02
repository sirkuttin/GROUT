using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ReleaseDock;

public static class AssetCompatibility
{
    private static bool Token(string name, string pattern) => Regex.IsMatch(name, @"(?:^|[\s._()-])(?:" + pattern + @")(?:$|[\s._()-])", RegexOptions.IgnoreCase);
    public static bool ForWindows(string name, Architecture? architecture = null)
    {
        if (!Repositories.IsWindowsAsset(name)) return false;
        var windows = Token(name, "windows|win|win32|win64|win-x64|win-x86|win-arm64");
        if (!windows && Token(name, "linux|macos|mac|osx|darwin|android|ios|freebsd|openbsd|netbsd|ubuntu|debian|fedora|raspberrypi")) return false;
        if (Token(name, "source|sources|sourcecode|src|symbols|debugsymbols")) return false;
        var arch = architecture ?? RuntimeInformation.OSArchitecture;
        if (arch == Architecture.X64 && Token(name, "arm|arm32|arm64|aarch64|aarch32") && !Token(name, "x64|amd64|x86_64|universal|allarch")) return false;
        if (arch == Architecture.X86 && Token(name, "x64|amd64|x86_64|arm|arm32|arm64|aarch64|aarch32") && !Token(name, "x86(?![_-]64)|i386|i686|win32|universal|allarch")) return false;
        return true;
    }
    public static List<Asset> Candidates(IEnumerable<Asset> assets, bool showAll = false, Architecture? architecture = null)
    {
        static bool RawPlugin(Asset asset) => asset.OriginHost == "d2rloader.net" && (asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || asset.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        var arch = architecture ?? RuntimeInformation.OSArchitecture;
        int Rank(Asset asset) => !ForWindows(asset.Name, arch) ? 100 : arch == Architecture.X64 && Token(asset.Name, "x64|amd64|x86_64|win64") || arch == Architecture.Arm64 && Token(asset.Name, "arm64|aarch64") ? 0 : Token(asset.Name, "x86(?![_-]64)|i386|i686|win32") ? 30 : 10;
        return assets.Where(a => (Repositories.IsWindowsAsset(a.Name) || RawPlugin(a)) && (showAll || RawPlugin(a) || ForWindows(a.Name, arch))).OrderBy(Rank).ToList();
    }
}
