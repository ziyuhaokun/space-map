namespace Space.Map.App.Services;

public interface IThemeService
{
    bool IsDarkTheme { get; }

    void ToggleTheme();
}
