using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Space.Map.App.Services;

public sealed class WpfUiThemeService : IThemeService
{
    public bool IsDarkTheme => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

    public void ToggleTheme()
    {
        var nextTheme = IsDarkTheme ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(nextTheme, WindowBackdropType.Mica, updateAccent: true);
    }
}
