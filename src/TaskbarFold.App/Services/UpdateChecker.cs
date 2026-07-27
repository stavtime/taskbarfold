using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace TaskbarFold.App.Services;

/// <param name="Version">Latest published version, e.g. <c>0.2.0</c>.</param>
/// <param name="Url">Page to send the user to.</param>
/// <param name="Notes">One-line summary shown in the notification.</param>
public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("notes")] string? Notes);

[JsonSerializable(typeof(UpdateManifest))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;

public enum UpdateResult
{
    /// <summary>Running the newest published version.</summary>
    UpToDate,

    /// <summary>A newer version is published.</summary>
    Available,

    /// <summary>The feed could not be read; nothing can be said either way.</summary>
    Failed,

    /// <summary>An administrator has switched the check off.</summary>
    Disabled,
}

/// <param name="Result">Outcome of the check.</param>
/// <param name="LatestVersion">Version string from the feed, when one was read.</param>
/// <param name="ReleaseUrl">Where to send the user.</param>
/// <param name="Notes">Short description of the release.</param>
public sealed record UpdateStatus(
    UpdateResult Result,
    string? LatestVersion = null,
    string? ReleaseUrl = null,
    string? Notes = null)
{
    public bool IsAvailable => Result == UpdateResult.Available;
}

/// <summary>
/// Asks a small static JSON file whether a newer release exists.
///
/// The feed is a plain file on the project's own site rather than the GitHub API,
/// because the API allows 60 unauthenticated requests per hour per IP: a single office
/// behind one NAT would exhaust that and every machine behind it would silently stop
/// checking. A static file is served from a CDN and has no such limit — and publishing
/// is one file edit, which also makes it possible to hold back or roll back an
/// announcement without touching the release itself.
///
/// Everything here is asynchronous and, for the automatic check, throttled to once a day.
/// taskbar-groups issued a blocking HTTP request from a UI constructor, so a slow network
/// froze the application on startup; nothing in this class is ever awaited by the UI thread.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>Default feed. Overridable by policy for private or staged deployments.</summary>
    public const string DefaultFeedUrl = "https://taskbarfold.duckapps.ru/latest.json";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>Version as shown to the user, without the trailing revision field.</summary>
    public static string CurrentVersionDisplay => CurrentVersion.ToString(3);

    public static string FeedUrl => UpdatePolicy.FeedUrlOverride ?? DefaultFeedUrl;

    /// <summary>Whether the once-a-day automatic check is due.</summary>
    public static bool IsDue(DateTimeOffset? lastCheckUtc) =>
        lastCheckUtc is null || DateTimeOffset.UtcNow - lastCheckUtc.Value >= CheckInterval;

    public async Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (UpdatePolicy.IsCheckDisabled)
        {
            return new UpdateStatus(UpdateResult.Disabled);
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"TaskbarFold/{CurrentVersion}");

            // The feed is small and changes rarely; asking for a fresh copy avoids a
            // proxy holding on to a stale answer for days.
            client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
            };

            var manifest = await client
                .GetFromJsonAsync(FeedUrl, UpdateJsonContext.Default.UpdateManifest, cancellationToken)
                .ConfigureAwait(false);

            if (manifest?.Version is null || !TryParseVersion(manifest.Version, out var latest))
            {
                Log.Warn("The update feed did not contain a usable version.");
                return new UpdateStatus(UpdateResult.Failed);
            }

            if (latest <= Normalize(CurrentVersion))
            {
                return new UpdateStatus(UpdateResult.UpToDate, manifest.Version);
            }

            // Only follow an absolute HTTPS link, whatever the feed says.
            var url = Uri.TryCreate(manifest.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                ? manifest.Url
                : null;

            return new UpdateStatus(UpdateResult.Available, manifest.Version, url, manifest.Notes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                      or System.Text.Json.JsonException or UriFormatException)
        {
            // No network, a captive portal, or a malformed feed. None of these are worth
            // interrupting the user over; the settings window reports it if they asked.
            Log.Info($"Update check failed: {ex.GetType().Name}");
            return new UpdateStatus(UpdateResult.Failed);
        }
    }

    private static bool TryParseVersion(string text, out Version version)
    {
        if (Version.TryParse(text.TrimStart('v', 'V'), out var parsed) && parsed is not null)
        {
            version = Normalize(parsed);
            return true;
        }

        version = new Version(0, 0, 0, 0);
        return false;
    }

    /// <summary>
    /// Pads a version to four components. Unspecified fields parse as -1, which would
    /// make "0.2.0" compare as older than "0.2.0.0" and hide a genuine release.
    /// </summary>
    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));
}
