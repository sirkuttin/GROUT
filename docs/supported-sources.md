# Supported sources

[← Back to README](../README.md) · [User guide](user-guide.md)

GROUT tracks **public releases** on these hosts:

| Host | Accepted input | Preview handling |
| --- | --- | --- |
| GitHub | Repository/release URLs or `owner/repository` | Native prerelease metadata; drafts excluded |
| Codeberg.org | Repository/release URLs | Previews included when enabled; drafts excluded |
| GitLab.com | Project/release URLs, including nested subgroups | Common preview markers in tags |
| D2RLoader (0.8.0 test builds) | Mod page or individual plugin API URL | Published releases only; common preview markers in versions |

```text
https://github.com/owner/app
https://codeberg.org/owner/app/releases
https://gitlab.com/group/subgroup/app/-/releases
```

Host identity is retained, so equal owner/project names on different hosts remain separate library entries.

## Nexus Mods (disabled in public builds)

Public builds keep this integration disabled pending registration. In explicitly enabled private test builds, paste a mod-page link such as `https://www.nexusmods.com/diablo2resurrected/mods/169`. Nexus requires an account/API connection. GROUT tracks the chosen file and follows the publisher's file-update chain, independently of optional files. Direct downloads require Premium; free accounts open Nexus in a browser. See the [Nexus guide](nexus-mods.md). This integration requires Nexus application registration before wider public release.

## Release selection

Stable releases are the default. Enable **Include preview releases** per app when desired, including projects whose only releases are previews.

GitLab lacks the same native prerelease flag as GitHub/Codeberg. GROUT excludes common markers such as `alpha`, `beta`, `rc`, `preview`, `nightly` and `dev` from stable selection. This heuristic cannot recognize every naming scheme. Future-dated/upcoming GitLab releases remain excluded.

Selection examines up to 100 releases returned by each provider's list endpoint. Public API rate limits apply; pauses affect their source rather than stopping all hosts.

## Windows downloads

The normal picker hides clearly named Linux/macOS/mobile downloads, source/symbol archives and incompatible architectures. Unlabelled archives remain candidates: an extension alone cannot prove Windows compatibility.

**Show all downloads** exposes hidden candidates for unusual filenames and other platforms. This preference is saved per app. Confirm the architecture and file type before installation.

GitLab release asset links can have friendly labels; GROUT derives a filename when possible. Automatically generated source-code archives are not app downloads. Linked HTTPS assets/CDNs are supported; HTML or sign-in pages are rejected instead of being saved as EXEs.

## Current limits

- No private app repositories, self-hosted Gitea/Forgejo/GitLab servers, standalone download URLs or website scraping.
- EXE, MSI and ZIP files have the update methods in the [user guide](user-guide.md#configure-future-updates). Formats such as MSIX, 7z and RAR require manual handling.
- An EXE can be an installer or a portable app; choose its purpose yourself.
- Source metadata redirects stay on the same API host. Download redirects reject HTTPS-to-HTTP downgrade, embedded credentials and obvious local/private literal addresses.
- Version comparison supports common schemes, not every arbitrary publisher tag.

## File validation

Tracked-app downloads are checked against the published byte count and SHA-256 digest when provided. If the API lacks a byte count, HTTP content length is checked when available; otherwise the UI shows **size unknown**. A cached download is reused only when its tag, name, size and supplied SHA-256 still match.

ZIP inspection rejects unsafe paths, Windows device names, duplicate/colliding paths, archive links and linked destination paths. Limits are 10,000 entries and 8 GB uncompressed data. Download cancellation preserves destination files; file application completes or attempts rollback once started.

A developer can still publish an unsuitable package. Choose the release file and destination carefully; included archive files overwrite matching files. GROUT does not forcibly shut down third-party apps or silently elevate file updates.

## D2RLoader

Mod page and detail API URLs map to the same tracked item. **Browse D2RLoader…** in Add App reads the paginated catalog on demand. Checks use the chosen mod detail endpoint rather than scanning the catalog. Downloads use the release-ID endpoint with exact byte-size and SHA-256 verification. Loader requirements and source scan warnings are preserved. See the [D2RLoader guide](d2rloader.md).
