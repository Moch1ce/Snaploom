using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Globalization;
using Snaploom.Core;

namespace Snaploom.App;

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    NetworkFailure,
    RateLimited,
    InvalidResponse,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version? LatestVersion = null,
    DateTimeOffset? PublishedAt = null,
    string? ReleaseNotes = null,
    Uri? ReleasePage = null);

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class UpdateCheckService : IUpdateCheckService
{
    private static readonly Uri ReleasesEndpoint = new(
        $"https://api.github.com/repos/{ProductIdentity.GitHubOwner}/" +
        $"{ProductIdentity.GitHubRepository}/releases?per_page=20");

    private readonly HttpClient _httpClient;
    private readonly Version _currentVersion;

    public UpdateCheckService(HttpClient httpClient, Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(currentVersion);
        _httpClient = httpClient;
        _currentVersion = NormalizeVersion(currentVersion);
    }

    public static UpdateCheckService CreateDefault()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
        return new UpdateCheckService(client, version);
    }

    public async Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
            request.Headers.UserAgent.ParseAdd($"{ProductIdentity.Name}/{_currentVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2026-03-10");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Forbidden or
                HttpStatusCode.TooManyRequests)
            {
                return new UpdateCheckResult(UpdateCheckStatus.RateLimited);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NetworkFailure);
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                content,
                cancellationToken: cancellationToken);
            return ParseResponse(document.RootElement);
        }
        catch (JsonException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.InvalidResponse);
        }
        catch (HttpRequestException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NetworkFailure);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NetworkFailure);
        }
    }

    private UpdateCheckResult ParseResponse(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Array)
        {
            return new UpdateCheckResult(UpdateCheckStatus.InvalidResponse);
        }

        UpdateCheckResult? latest = null;
        foreach (var release in response.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) &&
                draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var result = ParseRelease(release);
            if (result.Status != UpdateCheckStatus.InvalidResponse &&
                result.LatestVersion is not null &&
                (latest?.LatestVersion is null ||
                 result.LatestVersion.CompareTo(latest.LatestVersion) > 0))
            {
                latest = result;
            }
        }

        return latest ?? new UpdateCheckResult(UpdateCheckStatus.InvalidResponse);
    }

    private UpdateCheckResult ParseRelease(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object ||
            !TryGetStringProperty(release, "tag_name", out var tag) ||
            !TryParseVersion(tag, out var latestVersion) ||
            !TryGetStringProperty(release, "published_at", out var published) ||
            !DateTimeOffset.TryParse(
                published,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var publishedAt) ||
            !TryGetStringProperty(release, "html_url", out var page) ||
            !TryParseReleasePage(page, tag!, out var releasePage))
        {
            return new UpdateCheckResult(UpdateCheckStatus.InvalidResponse);
        }

        var notes = string.Empty;
        if (release.TryGetProperty("body", out var notesElement))
        {
            if (notesElement.ValueKind == JsonValueKind.String)
            {
                notes = notesElement.GetString() ?? string.Empty;
            }
            else if (notesElement.ValueKind != JsonValueKind.Null)
            {
                return new UpdateCheckResult(UpdateCheckStatus.InvalidResponse);
            }
        }
        if (notes.Length > 100_000)
        {
            return new UpdateCheckResult(UpdateCheckStatus.InvalidResponse);
        }
        var status = latestVersion > _currentVersion
            ? UpdateCheckStatus.UpdateAvailable
            : UpdateCheckStatus.UpToDate;
        return new UpdateCheckResult(
            status,
            latestVersion,
            publishedAt,
            notes,
            releasePage);
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var value = tag.Trim();
        if (value.StartsWith("snaploom-", StringComparison.OrdinalIgnoreCase))
        {
            value = value["snaploom-".Length..];
        }

        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            value = value[1..];
        }

        if (value.Contains('-', StringComparison.Ordinal) ||
            value.Split('.').Length != 3 ||
            !Version.TryParse(value, out var parsed) ||
            parsed.Major < 0 || parsed.Minor < 0)
        {
            return false;
        }

        version = NormalizeVersion(parsed);
        return true;
    }

    private static bool TryParseReleasePage(
        string? value,
        string tag,
        out Uri releasePage)
    {
        releasePage = new Uri("https://github.com");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                parsed.AbsolutePath,
                $"/{ProductIdentity.GitHubOwner}/{ProductIdentity.GitHubRepository}/releases/tag/{tag}",
                StringComparison.Ordinal))
        {
            return false;
        }

        releasePage = parsed;
        return true;
    }

    private static bool TryGetStringProperty(
        JsonElement value,
        string propertyName,
        out string? propertyValue)
    {
        propertyValue = null;
        if (!value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        propertyValue = property.GetString();
        return !string.IsNullOrWhiteSpace(propertyValue);
    }

    private static Version NormalizeVersion(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));
}
