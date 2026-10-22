using System.Globalization;
using System.Windows;
using Space.Map.App.Logging;

namespace Space.Map.App.Services;

public sealed class LocalizationService : ILocalizationService
{
    private const string EnglishCode = "en-US";
    private const string ChineseCode = "zh-CN";

    public static LocalizationService Instance { get; set; } = new();

    private ResourceDictionary? _currentDictionary;
    private string _currentLanguage = EnglishCode;

    public string CurrentLanguage => _currentLanguage;

    public event EventHandler? LanguageChanged;

    public LocalizationService()
    {
        // Detect system culture
        var systemLanguage = CultureInfo.CurrentUICulture.Name;
        var initialLanguage = systemLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? ChineseCode
            : EnglishCode;

        SetLanguage(initialLanguage);
    }

    public void SetLanguage(string cultureCode)
    {
        var targetCode = cultureCode.Equals(ChineseCode, StringComparison.OrdinalIgnoreCase)
            ? ChineseCode
            : EnglishCode;

        try
        {
            var uri = new Uri($"pack://application:,,,/Resources/Strings.{targetCode}.xaml", UriKind.Absolute);
            var newDict = new ResourceDictionary { Source = uri };

            var appResources = Application.Current?.Resources;
            if (appResources is not null)
            {
                if (_currentDictionary is not null)
                {
                    appResources.MergedDictionaries.Remove(_currentDictionary);
                }

                appResources.MergedDictionaries.Add(newDict);
            }

            _currentDictionary = newDict;
            _currentLanguage = targetCode;
            AppLog.Information($"Language changed to {targetCode}.");
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load language resources for {cultureCode}.", ex);
        }
    }

    public string ToggleLanguage()
    {
        var next = _currentLanguage == ChineseCode ? EnglishCode : ChineseCode;
        SetLanguage(next);
        return next;
    }

    public string GetString(string key)
    {
        if (Application.Current?.Resources.Contains(key) == true)
        {
            var value = Application.Current.Resources[key];
            if (value is string s)
            {
                return s;
            }
        }

        if (_currentDictionary?.Contains(key) == true)
        {
            var value = _currentDictionary[key];
            if (value is string s)
            {
                return s;
            }
        }

        return key;
    }

    public string Format(string key, params object[] args)
    {
        var pattern = GetString(key);
        try
        {
            return string.Format(pattern, args);
        }
        catch (FormatException)
        {
            return pattern;
        }
    }
}
