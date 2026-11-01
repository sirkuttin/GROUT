using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

const string Usage = @"
G.R.O.U.T. Release Tool
Build, test, and optionally publish a Windows x64 EXE under 1 MB.
Uses Windows-provided .NET Framework 4.8; no bundled runtime.

USAGE
  dotnet script ./release.csx -- [options]

OPTIONS
  --version X.Y.Z    Defaults to the project version; must match it.
  --notes FILE       Release notes Markdown file. Required to publish.
  --repo OWNER/REPO  Default: sirkuttin/GROUT. Must match origin.
  --publish          Push main and publish the verified GitHub release.
                     Without this flag, only build and test locally.
  -h, --help         Show this help.

EXAMPLES  (from the scripts folder)
  Show help:
    dotnet script ./release.csx -- --help

  Build and test locally:
    dotnet script ./release.csx

  Publish after committing the new version and release notes:
    dotnet script ./release.csx -- --notes ../releases/v0.6.0.md --publish

REQUIREMENTS
  .NET 10 SDK, dotnet-script, Git, and authenticated GitHub CLI.
  Run 'dotnet tool restore' to install the pinned local script runner.
  Publishing requires a clean main branch. Existing releases are never
  overwritten. Repository visibility is unchanged.
  Existing local/remote version tags must match the tested commit.

OUTPUT
  outputs/releases/<version>/ in the checkout.
  Successful builds remove their temporary EXE and test report.
  Failed builds remain under work/ for troubleshooting.
  Run from anywhere inside the checkout. Unknown or duplicate flags fail.
";
async Task<string> Run(string command, params string[] arguments)
{
    var start = new ProcessStartInfo(command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in arguments) start.ArgumentList.Add(arg);
    using var process = Process.Start(start) ?? throw new Exception("Could not start " + command);
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var text = await stdout; var error = await stderr;
    if (process.ExitCode != 0) throw new Exception(command + " failed: " + text + error);
    return text.Trim();
}
// Pure tag checks are kept separate so their edge cases can be tested offline.
string RemoteTagCommit(string refs, string tag)
{
    var name = "refs/tags/" + tag;
    var records = refs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length == 2).ToList();
    // Annotated tags advertise a tag-object SHA plus a peeled commit SHA.
    return records.FirstOrDefault(parts => parts[1] == name + "^{}")?[0]
        ?? records.FirstOrDefault(parts => parts[1] == name)?[0];
}
void AssertTagCommit(string tag, string testedCommit, string localCommit, string remoteCommit, bool requireRemote)
{
    if (localCommit != null && !localCommit.Equals(testedCommit, StringComparison.OrdinalIgnoreCase)) throw new Exception("Local tag " + tag + " points to a different commit. Existing tags are never moved automatically.");
    if (remoteCommit != null && !remoteCommit.Equals(testedCommit, StringComparison.OrdinalIgnoreCase)) throw new Exception("Remote tag " + tag + " points to a different commit. Refusing to publish binaries against unrelated source.");
    if (requireRemote && remoteCommit == null) throw new Exception("Published release tag " + tag + " could not be verified on origin.");
}
async Task ValidateTagCommit(string tag, string testedCommit, bool requireRemote = false)
{
    var localTag = await Run("git", "tag", "--list", tag);
    var localCommit = localTag.Length == 0 ? null : await Run("git", "rev-parse", "refs/tags/" + tag + "^{commit}");
    var remoteRefs = await Run("git", "-c", "credential.helper=", "-c", "credential.helper=!gh auth git-credential", "ls-remote", "--tags", "origin", "refs/tags/" + tag, "refs/tags/" + tag + "^{}");
    AssertTagCommit(tag, testedCommit, localCommit, RemoteTagCommit(remoteRefs, tag), requireRemote);
}
bool ReleaseOutputInUse(string output, IEnumerable<string> names)
{
    foreach (var name in names)
    {
        var path = Path.Combine(output, name); if (!File.Exists(path)) continue;
        try { using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
    return false;
}
try
{
    var values = new Dictionary<string, string>(); var publish = false; var help = false;
    for (int i = 0; i < Args.Count; i++)
    {
        var flag = Args[i];
        if (flag == "--help" || flag == "-h") { help = true; continue; }
        if (flag == "--publish" && !publish) { publish = true; continue; }
        if (!new[] { "--version", "--notes", "--repo" }.Contains(flag) || values.ContainsKey(flag) || i + 1 >= Args.Count || Args[i + 1].StartsWith("--"))
            throw new ArgumentException("Unknown, duplicate or incomplete flag: " + flag);
        values.Add(flag, Args[++i]);
    }
    if (help) { Console.WriteLine(Usage); return; }
    var root = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (root != null && !File.Exists(Path.Combine(root.FullName, "ReleaseDock", "ReleaseDock.csproj"))) root = root.Parent;
    if (root == null) throw new Exception("Run from within the GROUT checkout.");
    var notes = values.ContainsKey("--notes") ? Path.GetFullPath(values["--notes"]) : "";
    Directory.SetCurrentDirectory(root.FullName);
    var project = Path.Combine(root.FullName, "ReleaseDock", "ReleaseDock.csproj");
    var version = XDocument.Load(project).Descendants("Version").Single().Value;
    if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$") || values.ContainsKey("--version") && values["--version"] != version) throw new Exception("Version must match the project's X.Y.Z version.");
    var repo = values.ContainsKey("--repo") ? values["--repo"] : "sirkuttin/GROUT";
    if (!Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new Exception("Invalid --repo.");
    var tag = "v" + version;
    var commit = await Run("git", "rev-parse", "HEAD");
    if (publish)
    {
        if (!File.Exists(notes) || string.IsNullOrWhiteSpace(File.ReadAllText(notes))) throw new Exception("--notes must name a nonempty release notes file.");
        if ((await Run("git", "status", "--porcelain")).Length > 0) throw new Exception("Commit all changes before publishing.");
        if (await Run("git", "branch", "--show-current") != "main") throw new Exception("Publish from main.");
        var origin = await Run("git", "remote", "get-url", "origin");
        if (origin != "https://github.com/" + repo + ".git" && origin != "git@github.com:" + repo + ".git") throw new Exception("Origin does not match --repo.");
        await Run("gh", "api", "user", "--jq", ".login");
        await ValidateTagCommit(tag, commit);
        using var tags = JsonDocument.Parse(await Run("gh", "release", "list", "--repo", repo, "--limit", "1000", "--json", "tagName"));
        if (tags.RootElement.EnumerateArray().Any(r => r.GetProperty("tagName").GetString() == tag)) throw new Exception("Release already exists; existing releases are never overwritten.");
    }
    var build = Path.Combine(root.FullName, "work", "release-" + version + "-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(build);
    Console.WriteLine("Building " + tag + " as a single EXE…");
    Console.WriteLine(await Run("dotnet", "build", project, "-c", "Release", "-p:PublishProfile=Portable-win-x64", "-p:NexusTestingEnabled=false", "-o", build, "--source", "https://api.nuget.org/v3/index.json"));
    var exe = Path.Combine(build, "GROUT.exe");
    if (!File.Exists(exe) || Directory.GetFiles(build).Length != 1 || Directory.GetDirectories(build).Length != 0) throw new Exception("Build must contain exactly GROUT.exe, without runtime DLLs or a config file.");
    if (new FileInfo(exe).Length >= 1_000_000) throw new Exception("GROUT.exe exceeds the under-1-MB size budget.");
    Console.WriteLine("Standalone EXE: " + new FileInfo(exe).Length.ToString("N0") + " bytes (uses Windows .NET Framework 4.8).");
    var report = Path.Combine(build, "TEST-RESULTS.txt");
    Console.WriteLine("Running packaged regression tests…");
    try { await Run(exe, "--self-test", report); }
    catch { throw new Exception(File.Exists(report) ? "Packaged tests failed:\n" + File.ReadAllText(report) : "Packaged tests failed without a report. Build directory: " + build); }
    if (!File.Exists(report) || !File.ReadAllText(report).StartsWith("PASS:")) throw new Exception("Packaged self-tests did not pass.");
    Console.WriteLine(File.ReadAllText(report));
    var output = Path.Combine(root.FullName, "outputs", "releases", version); Directory.CreateDirectory(output);
    if (ReleaseOutputInUse(output, new[] { "GROUT.exe", "GROUT-" + version + "-win-x64.exe" }))
    {
        output = Path.Combine(output, "build-" + commit[..12] + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(output);
        Console.WriteLine("Existing release EXE is in use; writing to " + output);
    }
    var assetName = "GROUT.exe";
    var asset = Path.Combine(output, assetName); File.Copy(exe, asset, true);
    // GROUT 0.5.0 looks for a versioned asset. Keep an identical compatibility
    // copy so those installations can reach the stable-filename updater.
    var compatibilityName = "GROUT-" + version + "-win-x64.exe";
    var compatibility = Path.Combine(output, compatibilityName); File.Copy(exe, compatibility, true);
    var tests = Path.Combine(output, "GROUT-" + version + "-TEST-RESULTS.txt"); File.Copy(report, tests, true);
    string hash; using (var stream = File.OpenRead(asset)) hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    var checksums = Path.Combine(output, "GROUT-" + version + "-SHA256SUMS.txt"); File.WriteAllText(checksums, hash + "  " + assetName + "\n" + hash + "  " + compatibilityName + "\n");
    var licenses = Path.Combine(output, "GROUT-" + version + "-LICENSES.txt");
    File.WriteAllText(licenses, File.ReadAllText(Path.Combine(root.FullName, "LICENSE")) + "\n\n" + string.Join("\n\n", Directory.GetFiles(Path.Combine(root.FullName, "licenses"), "*.txt").OrderBy(p => p).Select(p => Path.GetFileName(p) + "\n\n" + File.ReadAllText(p))));
    // Remove only the two files created by this successful invocation.
    // Never recursively remove the work directory or other build artifacts.
    try
    {
        var workRoot = Path.GetFullPath(Path.Combine(root.FullName, "work")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(build).StartsWith(workRoot, StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(build) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unexpected build cleanup path.");
        File.Delete(exe); File.Delete(report); Directory.Delete(build);
        Console.WriteLine("Temporary build files cleaned.");
    }
    catch (IOException ex) { Console.WriteLine("Build cleanup deferred: " + ex.Message); }
    catch (UnauthorizedAccessException ex) { Console.WriteLine("Build cleanup deferred: " + ex.Message); }
    if (!publish) { Console.WriteLine("Verified local artifacts: " + output + "\nNothing pushed or published."); return; }
    if (await Run("git", "rev-parse", "HEAD") != commit || (await Run("git", "status", "--porcelain")).Length > 0) throw new Exception("Checkout changed during build; publication stopped.");
    Console.WriteLine("Pushing main and uploading draft release assets…");
    await Run("git", "-c", "credential.helper=", "-c", "credential.helper=!gh auth git-credential", "push", "origin", "main");
    await ValidateTagCommit(tag, commit);
    await Run("gh", "release", "create", tag, asset, compatibility + "#Compatibility copy for older GROUT updaters", checksums, tests, licenses, "--repo", repo, "--target", commit, "--title", "G.R.O.U.T. " + version, "--notes-file", notes, "--draft");
    using var release = JsonDocument.Parse(await Run("gh", "release", "view", tag, "--repo", repo, "--json", "assets,targetCommitish"));
    if (release.RootElement.GetProperty("targetCommitish").GetString() != commit) throw new Exception("Draft release target differs from the tested commit; release left as draft.");
    var expected = new[] { asset, compatibility, checksums, tests, licenses };
    foreach (var file in expected)
    {
        var remote = release.RootElement.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == Path.GetFileName(file));
        string digest; using (var stream = File.OpenRead(file)) digest = "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (remote.GetProperty("state").GetString() != "uploaded" || remote.GetProperty("size").GetInt64() != new FileInfo(file).Length || remote.GetProperty("digest").GetString() != digest) throw new Exception("Asset verification failed; release left as draft.");
    }
    await ValidateTagCommit(tag, commit);
    await Run("gh", "release", "edit", tag, "--repo", repo, "--draft=false", "--latest");
    await ValidateTagCommit(tag, commit, requireRemote: true);
    using var published = JsonDocument.Parse(await Run("gh", "release", "view", tag, "--repo", repo, "--json", "isDraft,url"));
    if (published.RootElement.GetProperty("isDraft").GetBoolean()) throw new Exception("Release is still a draft.");
    // The compatibility asset is retained on GitHub, not as a duplicate local EXE.
    // Remove only this run's generated copy, and leave it alone if modified/locked.
    try
    {
        string compatibilityHash; using (var stream = File.OpenRead(compatibility)) compatibilityHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (compatibilityHash == hash) File.Delete(compatibility);
    }
    catch (IOException ex) { Console.WriteLine("Compatibility-copy cleanup deferred: " + ex.Message); }
    catch (UnauthorizedAccessException ex) { Console.WriteLine("Compatibility-copy cleanup deferred: " + ex.Message); }
    Console.WriteLine("Published and verified: " + published.RootElement.GetProperty("url").GetString());
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message + "\n\n" + Usage);
    Environment.ExitCode = 1;
}
