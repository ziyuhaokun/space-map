using System.IO;
using Space.Map.Infrastructure.Logging;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Space.Map.App.Services;

public sealed class WpfUiThemeService : IThemeService
{
    private const string DarkPreference = "dark";
    private const string LightPreference = "light";

    private static readonly string PreferenceFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Space.Map",
        "theme.txt");

    public bool IsDarkTheme => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

    public bool HasSavedPreference => TryReadPreference() is not null;

    /// <summary>
    /// Applies the saved theme choice, if the user ever made one. Like the language
    /// preference, an untouched install keeps following the system theme instead of
    /// freezing whatever it happened to start with.
    /// </summary>
    public bool ApplySavedTheme()
    {
        var saved = TryReadPreference();
        if (saved is null)
        {
            return false;
        }

        Apply(saved == DarkPreference ? ApplicationTheme.Dark : ApplicationTheme.Light);
        return true;
    }

    public void ToggleTheme()
    {
        var nextTheme = IsDarkTheme ? ApplicationTheme.Light : ApplicationTheme.Dark;
        Apply(nextTheme);
        TryWritePreference(nextTheme == ApplicationTheme.Dark ? DarkPreference : LightPreference);
    }

    private static void Apply(ApplicationTheme theme)
    {
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
    }

    private static string? TryReadPreference()
    {
        try
        {
            if (!File.Exists(PreferenceFilePath))
            {
                return null;
            }

            var value = File.ReadAllText(PreferenceFilePath).Trim();
            return value.Length == 0 ? null : value.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private static void TryWritePreference(string value)
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferenceFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(PreferenceFilePath, value);
        }
        catch (Exception ex)
        {
            // Persisting the choice is a convenience; never fail the switch over it.
            AppLog.Warning($"Failed to persist theme preference: {ex.Message}");
        }
    }
}
