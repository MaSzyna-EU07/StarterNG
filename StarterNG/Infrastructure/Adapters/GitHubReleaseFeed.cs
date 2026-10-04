using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;

namespace StarterNG.Infrastructure.Adapters;

public sealed class GitHubReleaseFeed : IReleaseFeed
{
    private const string ReleasesUrl = "https://api.github.com/repos/MaSzyna-EU07/StarterNG/releases/";

    /// <summary>The tag CI keeps recreating for every build of the staging branch.</summary>
    public const string StagingTag = "staging";

    private readonly HttpClient _http;
    private readonly IDiagnosticsLog _log;

    public GitHubReleaseFeed(IDiagnosticsLog log, Version current, HttpMessageHandler? handler = null)
    {
        _log = log;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);

        // The API turns away requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StarterNG", current.ToString()));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseLookup> LatestAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        // "latest" is GitHub's own pick: the newest release that is not a pre-release.
        string url = ReleasesUrl + (channel == UpdateChannel.Staging ? "tags/" + StagingTag : "latest");

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);

            // Nothing released yet.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ReleaseLookup.Found(null);

            response.EnsureSuccessStatusCode();
            return ReleaseLookup.Found(Parse(await response.Content.ReadAsStringAsync(cancellationToken), channel));
        }
        // Offline, rate limited or timed out: there is simply no answer this time,
        // and a log entry on every start without a network would only be noise.
        catch (HttpRequestException)
        {
            return ReleaseLookup.NoAnswer;
        }
        catch (TaskCanceledException)
        {
            return ReleaseLookup.NoAnswer;
        }
        catch (Exception ex)
        {
            _log.Log("update check", ex);
            return ReleaseLookup.NoAnswer;
        }
    }

    public static ReleaseInfo? Parse(string json, UpdateChannel channel)
    {
        var release = JsonSerializer.Deserialize(json, GitHubReleaseContext.Default.GitHubReleaseDocument);
        if (release is null || release.Draft ||
            string.IsNullOrEmpty(release.TagName) || string.IsNullOrEmpty(release.HtmlUrl))
            return null;

        bool belongs = channel == UpdateChannel.Staging
            ? release.TagName == StagingTag
            : !release.Prerelease;

        return belongs ? new ReleaseInfo(release.TagName, release.HtmlUrl, release.TargetCommitish) : null;
    }
}

internal sealed class GitHubReleaseDocument
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }

    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }

    [JsonPropertyName("target_commitish")] public string? TargetCommitish { get; set; }

    [JsonPropertyName("draft")] public bool Draft { get; set; }

    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
}

[JsonSerializable(typeof(GitHubReleaseDocument))]
internal partial class GitHubReleaseContext : JsonSerializerContext
{
}
