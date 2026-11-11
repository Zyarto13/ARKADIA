using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Arkadia.App.Localization;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly ResourceManager ResourceManager =
        new("Arkadia.App.Strings", Assembly.GetExecutingAssembly());

    private static readonly LocalizationManager InstanceValue = new();

    private LocalizationManager()
    {
        SetCulture(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    public static LocalizationManager Instance => InstanceValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    public void SetCulture(string cultureName)
    {
        var culture = cultureName is "fr" or "en"
            ? CultureInfo.GetCultureInfo(cultureName)
            : CultureInfo.GetCultureInfo("en");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
