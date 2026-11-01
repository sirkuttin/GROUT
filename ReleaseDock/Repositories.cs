using System.Text.RegularExpressions;

namespace ReleaseDock;

public sealed record RepositoryRef(string Host, string Path)
{
    public string Name => Host switch { "codeberg.org" => "Codeberg", "gitlab.com" => "GitLab", _ => "GitHub" };
    public string WebUrl => $"https://{Host}/{Path}";
    public string Key => Host == "github.com" ? Path : WebUrl;
    public string ReleasesUrl => WebUrl + (Host == "gitlab.com" ? "/-/releases" : "/releases");
    public string ApiUrl => Host switch { "codeberg.org" => $"https://codeberg.org/api/v1/repos/{Path}", "gitlab.com" => "https://gitlab.com/api/v4/projects/" + Uri.EscapeDataString(Path), _ => $"https://api.github.com/repos/{Path}" };
}

public static class Repositories
{
    public static RepositoryRef Resolve(string input)
    {
        input = input.Trim(); var host = "github.com";
        if (!input.Contains("://") && (input.StartsWith("codeberg.org/", StringComparison.OrdinalIgnoreCase) || input.StartsWith("gitlab.com/", StringComparison.OrdinalIgnoreCase) || input.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))) input = "https://" + input;
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            host = uri.Host.ToLowerInvariant();
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || host is not ("github.com" or "codeberg.org" or "gitlab.com")) throw new ArgumentException("Use a public HTTPS project link from GitHub, Codeberg or GitLab.com.");
            var path = uri.AbsolutePath.Trim('/');
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
    public static bool IsWindowsAsset(string name) => new[] { ".exe", ".msi", ".msix", ".msixbundle", ".zip", ".7z" }.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase));
}
