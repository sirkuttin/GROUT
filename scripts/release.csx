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

const string Usage = "Usage: dotnet tool run dotnet-script -- scripts/release.csx -- [--version X.Y.Z] [--notes FILE] [--repo OWNER/REPO] [--publish]\nBuilds and tests a single-EXE Windows x64 release. Without --publish, nothing is pushed or published.\n--version must match the project version (defaults to it). --notes is required to publish.\n--repo defaults to sirkuttin/GROUT and must match origin. Publishing requires a clean main branch.\n-h, --help: show this help. Unknown/duplicate flags fail. Run from within the checkout.\nPrerequisites: .NET 10 SDK, dotnet tool restore, Git and authenticated GitHub CLI.";
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
        using var tags = JsonDocument.Parse(await Run("gh", "release", "list", "--repo", repo, "--limit", "1000", "--json", "tagName"));
        if (tags.RootElement.EnumerateArray().Any(r => r.GetProperty("tagName").GetString() == tag)) throw new Exception("Release already exists; existing releases are never overwritten.");
    }
    var build = Path.Combine(root.FullName, "work", "release-" + version + "-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(build);
    Console.WriteLine("Building " + tag + " as a single EXE…");
    Console.WriteLine(await Run("dotnet", "publish", project, "-c", "Release", "-p:PublishProfile=Portable-win-x64", "-o", build, "--source", "https://api.nuget.org/v3/index.json"));
    var exe = Path.Combine(build, "GROUT.exe");
    if (!File.Exists(exe) || Directory.GetFiles(build).Length != 1 || Directory.GetDirectories(build).Length != 0) throw new Exception("Publish must contain exactly GROUT.exe.");
    var report = Path.Combine(build, "TEST-RESULTS.txt");
    Console.WriteLine("Running packaged regression tests…");
    await Run(exe, "--self-test", report);
    if (!File.Exists(report) || !File.ReadAllText(report).StartsWith("PASS:")) throw new Exception("Packaged self-tests did not pass.");
    Console.WriteLine(File.ReadAllText(report));
    var output = Path.Combine(root.FullName, "outputs", "releases", version); Directory.CreateDirectory(output);
    var assetName = "GROUT-" + version + "-win-x64.exe";
    var asset = Path.Combine(output, assetName); File.Copy(exe, asset, true);
    var tests = Path.Combine(output, "GROUT-" + version + "-TEST-RESULTS.txt"); File.Copy(report, tests, true);
    string hash; using (var stream = File.OpenRead(asset)) hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    var checksums = Path.Combine(output, "GROUT-" + version + "-SHA256SUMS.txt"); File.WriteAllText(checksums, hash + "  " + assetName + "\n");
    if (!publish) { Console.WriteLine("Verified local artifacts: " + output + "\nNothing pushed or published."); return; }
    if (await Run("git", "rev-parse", "HEAD") != commit || (await Run("git", "status", "--porcelain")).Length > 0) throw new Exception("Checkout changed during build; publication stopped.");
    Console.WriteLine("Pushing main and uploading draft release assets…");
    await Run("git", "-c", "credential.helper=", "-c", "credential.helper=!gh auth git-credential", "push", "origin", "main");
    await Run("gh", "release", "create", tag, asset, checksums, tests, "--repo", repo, "--target", commit, "--title", "G.R.O.U.T. " + version, "--notes-file", notes, "--draft");
    using var release = JsonDocument.Parse(await Run("gh", "release", "view", tag, "--repo", repo, "--json", "assets"));
    var expected = new[] { asset, checksums, tests };
    foreach (var file in expected)
    {
        var remote = release.RootElement.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == Path.GetFileName(file));
        string digest; using (var stream = File.OpenRead(file)) digest = "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (remote.GetProperty("state").GetString() != "uploaded" || remote.GetProperty("size").GetInt64() != new FileInfo(file).Length || remote.GetProperty("digest").GetString() != digest) throw new Exception("Asset verification failed; release left as draft.");
    }
    await Run("gh", "release", "edit", tag, "--repo", repo, "--draft=false", "--latest");
    using var published = JsonDocument.Parse(await Run("gh", "release", "view", tag, "--repo", repo, "--json", "isDraft,url"));
    if (published.RootElement.GetProperty("isDraft").GetBoolean()) throw new Exception("Release is still a draft.");
    Console.WriteLine("Published and verified: " + published.RootElement.GetProperty("url").GetString());
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message + "\n\n" + Usage);
    Environment.ExitCode = 1;
}
