using System.Text.RegularExpressions;

namespace ReleaseDock;

public sealed record RepositoryRef(string Host, string Path)
{
    public bool IsWebsite => Host == "website";
    public string Name => Host switch { "website" => "Website", "d2rloader.net" => "D2RLoader", "codeberg.org" => "Codeberg", "gitlab.com" => "GitLab", "nexusmods.com" => "Nexus Mods", _ => "GitHub" };
    public string WebUrl => IsWebsite ? Path : $"https://{Host}/{Path}";
    public string Key => IsWebsite ? "website:" + Path : Host == "github.com" ? Path : WebUrl;
    public string ReleasesUrl => IsWebsite ? WebUrl : Host == "d2rloader.net" ? WebUrl : Host == "nexusmods.com" ? WebUrl + "?tab=files" : WebUrl + (Host == "gitlab.com" ? "/-/releases" : "/releases");
    public string ApiUrl => IsWebsite ? WebUrl : Host switch { "d2rloader.net" => "https://d2rloader.net/api/v1/" + Path, "nexusmods.com" => "https://api.nexusmods.com/v1/games/" + Path.Split('/')[0] + "/mods/" + Path.Split('/')[2], "codeberg.org" => $"https://codeberg.org/api/v1/repos/{Path}", "gitlab.com" => "https://gitlab.com/api/v4/projects/" + Uri.EscapeDataString(Path), _ => $"https://api.github.com/repos/{Path}" };
}

public static class Repositories
{
    public static RepositoryRef Resolve(string input)
    {
        input = input.Trim(); if (input.StartsWith("website:", StringComparison.Ordinal)) return new("website", WebsiteSource.Url(input.Substring(8)).AbsoluteUri); if (input.StartsWith("www.nexusmods.com/", StringComparison.OrdinalIgnoreCase) || input.StartsWith("nexusmods.com/", StringComparison.OrdinalIgnoreCase) || input.StartsWith("d2rloader.net/", StringComparison.OrdinalIgnoreCase) || input.StartsWith("www.d2rloader.net/", StringComparison.OrdinalIgnoreCase)) input = "https://" + input; var host = "github.com";
        if (!input.Contains("://") && (input.StartsWith("codeberg.org/", StringComparison.OrdinalIgnoreCase) || input.StartsWith("gitlab.com/", StringComparison.OrdinalIgnoreCase) || input.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))) input = "https://" + input;
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            host = uri.Host.ToLowerInvariant(); if (host == "www.nexusmods.com") host = "nexusmods.com"; if (host == "www.d2rloader.net") host = "d2rloader.net";
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || host is not ("github.com" or "codeberg.org" or "gitlab.com" or "nexusmods.com" or "d2rloader.net")) throw new ArgumentException("Use an HTTPS project link from a supported source: GitHub, Codeberg, GitLab, D2RLoader or Nexus Mods (test builds).");
            var path = uri.AbsolutePath.Trim('/');
            if (host == "d2rloader.net")
            {
                var mod = Regex.Match(path, @"^(?:api/v1/)?hub/plugins/([a-z0-9][a-z0-9_-]*)$", RegexOptions.IgnoreCase);
                if (!mod.Success) throw new ArgumentException("Paste an individual D2RLoader mod-page URL, such as https://d2rloader.net/hub/plugins/example.");
                return new(host, "hub/plugins/" + mod.Groups[1].Value.ToLowerInvariant());
            }
            if (host == "nexusmods.com")
            {
                var mod = Regex.Match(path, @"^(?:games/)?([a-z0-9_-]+)/mods/([1-9][0-9]*)/?$", RegexOptions.IgnoreCase);
                if (!mod.Success || !long.TryParse(mod.Groups[2].Value, out _)) throw new ArgumentException("Use a Nexus mod link such as https://www.nexusmods.com/game/mods/123.");
                return new(host, mod.Groups[1].Value.ToLowerInvariant() + "/mods/" + mod.Groups[2].Value);
            }
            if (host == "gitlab.com") { var marker = path.IndexOf("/-/", StringComparison.Ordinal); if (marker >= 0) path = path[..marker]; }
            else path = string.Join("/", path.Split('/').Take(2));
            input = path;
        }
        if (input.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) input = input[..^4];
        var parts = input.Split('/');
        if (parts.Length < 2 || host != "gitlab.com" && parts.Length != 2 || parts.Any(p => p is "." or ".." || !Regex.IsMatch(p, @"^[A-Za-z0-9_][A-Za-z0-9_.-]*$"))) throw new ArgumentException("Enter a project link, or owner/repository for GitHub.");
        return new(host, input);
    }
    public static string Parse(string input) => Resolve(input).Key;
    public static bool IsWindowsAsset(string name) => new[] { ".exe", ".msi", ".msix", ".msixbundle", ".zip", ".7z", ".rar" }.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase));
}
