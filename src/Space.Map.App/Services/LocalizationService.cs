using System.Globalization;
using System.IO;
using System.Windows;
using Space.Map.Infrastructure.Logging;

namespace Space.Map.App.Services;

public sealed class LocalizationService : ILocalizationService
{
    private const string EnglishCode = "en-US";
    private const string ChineseCode = "zh-CN";

    private static readonly string PreferenceFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Space.Map",
        "language.txt");

    public static LocalizationService Instance { get; set; } = new();

    private ResourceDictionary? _currentDictionary;
    private string _currentLanguage = EnglishCode;

    public string CurrentLanguage => _currentLanguage;

    public event EventHandler? LanguageChanged;

    public LocalizationService()
    {
        // Do not persist here: writing a preference on first launch would freeze the
        // system locale forever, so later OS language changes would be ignored.
        SetLanguage(ResolveInitialLanguage(), persist: false);
    }

    /// <summary>
    /// Saved user preference wins over the system UI culture, so a manual switch
    /// survives restarts.
    /// </summary>
    private static string ResolveInitialLanguage()
    {
        var saved = TryReadPreference();
        if (!string.IsNullOrWhiteSpace(saved))
        {
            return saved;
        }

        var systemLanguage = CultureInfo.CurrentUICulture.Name;
        return systemLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? ChineseCode
            : EnglishCode;
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
            return value.Length == 0 ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static void TryWritePreference(string cultureCode)
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferenceFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(PreferenceFilePath, cultureCode);
        }
        catch (Exception ex)
        {
            // Persisting the choice is a convenience; never fail the switch over it.
            AppLog.Warning($"Failed to persist language preference: {ex.Message}");
        }
    }

    public void SetLanguage(string cultureCode) => SetLanguage(cultureCode, persist: true);

    /// <param name="persist">
    /// Whether to record this as the user's explicit choice. Startup resolution
    /// passes false so an untouched install keeps following the OS locale.
    /// </param>
    private void SetLanguage(string cultureCode, bool persist)
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

            // Numbers and dates must follow the UI language, not the OS locale,
            // otherwise English UI shows zh-CN grouping (or vice versa).
            ApplyCulture(targetCode);

            if (persist)
            {
                TryWritePreference(targetCode);
            }

            AppLog.Information($"Language changed to {targetCode}.");
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load language resources for {cultureCode}.", ex);
        }
    }

    private static void ApplyCulture(string cultureCode)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureCode);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException ex)
        {
            AppLog.Warning($"Culture {cultureCode} unavailable: {ex.Message}");
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
            return string.Format(CultureInfo.CurrentCulture, pattern, args);
        }
        catch (FormatException)
        {
            return pattern;
        }
    }
}
