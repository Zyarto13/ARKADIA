using Arkadia.App.Localization;
using Arkadia.App.Icons;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Arkadia.App.ViewModels;

public sealed class NavigationItemViewModel : ObservableObject
{
    private readonly string _titleKey;
    private bool _isSelected;

    public NavigationItemViewModel(string key, string titleKey)
    {
        Key = key;
        _titleKey = titleKey;
        LocalizationManager.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }

    public string Key { get; }
    public Geometry Icon => IconCatalog.ForNavigation(Key);
    public string Title => LocalizationManager.Instance[_titleKey];

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
