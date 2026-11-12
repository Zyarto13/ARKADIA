using Arkadia.App.Localization;
using Arkadia.App.Icons;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace Arkadia.App.ViewModels;

public sealed class HomeTileViewModel : ObservableObject
{
    private string _titleKey;
    private string _descriptionKey;
    private string _id;
    private string? _launchUri;
    private bool _isVisible = true;

    public HomeTileViewModel(
        string id,
        string glyph,
        string titleKey,
        string descriptionKey,
        string? launchUri = null,
        string? navigationKey = null)
    {
        _id = id;
        Glyph = glyph;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        _launchUri = launchUri;
        NavigationKey = navigationKey;
        LocalizationManager.Instance.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(VisibilityTooltip));
        };
    }

    public string Id => _id;
    public string LayoutId => _id is "spotify" or "deezer" or "youtubemusic" ? "music" : _id;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                OnPropertyChanged(nameof(VisibilityTooltip));
            }
        }
    }
    public string VisibilityTooltip => LocalizationManager.Instance[
        IsVisible ? "Home.Layout.Hide" : "Home.Layout.Show"];

    public string Glyph { get; }
    public string? LaunchUri => _launchUri;
    public string? NavigationKey { get; }
    public Geometry? BrandIcon => IconCatalog.ForBrand(Id);
    public bool HasBrandIcon => Id is "spotify" or "deezer" or "youtubemusic" or "discord" or "steam" or "epic" or "curseforge" or "minecraft";
    public Brush BrandColor => Id switch
    {
        "spotify" => new SolidColorBrush(Color.FromRgb(30, 215, 96)),
        "deezer" => new SolidColorBrush(Color.FromRgb(121, 37, 235)),
        "youtubemusic" => new SolidColorBrush(Color.FromRgb(255, 0, 0)),
        "discord" => new SolidColorBrush(Color.FromRgb(88, 101, 242)),
        "steam" => new SolidColorBrush(Color.FromRgb(47, 70, 90)),
        "epic" => new SolidColorBrush(Color.FromRgb(25, 27, 31)),
        "curseforge" => new SolidColorBrush(Color.FromRgb(241, 100, 54)),
        "minecraft" => new SolidColorBrush(Color.FromRgb(92, 140, 62)),
        "last-game" => new SolidColorBrush(Color.FromRgb(90, 160, 255)),
        "achievement" => new SolidColorBrush(Color.FromRgb(238, 184, 70)),
        "system" => new SolidColorBrush(Color.FromRgb(106, 196, 177)),
        "shortcuts" => new SolidColorBrush(Color.FromRgb(139, 128, 232)),
        _ => new SolidColorBrush(Color.FromRgb(111, 130, 205))
    };

    public Geometry TileIcon => IconCatalog.ForBrand(Id);
    public string Title => LocalizationManager.Instance[_titleKey];
    public string Description => LocalizationManager.Instance[_descriptionKey];

    public void SetMusicService(string id, string titleKey, string descriptionKey, string launchUri)
    {
        _id = id;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        _launchUri = launchUri;
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(LaunchUri));
        OnPropertyChanged(nameof(BrandIcon));
        OnPropertyChanged(nameof(HasBrandIcon));
        OnPropertyChanged(nameof(BrandColor));
        OnPropertyChanged(nameof(TileIcon));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }
}
