using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Arkadia.Core.Models;
using Arkadia.Infrastructure.Providers;
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
        _gameArtworkService.ArtworkUpdated += GameArtworkUpdated;
        trayIconService.Attach(this);
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
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

    private async void HomeTileGameArtwork_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Image image || image.DataContext is not GameEntry game)
        {
            return;
        }

        var artwork = await _gameArtworkService.LoadArtworkAsync(game);
        if (!image.IsLoaded)
        {
            return;
        }

        image.Source = artwork;
        image.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void GameArtworkUpdated(object? sender, GameArtworkUpdatedEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            foreach (var image in FindVisualChildren<System.Windows.Controls.Image>(this))
            {
                if (!Equals(image.DataContext, e.Game))
                {
                    continue;
                }

                image.Source = e.Artwork;
                image.Visibility = Visibility.Visible;
                if (image.Parent is Grid card)
                {
                    var fallback = card.Children
                        .OfType<FrameworkElement>()
                        .FirstOrDefault(child => child.Name == "GameArtworkFallback");
                    if (fallback is not null)
                    {
                        fallback.Visibility = Visibility.Collapsed;
                    }
                }
            }
        });
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
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
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.StartAutomaticUpdateChecks();
        }

        UpdateBackgroundActivity();
    }

    private void MainWindow_Closed(object? sender, EventArgs e) =>
        _gameArtworkService.ArtworkUpdated -= GameArtworkUpdated;

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void ToggleMaximizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) =>
        Close();

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

    private async void ScanPcGames_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizationManager.Instance["Games.ScanPc.SelectFolder"],
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        try
        {
            var scanResult = await Task.Run(
                () => PcGameScanner.ScanAsync(dialog.SelectedPath, CancellationToken.None));
            if (scanResult.Candidates.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    this,
                    LocalizationManager.Instance["Games.ScanPc.NoCandidates"],
                    LocalizationManager.Instance["Games.ScanPc"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var reviewWindow = new PcGameScanReviewWindow(scanResult) { Owner = this };
            if (reviewWindow.ShowDialog() == true
                && DataContext is ViewModels.MainViewModel viewModel)
            {
                await viewModel.AddScannedGamesAsync(
                    reviewWindow.SelectedCandidates,
                    CancellationToken.None);
            }
        }
        catch (ArgumentException exception)
        {
            Serilog.Log.Warning(exception, "The selected PC scan directory is invalid.");
            ShowPcScanError();
        }
        catch (IOException exception)
        {
            Serilog.Log.Warning(exception, "The PC game scan could not read the selected directory.");
            ShowPcScanError();
        }
        catch (UnauthorizedAccessException exception)
        {
            Serilog.Log.Warning(exception, "Access was denied during the PC game scan.");
            ShowPcScanError();
        }
    }

    private void ShowPcScanError() =>
        System.Windows.MessageBox.Show(
            this,
            LocalizationManager.Instance["Games.ScanPc.Failed"],
            LocalizationManager.Instance["Games.ScanPc"],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private void ShowSystemMetricsDetails_Click(object sender, RoutedEventArgs e)
    {
        new SystemMetricsDetailsWindow
        {
            Owner = this,
            DataContext = DataContext
        }.ShowDialog();
    }

    private async void ImportPalette_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationManager.Instance["Settings.Palette.Import"],
            Filter = LocalizationManager.Instance["Settings.Palette.FileFilter"],
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.ImportCustomPaletteAsync(dialog.FileName);
        }
    }

    private async void ExportPalette_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = LocalizationManager.Instance["Settings.Palette.Export"],
            Filter = LocalizationManager.Instance["Settings.Palette.FileFilter"],
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "arkadia-theme.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.ExportCustomPaletteAsync(dialog.FileName);
        }
    }

    private async void ExportUserData_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = LocalizationManager.Instance["Settings.Data.Export"],
            Filter = LocalizationManager.Instance["Settings.Data.FileFilter"],
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = "arkadia-data-export.zip",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            var exported = await viewModel.ExportUserDataAsync(dialog.FileName);
            System.Windows.MessageBox.Show(
                this,
                viewModel.LaunchMessage,
                "ARKADIA",
                MessageBoxButton.OK,
                exported
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Error);
        }
    }

    private async void DeleteUserData_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (System.Windows.MessageBox.Show(
                this,
                LocalizationManager.Instance["Settings.Data.DeleteConfirmation"],
                "ARKADIA",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (await viewModel.DeleteAllUserDataAsync())
        {
            IsQuitting = true;
            System.Windows.Application.Current.Shutdown();
            return;
        }

        System.Windows.MessageBox.Show(
            this,
            viewModel.LaunchMessage,
            "ARKADIA",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
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
