using System.Windows;
using System.Windows.Controls;
using Arkadia.Core.Models;
using Arkadia.App.Services;
using Arkadia.App.ViewModels;

namespace Arkadia.App;

public partial class MainWindow : Window
{
    private bool _isInitializingAppearance = true;
    private TrayIconService? _trayIconService;
    private readonly GameArtworkService _gameArtworkService;

    public MainWindow(
        ViewModels.MainViewModel viewModel,
        TrayIconService trayIconService,
        GameArtworkService gameArtworkService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _trayIconService = trayIconService;
        _gameArtworkService = gameArtworkService;
        trayIconService.Attach(this);
        Loaded += MainWindow_Loaded;
    }

    public bool IsQuitting { get; set; }

    private void ShortcutFavicon_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Image image)
        {
            image.Visibility = Visibility.Collapsed;
            if (image.Parent is Grid container)
            {
                foreach (UIElement child in container.Children)
                {
                    if (child is TextBlock fallback && fallback.Name == "ShortcutIconFallback")
                    {
                        fallback.Visibility = Visibility.Visible;
                        break;
                    }
                }
            }
        }
    }

    private async void GameArtwork_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Image image || image.DataContext is not GameEntry game)
        {
            return;
        }

        var artwork = await _gameArtworkService.LoadArtworkAsync(game);
        if (!image.IsLoaded || image.Parent is not Grid card)
        {
            return;
        }

        var fallback = card.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(child => child.Name == "GameArtworkFallback");
        image.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
        image.Source = artwork;
        if (fallback is not null)
        {
            fallback.Visibility = artwork is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void Appearance_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializingAppearance && DataContext is MainViewModel viewModel)
        {
            await viewModel.SaveAppearanceCommand.ExecuteAsync(null);
        }
    }

    private async void SystemSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitializingAppearance && DataContext is MainViewModel viewModel)
        {
            await viewModel.SaveSystemSettingsCommand.ExecuteAsync(null);
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e) =>
        _isInitializingAppearance = false;

    private void SteamApiKey_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && DataContext is MainViewModel viewModel)
        {
            passwordBox.Password = viewModel.SteamApiKey;
        }
    }

    private void SteamApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && DataContext is MainViewModel viewModel)
        {
            viewModel.SteamApiKey = passwordBox.Password;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!IsQuitting
            && DataContext is MainViewModel { IsApplyingUpdate: false, CloseToTray: true })
        {
            e.Cancel = true;
            _trayIconService?.HideToTray();
        }
        else if (!IsQuitting)
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}
