namespace Space.Map.App.Services;

public interface ILocalizationService
{
    string CurrentLanguage { get; }

    void SetLanguage(string cultureCode);

    string ToggleLanguage();

    string GetString(string key);

    string Format(string key, params object[] args);

    event EventHandler? LanguageChanged;
}
