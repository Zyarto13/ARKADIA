using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.ComponentModel;
using System.Windows.Media;
using Arkadia.App.Icons;
using Arkadia.App.Localization;
using Arkadia.App.Services;
using Arkadia.Core.Models;
using Arkadia.Core.Providers;
using Arkadia.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Arkadia.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public sealed record MusicServiceOption(
        string Id,
        string TitleKey,
        string DescriptionKey,
        string LaunchUri);

    private readonly IExternalLauncher _externalLauncher;
    private readonly ISettingsService _settingsService;
    private readonly IStartupTaskService _startupTaskService;
    private readonly IReadOnlyList<IGameProvider> _gameProviders;
    private readonly Core.Services.IGameAchievementService _gameAchievementService;
    private readonly SteamApiKeyStore _steamApiKeyStore;
    private readonly GitHubUpdateService _updateService;
    private readonly ThemeService _themeService;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly System.Threading.Timer _clockTimer;
    private readonly string _currentAppVersion;
    private string _gameSearchQuery = string.Empty;
    private string _selectedGameProvider = GameLibraryFilter.AllProviders;
    private string _filteredGamesStatusText = string.Empty;
    private string _currentPageKey = "Home";
    private string _launchMessage = string.Empty;
    private string _comingSoonText = string.Empty;
    private string _connectivityText = string.Empty;
    private bool _isSettingsSelected;
    private string _selectedTheme = "Blue";
    private string _selectedLanguage = "en";
    private string _selectedMusicService = "Spotify";
    private bool _runAtStartup;
    private bool _startMinimized;
    private bool _closeToTray = true;
    private bool _isLoadingGames;
    private string _gamesStatusText = string.Empty;
    private bool _hasScannedGames;
    private int _failedGameProviders;
    private bool _sortGamesByPlaytime;
    private string _steamApiKey = string.Empty;
    private string _achievementsStatusText = string.Empty;
    private GameEntry? _selectedAchievementGame;
    private bool _isLoadingAchievements;
    private string _shortcutNameInput = string.Empty;
    private string _shortcutUriInput = string.Empty;
    private string _updateStatusText = string.Empty;
    private UpdateRelease? _availableUpdate;
    private bool _isCheckingForUpdates;
    private bool _isApplyingUpdate;

    public MainViewModel(
        IExternalLauncher externalLauncher,
        ISettingsService settingsService,
        IStartupTaskService startupTaskService,
        Core.Services.IGameAchievementService gameAchievementService,
        SteamApiKeyStore steamApiKeyStore,
        IEnumerable<IGameProvider> gameProviders,
        ThemeService themeService,
        GitHubUpdateService updateService)
    {
        _externalLauncher = externalLauncher;
        _settingsService = settingsService;
        _startupTaskService = startupTaskService;
        _gameAchievementService = gameAchievementService;
        _steamApiKeyStore = steamApiKeyStore;
        _updateService = updateService;
        _gameProviders = gameProviders.ToArray();
        _themeService = themeService;
        _currentAppVersion = GitHubUpdateService.CurrentVersion.ToString(3);
        _synchronizationContext = SynchronizationContext.Current;
        NavigationItems =
        [
            new("Home", "Navigation.Home"),
            new("Games", "Navigation.Games"),
            new("Achievements", "Navigation.Achievements"),
            new("Sound", "Navigation.Sounds"),
            new("Shortcuts", "Navigation.Shortcuts")
        ];
        HomeTiles =
        [
            new("spotify", "◉", "Tile.Music", "Tile.Music.Description", "spotify:"),
            new("discord", "●", "Tile.Discord", "Tile.Discord.Description", "discord://"),
            new("steam", "◉", "Tile.Steam", "Tile.Steam.Description", "steam://open/main"),
            new("epic", "E", "Tile.Epic", "Tile.Epic.Description", "com.epicgames.launcher://"),
            new("curseforge", "✣", "Tile.CurseForge", "Tile.CurseForge.Description"),
            new("minecraft", "✦", "Tile.Minecraft", "Tile.Minecraft.Description"),
            new("last-game", "▶", "Tile.LastGame", "Tile.LastGame.Description", navigationKey: "Games"),
            new("achievement", "♜", "Tile.Achievements", "Tile.Achievements.Description", navigationKey: "Achievements"),
            new("system", "◌", "Tile.Stats", "Tile.Stats.Description"),
            new("shortcuts", "▦", "Tile.Shortcuts", "Tile.Shortcuts.Description", navigationKey: "Shortcuts")
        ];
        Games = [];
        FilteredGames = [];
        Achievements = [];
        Shortcuts = [];

        SelectPage("Home");
        LocalizationManager.Instance.PropertyChanged += LocalizationChanged;
        UpdateLocalizedProperties();
        UpdateClock();
        _clockTimer = new System.Threading.Timer(
            _ => _synchronizationContext?.Post(static state => ((MainViewModel)state!).UpdateClock(), this),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1));
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public ObservableCollection<HomeTileViewModel> HomeTiles { get; }
    public ObservableCollection<GameEntry> Games { get; }
    public ObservableCollection<GameEntry> FilteredGames { get; }
    public ObservableCollection<GameAchievement> Achievements { get; }
    public ObservableCollection<AppShortcut> Shortcuts { get; }
    public bool IsHome => _currentPageKey == "Home";
    public bool IsGames => _currentPageKey == "Games";
    public bool IsAchievements => _currentPageKey == "Achievements";
    public bool IsShortcuts => _currentPageKey == "Shortcuts";
    public bool IsSettings => _currentPageKey == "Settings";
    public bool IsComingSoon => !IsHome && !IsGames && !IsAchievements && !IsShortcuts && !IsSettings;
    public bool HasGames => Games.Count > 0;
    public bool HasFilteredGames => FilteredGames.Count > 0;
    public bool IsGameSearchEmpty => HasGames && !HasFilteredGames;
    public bool HasShortcuts => Shortcuts.Count > 0;
    public bool IsShortcutsEmpty => Shortcuts.Count == 0;
    public IEnumerable<GameEntry> SteamGames => Games.Where(game => game.ProviderId == "steam");
    public bool HasAchievements => Achievements.Count > 0;
    public int UnlockedAchievementCount => Achievements.Count(achievement => achievement.IsUnlocked);
    public double AchievementProgress => Achievements.Count == 0
        ? 0
        : (double)UnlockedAchievementCount / Achievements.Count;
    public bool IsGamesEmpty => !IsLoadingGames && !HasGames;
    public string CurrentPageTitle => _currentPageKey == "Home"
        ? LocalizationManager.Instance["Navigation.Home"]
        : PageTitle(_currentPageKey);
    public string CurrentPageSubtitle => LocalizationManager.Instance[
        _currentPageKey switch
        {
            "Home" => "Home.Subtitle",
            "Games" => "Games.Subtitle",
            "Achievements" => "Achievements.Subtitle",
            "Shortcuts" => "Shortcuts.Subtitle",
            _ => "Home.Empty"
        }];
    public string ComingSoonText
    {
        get => _comingSoonText;
        private set => SetProperty(ref _comingSoonText, value);
    }

    public string ConnectivityText
    {
        get => _connectivityText;
        private set => SetProperty(ref _connectivityText, value);
    }

    public string GamesStatusText
    {
        get => _gamesStatusText;
        private set => SetProperty(ref _gamesStatusText, value);
    }

    public string GameSearchQuery
    {
        get => _gameSearchQuery;
        set
        {
            if (SetProperty(ref _gameSearchQuery, value))
            {
                RefreshFilteredGames();
            }
        }
    }

    public string SelectedGameProvider
    {
        get => _selectedGameProvider;
        set
        {
            if (SetProperty(ref _selectedGameProvider, value))
            {
                RefreshFilteredGames();
            }
        }
    }

    public string FilteredGamesStatusText
    {
        get => _filteredGamesStatusText;
        private set => SetProperty(ref _filteredGamesStatusText, value);
    }

    public string AchievementsStatusText
    {
        get => _achievementsStatusText;
        private set => SetProperty(ref _achievementsStatusText, value);
    }

    public string CurrentAppVersion => _currentAppVersion;

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public bool HasUpdateAvailable => _availableUpdate is not null;

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            if (SetProperty(ref _isCheckingForUpdates, value))
            {
                CheckForUpdatesCommand.NotifyCanExecuteChanged();
                ApplyUpdateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsApplyingUpdate
    {
        get => _isApplyingUpdate;
        private set
        {
            if (SetProperty(ref _isApplyingUpdate, value))
            {
                CheckForUpdatesCommand.NotifyCanExecuteChanged();
                ApplyUpdateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SteamApiKey
    {
        get => _steamApiKey;
        set => SetProperty(ref _steamApiKey, value);
    }

    public GameEntry? SelectedAchievementGame
    {
        get => _selectedAchievementGame;
        set
        {
            if (SetProperty(ref _selectedAchievementGame, value))
            {
                Achievements.Clear();
                OnPropertyChanged(nameof(HasAchievements));
                OnPropertyChanged(nameof(UnlockedAchievementCount));
                OnPropertyChanged(nameof(AchievementProgress));
                LoadAchievementsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    [RelayCommand]
    private void SelectAchievementGame(GameEntry? game)
    {
        SelectedAchievementGame = game;
        AchievementsStatusText = game?.ProviderId == "steam"
            ? string.Empty
            : LocalizationManager.Instance["Achievements.UnsupportedProvider"];
    }

    public bool IsLoadingAchievements
    {
        get => _isLoadingAchievements;
        private set
        {
            if (SetProperty(ref _isLoadingAchievements, value))
            {
                LoadAchievementsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoadingGames
    {
        get => _isLoadingGames;
        private set
        {
            if (SetProperty(ref _isLoadingGames, value))
            {
                OnPropertyChanged(nameof(IsGamesEmpty));
                RefreshGamesCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public string ClockText { get; private set; } = string.Empty;
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _themeService.Apply(value);
            }
        }
    }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
            {
                LocalizationManager.Instance.SetCulture(value);
            }
        }
    }

    public string SelectedMusicService
    {
        get => _selectedMusicService;
        set
        {
            var service = MusicServices.FirstOrDefault(item =>
                string.Equals(item.Id, value, StringComparison.OrdinalIgnoreCase));
            if (service is not null && SetProperty(ref _selectedMusicService, service.Id))
            {
                var musicTile = HomeTiles.First(tile => tile.Id is "spotify" or "deezer" or "youtubemusic");
                musicTile.SetMusicService(
                    service.Id.ToLowerInvariant(),
                    service.TitleKey,
                    service.DescriptionKey,
                    service.LaunchUri);
            }
        }
    }

    public IReadOnlyList<MusicServiceOption> MusicServices { get; } =
    [
        new("Spotify", "Tile.Music.Spotify", "Tile.Music.Description", "spotify:"),
        new("Deezer", "Tile.Music.Deezer", "Tile.Music.Description", "https://www.deezer.com/"),
        new("YouTubeMusic", "Tile.Music.YouTubeMusic", "Tile.Music.Description", "https://music.youtube.com/")
    ];
    public bool IsSettingsSelected
    {
        get => _isSettingsSelected;
        private set => SetProperty(ref _isSettingsSelected, value);
    }

    public bool RunAtStartup
    {
        get => _runAtStartup;
        set => SetProperty(ref _runAtStartup, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetProperty(ref _startMinimized, value);
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetProperty(ref _closeToTray, value);
    }

    public string LaunchMessage
    {
        get => _launchMessage;
        private set => SetProperty(ref _launchMessage, value);
    }

    public string ShortcutNameInput
    {
        get => _shortcutNameInput;
        set => SetProperty(ref _shortcutNameInput, value);
    }

    public string ShortcutUriInput
    {
        get => _shortcutUriInput;
        set => SetProperty(ref _shortcutUriInput, value);
    }

    [RelayCommand]
    private void SelectPage(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _currentPageKey = key;
        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Key == key;
        }

        IsSettingsSelected = key == "Settings";
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(IsGames));
        OnPropertyChanged(nameof(IsAchievements));
        OnPropertyChanged(nameof(IsShortcuts));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsComingSoon));
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageSubtitle));
        LaunchMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAppearanceAsync(CancellationToken cancellationToken)
    {
        await _settingsService.SaveAsync(
            CurrentSettings,
            cancellationToken);
    }

    [RelayCommand]
    private async Task SaveSystemSettingsAsync(CancellationToken cancellationToken)
    {
        await _startupTaskService.SetEnabledAsync(RunAtStartup, StartMinimized, cancellationToken);
        await _settingsService.SaveAsync(CurrentSettings, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        _availableUpdate = null;
        OnPropertyChanged(nameof(HasUpdateAvailable));
        ApplyUpdateCommand.NotifyCanExecuteChanged();
        IsCheckingForUpdates = true;
        UpdateStatusText = LocalizationManager.Instance["Settings.Updates.Checking"];

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(cancellationToken);
            switch (result.Status)
            {
                case UpdateCheckStatus.NoRelease:
                    UpdateStatusText = LocalizationManager.Instance["Settings.Updates.NoRelease"];
                    break;
                case UpdateCheckStatus.UpToDate:
                    UpdateStatusText = string.Format(
                        CultureInfo.CurrentCulture,
                        LocalizationManager.Instance["Settings.Updates.UpToDate"],
                        CurrentAppVersion);
                    break;
                case UpdateCheckStatus.UpdateAvailable when result.Release is not null:
                    _availableUpdate = result.Release;
                    OnPropertyChanged(nameof(HasUpdateAvailable));
                    ApplyUpdateCommand.NotifyCanExecuteChanged();
                    UpdateStatusText = GitHubUpdateService.IsInstalled
                        ? string.Format(
                            CultureInfo.CurrentCulture,
                            LocalizationManager.Instance["Settings.Updates.Available"],
                            result.Release.Version)
                        : string.Format(
                            CultureInfo.CurrentCulture,
                            LocalizationManager.Instance["Settings.Updates.InstallRequired"],
                            result.Release.Version);
                    break;
                default:
                    throw new InvalidDataException("The update check returned an unexpected result.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or InvalidOperationException)
        {
            Log.Warning(exception, "Could not check for ARKADIA updates.");
            UpdateStatusText = LocalizationManager.Instance["Settings.Updates.CheckFailed"];
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyUpdate))]
    private async Task ApplyUpdateAsync(CancellationToken cancellationToken)
    {
        if (_availableUpdate is null)
        {
            return;
        }

        IsApplyingUpdate = true;
        UpdateStatusText = LocalizationManager.Instance["Settings.Updates.Installing"];
        try
        {
            await _updateService.DownloadAndLaunchInstallerAsync(_availableUpdate, cancellationToken);
            if (System.Windows.Application.Current is { } application)
            {
                if (application.MainWindow is Arkadia.App.MainWindow mainWindow)
                {
                    mainWindow.IsQuitting = true;
                }

                application.Shutdown();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            IsApplyingUpdate = false;
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or IOException
                or InvalidOperationException
                or System.Security.Cryptography.CryptographicException
                or Win32Exception)
        {
            Log.Warning(exception, "Could not install the available ARKADIA update.");
            UpdateStatusText = LocalizationManager.Instance["Settings.Updates.InstallFailed"];
            IsApplyingUpdate = false;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        _selectedTheme = settings.Theme;
        _selectedLanguage = settings.Language;
        _runAtStartup = await _startupTaskService.IsEnabledAsync(cancellationToken);
        _startMinimized = settings.StartMinimized;
        _closeToTray = settings.CloseToTray;
        foreach (var shortcut in settings.Shortcuts ?? [])
        {
            if (IsValidShortcut(shortcut.Name, shortcut.Uri))
            {
                Shortcuts.Add(shortcut);
            }
        }
        NotifyShortcutCollectionChanged();
        _selectedMusicService = MusicServices.FirstOrDefault(service =>
            string.Equals(service.Id, settings.MusicService, StringComparison.OrdinalIgnoreCase))?.Id
            ?? "Spotify";
        ApplySelectedMusicService();
        try
        {
            SteamApiKey = _steamApiKeyStore.Load() ?? string.Empty;
        }
        catch (CryptographicException exception)
        {
            Log.Warning(exception, "The locally protected Steam API key could not be decrypted.");
            AchievementsStatusText = LocalizationManager.Instance["Achievements.KeyUnreadable"];
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "The locally protected Steam API key could not be read.");
            AchievementsStatusText = LocalizationManager.Instance["Achievements.KeyUnreadable"];
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "Access to the locally protected Steam API key was denied.");
            AchievementsStatusText = LocalizationManager.Instance["Achievements.KeyUnreadable"];
        }
        _themeService.Apply(_selectedTheme);
        LocalizationManager.Instance.SetCulture(_selectedLanguage);
        if (string.IsNullOrWhiteSpace(SteamApiKey) && string.IsNullOrWhiteSpace(AchievementsStatusText))
        {
            AchievementsStatusText = LocalizationManager.Instance["Achievements.KeyRequired"];
        }
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(SelectedMusicService));
        OnPropertyChanged(nameof(RunAtStartup));
        OnPropertyChanged(nameof(StartMinimized));
        OnPropertyChanged(nameof(CloseToTray));
        await RefreshGamesAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task AddShortcutAsync(CancellationToken cancellationToken)
    {
        var name = ShortcutNameInput.Trim();
        var uriText = ShortcutUriInput.Trim();
        if (!IsValidShortcut(name, uriText))
        {
            LaunchMessage = LocalizationManager.Instance["Shortcuts.Invalid"];
            return;
        }

        if (Shortcuts.Count >= 30)
        {
            LaunchMessage = LocalizationManager.Instance["Shortcuts.Limit"];
            return;
        }

        if (Shortcuts.Any(shortcut => string.Equals(shortcut.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            LaunchMessage = LocalizationManager.Instance["Shortcuts.Duplicate"];
            return;
        }

        var shortcut = new AppShortcut(Guid.NewGuid(), name, new Uri(uriText, UriKind.Absolute).AbsoluteUri);
        Shortcuts.Add(shortcut);
        NotifyShortcutCollectionChanged();
        try
        {
            await _settingsService.SaveAsync(CurrentSettings, cancellationToken);
            ShortcutNameInput = string.Empty;
            ShortcutUriInput = string.Empty;
            LaunchMessage = LocalizationManager.Instance["Shortcuts.Added"];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Shortcuts.Remove(shortcut);
            NotifyShortcutCollectionChanged();
            throw;
        }
        catch (IOException exception)
        {
            Shortcuts.Remove(shortcut);
            NotifyShortcutCollectionChanged();
            Log.Error(exception, "Could not save a user shortcut.");
            LaunchMessage = LocalizationManager.Instance["Shortcuts.SaveFailed"];
        }
        catch (UnauthorizedAccessException exception)
        {
            Shortcuts.Remove(shortcut);
            NotifyShortcutCollectionChanged();
            Log.Error(exception, "Access was denied while saving a user shortcut.");
            LaunchMessage = LocalizationManager.Instance["Shortcuts.SaveFailed"];
        }
    }

    [RelayCommand]
    private async Task RemoveShortcutAsync(AppShortcut? shortcut, CancellationToken cancellationToken)
    {
        if (shortcut is null || !Shortcuts.Remove(shortcut))
        {
            return;
        }
        NotifyShortcutCollectionChanged();

        try
        {
            await _settingsService.SaveAsync(CurrentSettings, cancellationToken);
            LaunchMessage = LocalizationManager.Instance["Shortcuts.Removed"];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Shortcuts.Add(shortcut);
            NotifyShortcutCollectionChanged();
            throw;
        }
        catch (IOException exception)
        {
            Shortcuts.Add(shortcut);
            NotifyShortcutCollectionChanged();
            Log.Error(exception, "Could not remove a user shortcut.");
            LaunchMessage = LocalizationManager.Instance["Shortcuts.SaveFailed"];
        }
        catch (UnauthorizedAccessException exception)
        {
            Shortcuts.Add(shortcut);
            NotifyShortcutCollectionChanged();
            Log.Error(exception, "Access was denied while removing a user shortcut.");
            LaunchMessage = LocalizationManager.Instance["Shortcuts.SaveFailed"];
        }
    }

    [RelayCommand]
    private async Task LaunchShortcutAsync(AppShortcut? shortcut, CancellationToken cancellationToken)
    {
        if (shortcut is null || !IsValidShortcut(shortcut.Name, shortcut.Uri))
        {
            LaunchMessage = LocalizationManager.Instance["Shortcuts.Invalid"];
            return;
        }

        var result = await _externalLauncher.OpenUriAsync(shortcut.Uri, cancellationToken);
        LaunchMessage = result.Succeeded
            ? string.Empty
            : LocalizationManager.Instance["Home.LaunchFailed"];
    }

    [RelayCommand]
    private async Task LaunchTileAsync(HomeTileViewModel? tile, CancellationToken cancellationToken)
    {
        if (tile is null)
        {
            return;
        }

        if (tile.NavigationKey is not null)
        {
            SelectPage(tile.NavigationKey);
            return;
        }

        if (tile.LaunchUri is null)
        {
            LaunchMessage = LocalizationManager.Instance["Home.LaunchUnavailable"];
            return;
        }

        var result = await _externalLauncher.OpenUriAsync(tile.LaunchUri, cancellationToken);
        LaunchMessage = result.Succeeded
            ? string.Empty
            : LocalizationManager.Instance["Home.LaunchFailed"];
    }

    [RelayCommand(CanExecute = nameof(CanRefreshGames))]
    private async Task RefreshGamesAsync(CancellationToken cancellationToken)
    {
        IsLoadingGames = true;
        try
        {
            var discoveredGames = new List<GameEntry>();
            var failedProviders = 0;
            foreach (var provider in _gameProviders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    discoveredGames.AddRange(await provider.ScanInstalledAsync(cancellationToken));
                }
                catch (IOException exception)
                {
                    failedProviders++;
                    Log.Warning(exception, "Could not scan games from provider {ProviderId}.", provider.Id);
                }
                catch (UnauthorizedAccessException exception)
                {
                    failedProviders++;
                    Log.Warning(exception, "Access was denied while scanning games from provider {ProviderId}.", provider.Id);
                }
                catch (JsonException exception)
                {
                    failedProviders++;
                    Log.Warning(exception, "A game manifest from provider {ProviderId} is invalid.", provider.Id);
                }
            }

            var uniqueGames = discoveredGames
                .DistinctBy(game => $"{game.ProviderId}:{game.ExternalId}", StringComparer.OrdinalIgnoreCase);
            Games.Clear();
            foreach (var game in OrderGames(uniqueGames))
            {
                Games.Add(game);
            }

            _failedGameProviders = failedProviders;
            _hasScannedGames = true;
            OnPropertyChanged(nameof(SteamGames));
            if (SelectedAchievementGame is null || !uniqueGames.Any(game => game == SelectedAchievementGame))
            {
                SelectedAchievementGame = Games.FirstOrDefault(game => game.ProviderId == "steam");
            }
            UpdateGamesStatusText();
            OnPropertyChanged(nameof(HasGames));
            OnPropertyChanged(nameof(IsGamesEmpty));
            RefreshFilteredGames();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            IsLoadingGames = false;
        }
    }

    [RelayCommand]
    private void SaveSteamApiKey()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SteamApiKey))
            {
                _steamApiKeyStore.Delete();
                AchievementsStatusText = LocalizationManager.Instance["Achievements.KeyRemoved"];
            }
            else
            {
                _steamApiKeyStore.Save(SteamApiKey);
                SteamApiKey = SteamApiKey.Trim();
                AchievementsStatusText = LocalizationManager.Instance["Achievements.KeySaved"];
            }
        }
        catch (IOException exception)
        {
            Log.Error(exception, "Could not save the Steam API key.");
            AchievementsStatusText = LocalizationManager.Instance["Achievements.KeySaveFailed"];
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Error(exception, "Access was denied while saving the Steam API key.");
            AchievementsStatusText = LocalizationManager.Instance["Achievements.KeySaveFailed"];
        }
        catch (CryptographicException exception)
        {
            Log.Error(exception, "The Steam API key could not be encrypted.");
            AchievementsStatusText = LocalizationManager.Instance["Achievements.KeySaveFailed"];
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadAchievements))]
    private async Task LoadAchievementsAsync(CancellationToken cancellationToken)
    {
        var selectedGame = SelectedAchievementGame;
        if (selectedGame is null)
        {
            AchievementsStatusText = LocalizationManager.Instance["Achievements.SelectGame"];
            return;
        }

        if (string.IsNullOrWhiteSpace(SteamApiKey))
        {
            AchievementsStatusText = LocalizationManager.Instance["Achievements.KeyRequired"];
            return;
        }

        IsLoadingAchievements = true;
        try
        {
            var achievements = await _gameAchievementService.GetAchievementsAsync(
                selectedGame,
                SteamApiKey,
                cancellationToken);
            Achievements.Clear();
            foreach (var achievement in achievements)
            {
                Achievements.Add(achievement);
            }

            if (achievements.Count == 0)
            {
                AchievementsStatusText = LocalizationManager.Instance["Achievements.NoneFound"];
            }
            else
            {
                var unlockedCount = achievements.Count(achievement => achievement.IsUnlocked);
                AchievementsStatusText = string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizationManager.Instance["Achievements.Loaded"],
                    unlockedCount,
                    achievements.Count);
            }
            OnPropertyChanged(nameof(HasAchievements));
            OnPropertyChanged(nameof(UnlockedAchievementCount));
            OnPropertyChanged(nameof(AchievementProgress));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            Log.Warning(
                "Steam achievement request timed out for {GameId}: {ErrorType}.",
                selectedGame.ExternalId,
                exception.GetType().Name);
            AchievementsStatusText = LocalizationManager.Instance["Achievements.RequestFailed"];
        }
        catch (HttpRequestException exception)
        {
            Log.Warning(
                "Steam achievement request failed for {GameId} with HTTP error category {ErrorCategory}.",
                selectedGame.ExternalId,
                exception.HttpRequestError);
            AchievementsStatusText = LocalizationManager.Instance["Achievements.RequestFailed"];
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "Could not read the local Steam profile.");
            AchievementsStatusText = LocalizationManager.Instance["Achievements.ProfileUnavailable"];
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "Access to the local Steam profile was denied.");
            AchievementsStatusText = LocalizationManager.Instance["Achievements.ProfileUnavailable"];
        }
        catch (JsonException exception)
        {
            Log.Warning(exception, "Steam returned invalid achievement data for {GameId}.", selectedGame.ExternalId);
            AchievementsStatusText = LocalizationManager.Instance["Achievements.InvalidResponse"];
        }
        catch (InvalidOperationException exception)
        {
            Log.Information(exception, "Steam achievements are unavailable for {GameId}.", selectedGame.ExternalId);
            AchievementsStatusText = LocalizationManager.Instance["Achievements.GameUnavailable"];
        }
        finally
        {
            IsLoadingAchievements = false;
        }
    }

    [RelayCommand]
    private void SortGamesByPlaytime()
    {
        _sortGamesByPlaytime = true;
        var sortedGames = OrderGames(Games).ToArray();
        Games.Clear();
        foreach (var game in sortedGames)
        {
            Games.Add(game);
        }
        RefreshFilteredGames();
    }

    [RelayCommand]
    private async Task LaunchGameAsync(GameEntry? game, CancellationToken cancellationToken)
    {
        if (game is null)
        {
            return;
        }

        var provider = _gameProviders.FirstOrDefault(item =>
            string.Equals(item.Id, game.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            LaunchMessage = LocalizationManager.Instance["Games.ProviderUnavailable"];
            return;
        }

        var result = await provider.LaunchAsync(game, cancellationToken);
        LaunchMessage = result.Succeeded
            ? string.Empty
            : result.ErrorMessage ?? LocalizationManager.Instance["Home.LaunchFailed"];
    }

    public void Dispose()
    {
        _clockTimer.Dispose();
        LocalizationManager.Instance.PropertyChanged -= LocalizationChanged;
        GC.SuppressFinalize(this);
    }

    private void UpdateClock()
    {
        ClockText = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(ClockText));
    }

    private void LocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageSubtitle));
        UpdateLocalizedProperties();
    }

    private void UpdateLocalizedProperties()
    {
        ComingSoonText = LocalizationManager.Instance["Home.Empty"];
        ConnectivityText = LocalizationManager.Instance["Home.Ready"];
        UpdateGamesStatusText();
        RefreshFilteredGames();
    }

    private bool CanRefreshGames() => !IsLoadingGames;
    private bool CanCheckForUpdates() => !IsCheckingForUpdates && !IsApplyingUpdate;

    private bool CanApplyUpdate() =>
        _availableUpdate is not null
        && GitHubUpdateService.IsInstalled
        && !IsCheckingForUpdates
        && !IsApplyingUpdate;

    private void NotifyShortcutCollectionChanged()
    {
        OnPropertyChanged(nameof(HasShortcuts));
        OnPropertyChanged(nameof(IsShortcutsEmpty));
    }

    private bool CanLoadAchievements() =>
        !IsLoadingAchievements && SelectedAchievementGame?.ProviderId == "steam";

    private IEnumerable<GameEntry> OrderGames(IEnumerable<GameEntry> games) =>
        _sortGamesByPlaytime
            ? games.OrderByDescending(game => game.PlaytimeMinutes.HasValue)
                .ThenByDescending(game => game.PlaytimeMinutes.GetValueOrDefault())
                .ThenBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            : games.OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase);

    private void RefreshFilteredGames()
    {
        var filtered = GameLibraryFilter.Filter(Games, GameSearchQuery, SelectedGameProvider);
        FilteredGames.Clear();
        foreach (var game in filtered)
        {
            FilteredGames.Add(game);
        }

        OnPropertyChanged(nameof(HasFilteredGames));
        OnPropertyChanged(nameof(IsGameSearchEmpty));
        FilteredGamesStatusText = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Instance["Games.FilterCount"],
            FilteredGames.Count);
    }

    private void UpdateGamesStatusText()
    {
        if (!_hasScannedGames)
        {
            GamesStatusText = LocalizationManager.Instance["Games.Empty"];
            return;
        }

        var count = Games.Count.ToString(CultureInfo.CurrentCulture);
        GamesStatusText = _failedGameProviders == 0
            ? string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["Games.ScanComplete"], count)
            : string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Instance["Games.ScanPartial"],
                count,
                _failedGameProviders);
    }

    private AppSettings CurrentSettings => new(
        SelectedTheme,
        SelectedLanguage,
        RunAtStartup,
        StartMinimized,
        CloseToTray,
        MusicService: SelectedMusicService,
        Shortcuts: Shortcuts.ToArray());

    private static bool IsValidShortcut(string name, string uriText) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 50
        && Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
        && uriText.Length <= 2048
        && LaunchUriPolicy.IsAllowedUserShortcut(uriText);

    private void ApplySelectedMusicService()
    {
        var service = MusicServices.First(item => item.Id == SelectedMusicService);
        var musicTile = HomeTiles.First(tile => tile.Id is "spotify" or "deezer" or "youtubemusic");
        musicTile.SetMusicService(
            service.Id.ToLowerInvariant(),
            service.TitleKey,
            service.DescriptionKey,
            service.LaunchUri);
    }


    private static string PageTitle(string key) =>
        LocalizationManager.Instance[key switch
        {
            "Games" => "Navigation.Games",
            "Achievements" => "Navigation.Achievements",
            "Sound" => "Navigation.Sounds",
            "Shortcuts" => "Navigation.Shortcuts",
            "Settings" => "Navigation.Settings",
            _ => "Navigation.Home"
        }];
}
