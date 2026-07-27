using System.Windows;
using Microsoft.Win32;
using TaskbarFold.Core.Models;

namespace TaskbarFold.App.Services;

/// <summary>
/// Swaps the application's colour dictionary and tracks the Windows setting when the
/// user leaves the theme on <see cref="AppTheme.System"/>.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightThemeValue = "AppsUseLightTheme";

    private AppTheme _preference = AppTheme.System;
    private bool _disposed;

    public ThemeService() => SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

    /// <summary>Raised after the effective theme changes, so open windows can restyle.</summary>
    public event EventHandler<bool>? EffectiveThemeChanged;

    public bool IsDark { get; private set; } = true;

    public void Apply(AppTheme preference)
    {
        _preference = preference;
        var dark = preference switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => IsSystemDark(),
        };

        if (dark == IsDark && Application.Current?.Resources.MergedDictionaries.Count > 0)
        {
            return;
        }

        IsDark = dark;
        SwapDictionary(dark);
        EffectiveThemeChanged?.Invoke(this, dark);
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // The value is 1 for light apps, 0 for dark. Missing means light.
            return key?.GetValue(LightThemeValue) is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void SwapDictionary(bool dark)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var uri = new Uri(
            dark ? "Resources/Dark.xaml" : "Resources/Light.xaml", UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = uri };

        var dictionaries = application.Resources.MergedDictionaries;

        // The palette is always index 0; shared styles that reference it follow.
        if (dictionaries.Count == 0)
        {
            dictionaries.Add(dictionary);
        }
        else
        {
            dictionaries[0] = dictionary;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color &&
            _preference == AppTheme.System)
        {
            // The registry value is written slightly after the notification arrives.
            Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _disposed = true;
    }
}
