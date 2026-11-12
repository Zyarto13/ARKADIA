using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Arkadia.Core.Models;
using Arkadia.App.Localization;
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
        IsVisibleChanged += MainWindow_IsVisibleChanged;
        StateChanged += MainWindow_StateChanged;
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

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializingAppearance = false;
        UpdateBackgroundActivity();
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        UpdateBackgroundActivity();

    private void MainWindow_StateChanged(object? sender, EventArgs e) =>
        UpdateBackgroundActivity();

    private void UpdateBackgroundActivity()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SetApplicationVisible(IsVisible && WindowState != WindowState.Minimized);
        }
    }

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

    private async void AddManualGame_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationManager.Instance["Games.SelectExecutable"],
            Filter = LocalizationManager.Instance["Games.ExecutableFilter"],
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.AddManualGameCommand.ExecuteAsync(dialog.FileName);
        }
    }

    private void MusicVolumePercent_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
        {
            e.Handled = true;
            return;
        }

        var proposedText = textBox.Text
            .Remove(textBox.SelectionStart, textBox.SelectionLength)
            .Insert(textBox.SelectionStart, e.Text);
        e.Handled = !IsValidVolumeEntry(proposedText);
    }

    private void MusicVolumePercent_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox
            || !e.SourceDataObject.GetDataPresent(System.Windows.DataFormats.UnicodeText, true)
            || e.SourceDataObject.GetData(System.Windows.DataFormats.UnicodeText) is not string pastedText)
        {
            e.CancelCommand();
            return;
        }

        var proposedText = textBox.Text
            .Remove(textBox.SelectionStart, textBox.SelectionLength)
            .Insert(textBox.SelectionStart, pastedText);
        if (!IsValidVolumeEntry(proposedText))
        {
            e.CancelCommand();
        }
    }

    private void MusicVolumePercent_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        ApplyMusicVolumePercent(textBox, viewModel);
    }

    private void MusicVolumePercent_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || sender is not System.Windows.Controls.TextBox textBox
            || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        ApplyMusicVolumePercent(textBox, viewModel);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private static void ApplyMusicVolumePercent(
        System.Windows.Controls.TextBox textBox,
        MainViewModel viewModel)
    {
        if (int.TryParse(textBox.Text, out var percent))
        {
            viewModel.MusicVolumePercent = percent;
        }

        textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
    }

    private static bool IsValidVolumeEntry(string text) =>
        text.Length == 0
        || text.All(character => character is >= '0' and <= '9')
        && int.TryParse(text, out var percent)
        && percent <= 100;

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
