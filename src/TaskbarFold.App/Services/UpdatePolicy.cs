using Microsoft.Win32;

namespace TaskbarFold.App.Services;

/// <summary>
/// Administrator overrides for the update check, read from
/// <c>HKLM\SOFTWARE\Policies\TaskbarFold</c> (and the same path under HKCU).
///
/// Where an organisation deploys versions itself — Intune, SCCM, Group Policy — telling
/// the user about a release they have no way to install is worse than saying nothing, so
/// the check can be switched off centrally. A private or staged deployment can also point
/// the app at its own feed.
/// </summary>
public static class UpdatePolicy
{
    private const string PolicyPath = @"SOFTWARE\Policies\TaskbarFold";

    private const string DisableValueName = "DisableUpdateCheck";
    private const string FeedValueName = "UpdateFeedUrl";

    /// <summary>True when an administrator has turned the check off entirely.</summary>
    public static bool IsCheckDisabled => ReadDword(DisableValueName) == 1;

    /// <summary>An alternative feed URL, or null to use the built-in one.</summary>
    public static string? FeedUrlOverride
    {
        get
        {
            var value = ReadString(FeedValueName);

            // Only absolute HTTPS endpoints: a policy value is not a reason to downgrade
            // to plaintext or to follow something that is not a URL at all.
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                ? value
                : null;
        }
    }

    private static int? ReadDword(string name)
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = root.OpenSubKey(PolicyPath);
                if (key?.GetValue(name) is int value)
                {
                    return value;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static string? ReadString(string name)
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = root.OpenSubKey(PolicyPath);
                if (key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
