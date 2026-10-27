namespace Space.Map.App.Services;

public interface IThemeService
{
    bool IsDarkTheme { get; }

    /// <summary>
    /// Whether the user ever picked a theme explicitly. When true the shell must not
    /// hand control back to the system theme watcher, which would overwrite the choice.
    /// </summary>
    bool HasSavedPreference { get; }

    /// <summary>
    /// Re-applies the user's saved theme choice.
    /// </summary>
    /// <returns><c>true</c> when a saved choice existed and was applied.</returns>
    bool ApplySavedTheme();

    void ToggleTheme();
}
