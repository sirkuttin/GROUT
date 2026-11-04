using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace ReleaseDock;

public static class WebsiteSource
{
    private static readonly Regex VersionToken = new(@"\d+\.\d+(?:\.\d+){0,2}", RegexOptions.CultureInvariant);
    public static Uri Url(string text)
    {
        if (text.Length > 8192) throw new ArgumentException("Website URL is too long.");
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)) throw new ArgumentException("Enter a complete HTTPS website URL.");
        ReleaseClient.ValidateUrl(uri);
        if (uri.Fragment.Length > 0) throw new ArgumentException("Use a website URL without a fragment (#).");
        return uri;
    }
    public static string SourceKey(string text)
    {
        if (text.StartsWith("website:", StringComparison.Ordinal)) return Repositories.Parse(text);
        try { return Repositories.Parse(text); }
        catch (ArgumentException) when (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == "https" && !new[] { "github.com", "codeberg.org", "gitlab.com", "d2rloader.net", "www.d2rloader.net", "nexusmods.com", "www.nexusmods.com" }.Contains(uri.Host.ToLowerInvariant()))
        { return "website:" + Url(text).AbsoluteUri; }
    }
    public static Release Parse(string page, string seed, string html)
    {
        var pageUri = Url(page); var sample = Url(seed);
        var sampleName = Path.GetFileName(sample.AbsolutePath);
        if (!Repositories.IsWindowsAsset(sampleName)) throw new ArgumentException("Choose a direct Windows app download (EXE, MSI or ZIP, for example).");
        var versions = VersionToken.Matches(sample.AbsolutePath);
        if (versions.Count != 1) throw new ArgumentException("The download URL must contain one version such as 1.0.37. Fixed latest-download URLs are not supported yet.");
        var token = versions[0];
        if (!System.Version.TryParse(token.Value, out _)) throw new ArgumentException("The download URL contains an invalid version number.");
        var pattern = "^" + Regex.Escape(sample.AbsolutePath.Substring(0, token.Index)) + @"(?<version>\d+\.\d+(?:\.\d+){0,2})" + Regex.Escape(sample.AbsolutePath.Substring(token.Index + token.Length)) + "$";
        var matches = new Dictionary<string, (string Version, Uri Url)>();
        html = Regex.Replace(html, @"<!--.*?-->|<(script|style)\b[^>]*>.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
        foreach (Match anchor in Regex.Matches(html, @"<a\b[^>]*\s+href\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)'|(?<url>[^\s>]+))", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
        {
            if (!Uri.TryCreate(pageUri, WebUtility.HtmlDecode(anchor.Groups["url"].Value), out var link)) continue;
            if (link.Host != sample.Host || link.Query != sample.Query || link.Fragment.Length > 0) continue;
            try { Url(link.AbsoluteUri); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { continue; }
            var match = Regex.Match(link.AbsolutePath, pattern, RegexOptions.CultureInvariant);
            if (match.Success && System.Version.TryParse(match.Groups["version"].Value, out _)) matches[link.AbsoluteUri] = (match.Groups["version"].Value, link);
        }
        if (matches.Count == 0) throw new InvalidOperationException("No matching download link found on this page. Check the page and download URLs in App settings. JavaScript-generated links are not supported.");
        var candidates = matches.Values.OrderByDescending(m => System.Version.Parse(m.Version)).ToList();
        var latest = candidates[0];
        if (candidates.Count(m => System.Version.Parse(m.Version) == System.Version.Parse(latest.Version)) > 1) throw new InvalidOperationException("Multiple downloads match the newest version. Choose a more specific download URL.");
        var title = Regex.Replace(Path.GetFileNameWithoutExtension(sampleName), @"[-_. ]?" + Regex.Escape(token.Value) + @".*$", "");
        title = Regex.Replace(title, @"[-_. ]+(?:setup|installer)$", "", RegexOptions.IgnoreCase);
        return new(latest.Version, pageUri.AbsoluteUri, "Download discovered on " + pageUri.AbsoluteUri, [new(Path.GetFileName(latest.Url.AbsolutePath), latest.Url.AbsoluteUri, -1, null, "website")], Title: title);
    }
}
