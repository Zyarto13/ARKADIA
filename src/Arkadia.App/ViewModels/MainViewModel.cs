using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Security.Cryptography;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Arkadia.App.Icons;
using Arkadia.App.Localization;
using Arkadia.App.Services;
using Arkadia.Core.Models;
using Arkadia.Core.Providers;
using Arkadia.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using Serilog;

namespace Arkadia.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly string[] DefaultHomeTileOrder =
    [
        "music",
        "discord",
        "steam",
        "epic",
        "curseforge",
        "minecraft",
        "last-game",
        "achievement",
        "system",
        "shortcuts"
    ];

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
    private readonly WindowsAudioDeviceNotificationService _audioDeviceNotificationService = new();
    private readonly WindowsSystemMetricsService _systemMetricsService = new();
    private readonly System.Threading.Timer _clockTimer;
    private readonly System.Threading.Timer _mediaSessionTimer;
    private readonly System.Threading.Timer _audioMeterTimer;
    private readonly System.Threading.Timer _systemMetricsTimer;
    private readonly string _currentAppVersion;
    private string _gameSearchQuery = string.Empty;
    private string _selectedGameProvider = GameLibraryFilter.AllProviders;
    private string _selectedGameCollection = GameLibraryFilter.InstalledGames;
    private string _filteredGamesStatusText = string.Empty;
    private string _currentPageKey = "Home";
    private string _launchMessage = string.Empty;
    private string _comingSoonText = string.Empty;
    private string _connectivityText = string.Empty;
    private bool _isOnline;
    private double? _cpuUsagePercent;
    private double _memoryUsagePercent;
    private volatile bool _isApplicationVisible;
    private bool _isSettingsSelected;
    private bool _isHomeEditMode;
    private string _homeLayoutStatusText = string.Empty;
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
    private bool _steamOwnedLibraryLoadFailed;
    private string? _steamOwnedLibraryFailureDetail;
    private int _isRefreshingMediaSession;
    private int _isAudioRefreshQueued;
    private int _isAudioDeviceRefreshQueued;
    private int _isRefreshingAudioSessions;
    private string _audioSessionsStatusText = string.Empty;
    private string _audioDevicesStatusText = string.Empty;
    private string _audioProfileNameInput = string.Empty;
    private AudioVolumeProfile? _selectedAudioProfile;
    private bool _isUpdatingMusicVolume;
    private double _musicVolume;
    private bool _canAdjustMusicVolume;
    private bool _isMusicMuted;
    private bool _isMusicPlaying;
    private string _currentTrackTitle = string.Empty;
    private string _currentTrackArtist = string.Empty;
    private string? _currentTrackSourceAppUserModelId;
    private BitmapImage? _currentTrackArtwork;
    private string _steamApiKey = string.Empty;
    private IReadOnlyList<ManualGameDefinition> _manualGames = [];
    private string _achievementsStatusText = string.Empty;
    private GameEntry? _selectedAchievementGame;
    private bool _isLoadingAchievements;
    private CancellationTokenSource? _achievementLoadCancellation;
    private long _achievementLoadId;
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
        _isOnline = NetworkInterface.GetIsNetworkAvailable();
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
        VisibleHomeTiles = [];
        Games = [];
        FilteredGames = [];
        Achievements = [];
        Shortcuts = [];
        AudioSessions = [];
        AudioOutputDevices = [];
        AudioInputDevices = [];
        AudioProfiles = [];

        LocalizationManager.Instance.PropertyChanged += LocalizationChanged;
        _audioDeviceNotificationService.DevicesChanged += AudioDevicesChanged;
        UpdateLocalizedProperties();
        RefreshVisibleHomeTiles();
        UpdateClock();
        _clockTimer = new System.Threading.Timer(
            _ => _synchronizationContext?.Post(static state => ((MainViewModel)state!).UpdateClock(), this),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _mediaSessionTimer = new System.Threading.Timer(
            _ => _synchronizationContext?.Post(
                static state =>
                {
                    var viewModel = (MainViewModel)state!;
                    viewModel.RefreshMusicState();
                },
                this),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _audioMeterTimer = new System.Threading.Timer(
            _ =>
            {
                if (!IsSound
                    || Volatile.Read(ref _isRefreshingAudioSessions) != 0
                    || Interlocked.Exchange(ref _isAudioRefreshQueued, 1) != 0
                    || _synchronizationContext is null)
                {
                    Volatile.Write(ref _isAudioRefreshQueued, 0);
                    return;
                }

                _synchronizationContext.Post(
                    static state =>
                    {
                        var viewModel = (MainViewModel)state!;
                        try
                        {
                            if (viewModel.IsSound)
                            {
                                viewModel.QueueAudioSessionRefresh();
                            }
                        }
                        finally
                        {
                            Volatile.Write(ref viewModel._isAudioRefreshQueued, 0);
                        }
                    },
                    this);
            },
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _systemMetricsTimer = new System.Threading.Timer(
            _ => RefreshSystemMetrics(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChanged;
        SelectPage("Home");
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public ObservableCollection<HomeTileViewModel> HomeTiles { get; }
    public ObservableCollection<HomeTileViewModel> VisibleHomeTiles { get; }
    public ObservableCollection<GameEntry> Games { get; }
    public ObservableCollection<GameEntry> FilteredGames { get; }
    public ObservableCollection<GameAchievement> Achievements { get; }
    public ObservableCollection<AppShortcut> Shortcuts { get; }
    public ObservableCollection<AudioSessionViewModel> AudioSessions { get; }
    public ObservableCollection<AudioDeviceViewModel> AudioOutputDevices { get; }
    public ObservableCollection<AudioDeviceViewModel> AudioInputDevices { get; }
    public ObservableCollection<AudioVolumeProfile> AudioProfiles { get; }
    public bool IsHome => _currentPageKey == "Home";
    public bool IsGames => _currentPageKey == "Games";
    public bool IsAchievements => _currentPageKey == "Achievements";
    public bool IsSound => _currentPageKey == "Sound";
    public bool IsShortcuts => _currentPageKey == "Shortcuts";
    public bool IsSettings => _currentPageKey == "Settings";
    public bool IsHomeEditMode
    {
        get => _isHomeEditMode;
        private set
        {
            if (SetProperty(ref _isHomeEditMode, value))
            {
                OnPropertyChanged(nameof(HomeLayoutModeLabel));
                RefreshVisibleHomeTiles();
            }
        }
    }

    public string HomeLayoutModeLabel => LocalizationManager.Instance[
        IsHomeEditMode ? "Home.Layout.Done" : "Home.Layout.Edit"];

    public string HomeLayoutStatusText
    {
        get => _homeLayoutStatusText;
        private set => SetProperty(ref _homeLayoutStatusText, value);
    }
    public bool IsComingSoon => !IsHome && !IsGames && !IsAchievements && !IsSound && !IsShortcuts && !IsSettings;
    public bool HasGames => Games.Count > 0;
    public bool HasFilteredGames => FilteredGames.Count > 0;
    public bool IsGameSearchEmpty => HasGames && !HasFilteredGames && !IsSteamOwnedGamesUnavailable;
    public bool IsSteamOwnedGamesUnavailable =>
        (SelectedGameCollection is GameLibraryFilter.OwnedGames or GameLibraryFilter.NotInstalledGames)
        && (string.IsNullOrWhiteSpace(SteamApiKey) || _steamOwnedLibraryLoadFailed);
    public string SteamOwnedGamesStatusText => string.IsNullOrWhiteSpace(SteamApiKey)
        ? LocalizationManager.Instance["Games.OwnedKeyRequired"]
        : !string.IsNullOrWhiteSpace(_steamOwnedLibraryFailureDetail)
            ? string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Instance["Games.OwnedScanFailedDetails"],
                _steamOwnedLibraryFailureDetail)
        : LocalizationManager.Instance["Games.OwnedScanFailed"];
    public bool HasShortcuts => Shortcuts.Count > 0;
    public bool IsShortcutsEmpty => Shortcuts.Count == 0;
    public bool HasAudioSessions => AudioSessions.Count > 0;
    public bool HasAudioOutputDevices => AudioOutputDevices.Count > 0;
    public bool HasAudioInputDevices => AudioInputDevices.Count > 0;
    public bool HasAudioProfiles => AudioProfiles.Count > 0;
    public bool CanApplyAudioProfile => SelectedAudioProfile is not null;
    public string AudioSessionsStatusText
    {
        get => _audioSessionsStatusText;
        private set => SetProperty(ref _audioSessionsStatusText, value);
    }
    public string AudioDevicesStatusText
    {
        get => _audioDevicesStatusText;
        private set => SetProperty(ref _audioDevicesStatusText, value);
    }

    public string AudioProfileNameInput
    {
        get => _audioProfileNameInput;
        set => SetProperty(ref _audioProfileNameInput, value);
    }

    public AudioVolumeProfile? SelectedAudioProfile
    {
        get => _selectedAudioProfile;
        set
        {
            if (SetProperty(ref _selectedAudioProfile, value))
            {
                OnPropertyChanged(nameof(CanApplyAudioProfile));
            }
        }
    }
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
            "Sound" => "Sound.Subtitle",
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

    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (SetProperty(ref _isOnline, value))
            {
                ConnectivityText = LocalizationManager.Instance[value ? "Home.Online" : "Home.Offline"];
            }
        }
    }

    public string SystemMetricsText
    {
        get
        {
            var cpu = _cpuUsagePercent is { } cpuPercent
                ? $"{cpuPercent:0}%"
                : "—";
            return string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Instance["Home.SystemMetrics"],
                cpu,
                $"{_memoryUsagePercent:0}%");
        }
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

    [RelayCommand]
    private void SelectGameCollection(string? collection)
    {
        if (collection is GameLibraryFilter.AllGames
            or GameLibraryFilter.InstalledGames
            or GameLibraryFilter.OwnedGames
            or GameLibraryFilter.NotInstalledGames)
        {
            SelectedGameCollection = collection;
        }
    }

    public string SelectedGameCollection
    {
        get => _selectedGameCollection;
        set
        {
            if (SetProperty(ref _selectedGameCollection, value))
            {
                OnPropertyChanged(nameof(IsAllGamesSelected));
                OnPropertyChanged(nameof(IsInstalledGamesSelected));
                OnPropertyChanged(nameof(IsOwnedGamesSelected));
                OnPropertyChanged(nameof(IsNotInstalledGamesSelected));
                RefreshFilteredGames();
                OnPropertyChanged(nameof(IsSteamOwnedGamesUnavailable));
            }
        }
    }

    public bool IsAllGamesSelected => SelectedGameCollection == GameLibraryFilter.AllGames;
    public bool IsInstalledGamesSelected => SelectedGameCollection == GameLibraryFilter.InstalledGames;
    public bool IsOwnedGamesSelected => SelectedGameCollection == GameLibraryFilter.OwnedGames;
    public bool IsNotInstalledGamesSelected => SelectedGameCollection == GameLibraryFilter.NotInstalledGames;

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
        set
        {
            if (SetProperty(ref _steamApiKey, value))
            {
                OnPropertyChanged(nameof(IsSteamOwnedGamesUnavailable));
                OnPropertyChanged(nameof(SteamOwnedGamesStatusText));
                RefreshFilteredGames();
            }
        }
    }

    public GameEntry? SelectedAchievementGame
    {
        get => _selectedAchievementGame;
        set
        {
            if (SetProperty(ref _selectedAchievementGame, value))
            {
                CancelAchievementLoad();
                Achievements.Clear();
                OnPropertyChanged(nameof(HasAchievements));
                OnPropertyChanged(nameof(UnlockedAchievementCount));
                OnPropertyChanged(nameof(AchievementProgress));
                AchievementsStatusText = value is null || value.ProviderId == "steam"
                    ? string.Empty
                    : LocalizationManager.Instance["Achievements.UnsupportedProvider"];
                LoadAchievementsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private void CancelAchievementLoad()
    {
        Interlocked.Increment(ref _achievementLoadId);
        Interlocked.Exchange(ref _achievementLoadCancellation, null)?.Cancel();
        IsLoadingAchievements = false;
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
    public string CurrentTrackTitle
    {
        get => _currentTrackTitle;
        private set
        {
            if (SetProperty(ref _currentTrackTitle, value))
            {
                OnPropertyChanged(nameof(HasCurrentTrack));
                OnPropertyChanged(nameof(MusicPlayerTrackTitle));
                OnPropertyChanged(nameof(MusicPlayerTrackArtist));
            }
        }
    }

    public string CurrentTrackArtist
    {
        get => _currentTrackArtist;
        private set
        {
            if (SetProperty(ref _currentTrackArtist, value))
            {
                OnPropertyChanged(nameof(MusicPlayerTrackArtist));
            }
        }
    }

    public BitmapImage? CurrentTrackArtwork
    {
        get => _currentTrackArtwork;
        private set => SetProperty(ref _currentTrackArtwork, value);
    }

    public bool HasCurrentTrack => !string.IsNullOrWhiteSpace(CurrentTrackTitle);
    public string MusicPlayerTrackTitle => HasCurrentTrack
        ? CurrentTrackTitle
        : LocalizationManager.Instance["Home.MediaControl.NoTrack"];
    public string MusicPlayerTrackArtist => HasCurrentTrack
        ? CurrentTrackArtist
        : SelectedMusicService;

    public bool IsMusicPlaying
    {
        get => _isMusicPlaying;
        private set
        {
            if (SetProperty(ref _isMusicPlaying, value))
            {
                OnPropertyChanged(nameof(MusicPlaybackTooltip));
            }
        }
    }
    public string MusicPlaybackTooltip => LocalizationManager.Instance[
        IsMusicPlaying ? "Home.MediaControl.Pause" : "Home.MediaControl.Play"];

    public double MusicVolume
    {
        get => _musicVolume;
        set
        {
            if (!SetProperty(ref _musicVolume, Math.Clamp(value, 0d, 1d)))
            {
                return;
            }

            OnPropertyChanged(nameof(MusicVolumePercent));
            if (_isUpdatingMusicVolume)
            {
                return;
            }

            try
            {
                CanAdjustMusicVolume = WindowsAudioSessionService.TrySetVolume(
                    SelectedMusicService,
                    (float)_musicVolume,
                    _currentTrackSourceAppUserModelId);
            }
            catch (COMException exception)
            {
                CanAdjustMusicVolume = false;
                Log.Warning(exception, "Could not set the volume for {MusicService}.", SelectedMusicService);
            }
            catch (Win32Exception exception)
            {
                CanAdjustMusicVolume = false;
                Log.Warning(exception, "Could not access the audio session for {MusicService}.", SelectedMusicService);
            }
        }
    }

    public int MusicVolumePercent
    {
        get => (int)Math.Round(_musicVolume * 100, MidpointRounding.AwayFromZero);
        set => MusicVolume = Math.Clamp(value, 0, 100) / 100d;
    }

    public bool CanAdjustMusicVolume
    {
        get => _canAdjustMusicVolume;
        private set => SetProperty(ref _canAdjustMusicVolume, value);
    }

    public bool IsMusicMuted
    {
        get => _isMusicMuted;
        private set
        {
            if (SetProperty(ref _isMusicMuted, value))
            {
                OnPropertyChanged(nameof(MusicMuteTooltip));
            }
        }
    }

    public string MusicMuteTooltip => LocalizationManager.Instance[
        IsMusicMuted ? "Home.MediaControl.Unmute" : "Home.MediaControl.Mute"];

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
                OnPropertyChanged(nameof(MusicPlayerTrackArtist));
                var musicTile = HomeTiles.First(tile => tile.Id is "spotify" or "deezer" or "youtubemusic");
                musicTile.SetMusicService(
                    service.Id.ToLowerInvariant(),
                    service.TitleKey,
                    service.DescriptionKey,
                    service.LaunchUri);
                RefreshMusicVolume();
            }
        }
    }

    public IReadOnlyList<MusicServiceOption> MusicServices { get; } =
    [
        new("Spotify", "Tile.Music.Spotify", "Tile.Music.Description", "spotify:"),
        new("Deezer", "Tile.Music.Deezer", "Tile.Music.Description", "deezer://"),
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
        OnPropertyChanged(nameof(IsSound));
        OnPropertyChanged(nameof(IsShortcuts));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsComingSoon));
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageSubtitle));
        LaunchMessage = string.Empty;
        if (IsSound && _isApplicationVisible)
        {
            try
            {
                _audioDeviceNotificationService.Start();
            }
            catch (COMException exception)
            {
                Log.Warning(exception, "Could not subscribe to Windows audio device changes.");
                AudioDevicesStatusText = LocalizationManager.Instance["Sound.Unavailable"];
            }
            catch (Win32Exception exception)
            {
                Log.Warning(exception, "Could not subscribe to Windows audio device changes.");
                AudioDevicesStatusText = LocalizationManager.Instance["Sound.Unavailable"];
            }

            RefreshAudioMixer();
            _audioMeterTimer.Change(TimeSpan.FromMilliseconds(33), TimeSpan.FromMilliseconds(33));
        }
        else
        {
            _audioMeterTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            StopAudioDeviceNotifications();
        }
    }

    private async Task RefreshAudioSessionsAsync()
    {
        if (Interlocked.Exchange(ref _isRefreshingAudioSessions, 1) != 0)
        {
            return;
        }

        try
        {
            var snapshots = await Task.Run(WindowsAudioSessionService.GetActiveSessions);
            if (!IsSound)
            {
                return;
            }

            var existingSessions = AudioSessions.ToDictionary(session => session.Id, StringComparer.Ordinal);
            foreach (var snapshot in snapshots)
            {
                if (existingSessions.Remove(snapshot.Id, out var existing))
                {
                    existing.ApplySnapshot(snapshot);
                }
                else
                {
                    AudioSessions.Add(new AudioSessionViewModel(
                        snapshot,
                        SetAudioSessionVolume,
                        SetAudioSessionMuted));
                }
            }

            foreach (var staleSession in existingSessions.Values)
            {
                AudioSessions.Remove(staleSession);
            }

            OnPropertyChanged(nameof(HasAudioSessions));
            AudioSessionsStatusText = snapshots.Count == 0
                ? LocalizationManager.Instance["Sound.NoApps"]
                : string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizationManager.Instance["Sound.ActiveApps"],
                    snapshots.Count);
        }
        catch (COMException exception)
        {
            Log.Warning(exception, "Could not enumerate Windows audio sessions.");
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
        catch (Win32Exception exception)
        {
            Log.Warning(exception, "Could not access Windows audio sessions.");
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
        finally
        {
            Volatile.Write(ref _isRefreshingAudioSessions, 0);
        }
    }

    private void QueueAudioSessionRefresh() => _ = RefreshAudioSessionsAsync();

    private void AudioDevicesChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _isAudioDeviceRefreshQueued, 1) != 0
            || _synchronizationContext is null)
        {
            Volatile.Write(ref _isAudioDeviceRefreshQueued, 0);
            return;
        }

        _synchronizationContext.Post(
            static state =>
            {
                var viewModel = (MainViewModel)state!;
                try
                {
                    if (viewModel.IsSound)
                    {
                        viewModel.RefreshAudioDevices();
                    }
                }
                finally
                {
                    Volatile.Write(ref viewModel._isAudioDeviceRefreshQueued, 0);
                }
            },
            this);
    }

    private void StopAudioDeviceNotifications()
    {
        try
        {
            _audioDeviceNotificationService.Stop();
        }
        catch (COMException exception)
        {
            Log.Warning(exception, "Could not unsubscribe from Windows audio device changes.");
        }
        catch (Win32Exception exception)
        {
            Log.Warning(exception, "Could not release the Windows audio device notification service.");
        }
    }

    [RelayCommand]
    private void RefreshAudioMixer()
    {
        RefreshAudioDevices();
        _ = RefreshAudioSessionsAsync();
    }

    private void RefreshAudioDevices()
    {
        try
        {
            UpdateAudioDeviceCollection(
                AudioOutputDevices,
                WindowsAudioSessionService.GetDevices(DataFlow.Render));
            UpdateAudioDeviceCollection(
                AudioInputDevices,
                WindowsAudioSessionService.GetDevices(DataFlow.Capture));
            OnPropertyChanged(nameof(HasAudioOutputDevices));
            OnPropertyChanged(nameof(HasAudioInputDevices));
            AudioDevicesStatusText = string.Empty;
        }
        catch (COMException exception)
        {
            Log.Warning(exception, "Could not enumerate Windows audio devices.");
            AudioDevicesStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
        catch (Win32Exception exception)
        {
            Log.Warning(exception, "Could not access Windows audio devices.");
            AudioDevicesStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
    }

    private void UpdateAudioDeviceCollection(
        ObservableCollection<AudioDeviceViewModel> collection,
        IReadOnlyList<AudioDeviceSnapshot> snapshots)
    {
        var existingDevices = collection.ToDictionary(device => device.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            if (existingDevices.Remove(snapshot.Id, out var existing))
            {
                existing.ApplySnapshot(snapshot);
            }
            else
            {
                collection.Add(new AudioDeviceViewModel(
                    snapshot,
                    SetAudioDeviceVolume,
                    SetAudioDeviceMuted));
            }
        }

        foreach (var staleDevice in existingDevices.Values)
        {
            collection.Remove(staleDevice);
        }
    }

    private void SetAudioDeviceVolume(AudioDeviceViewModel device)
    {
        try
        {
            var flow = device.IsInput ? DataFlow.Capture : DataFlow.Render;
            if (!WindowsAudioSessionService.TrySetDeviceVolume(device.Id, flow, device.VolumePercent / 100f))
            {
                AudioDevicesStatusText = LocalizationManager.Instance["Sound.DeviceUnavailable"];
            }
        }
        catch (COMException exception)
        {
            Log.Warning(exception, "Could not change the volume of a Windows audio device.");
            AudioDevicesStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
        catch (Win32Exception exception)
        {
            Log.Warning(exception, "Could not access a Windows audio device.");
            AudioDevicesStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
    }

    private void SetAudioDeviceMuted(AudioDeviceViewModel device)
    {
        try
        {
            var flow = device.IsInput ? DataFlow.Capture : DataFlow.Render;
            if (!WindowsAudioSessionService.TrySetDeviceMuted(device.Id, flow, device.IsMuted))
            {
                AudioDevicesStatusText = LocalizationManager.Instance["Sound.DeviceUnavailable"];
            }
        }
        catch (COMException exception)
        {
            Log.Warning(exception, "Could not change the mute state of a Windows audio device.");
            AudioDevicesStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
        catch (Win32Exception exception)
        {
            Log.Warning(exception, "Could not access a Windows audio device.");
            AudioDevicesStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
    }

    private void SetAudioSessionVolume(AudioSessionViewModel audioSession)
    {
        try
        {
            if (!WindowsAudioSessionService.TrySetSessionVolume(
                    audioSession.Id,
                    audioSession.VolumePercent / 100f))
            {
                AudioSessionsStatusText = LocalizationManager.Instance["Sound.SessionEnded"];
            }
        }
        catch (COMException exception)
        {
            Log.Warning(exception, "Could not change the volume of a Windows audio session.");
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
        catch (Win32Exception exception)
        {
            Log.Warning(exception, "Could not access a Windows audio session.");
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
    }

    private void SetAudioSessionMuted(AudioSessionViewModel audioSession)
    {
        try
        {
            if (!WindowsAudioSessionService.TrySetSessionMuted(audioSession.Id, audioSession.IsMuted))
            {
                AudioSessionsStatusText = LocalizationManager.Instance["Sound.SessionEnded"];
            }
        }
        catch (COMException exception)
        {
            Log.Warning(exception, "Could not change the mute state of a Windows audio session.");
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
        catch (Win32Exception exception)
        {
            Log.Warning(exception, "Could not access a Windows audio session.");
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.Unavailable"];
        }
    }

    [RelayCommand]
    private async Task SaveAudioProfileAsync(CancellationToken cancellationToken)
    {
        var name = AudioProfileNameInput.Trim();
        if (name.Length is 0 or > 40)
        {
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.ProfileNameInvalid"];
            return;
        }

        if (AudioProfiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.ProfileExists"];
            return;
        }

        var savedSessions = AudioSessions
            .Where(session => !string.IsNullOrWhiteSpace(session.ProcessName))
            .GroupBy(session => session.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(session => new AudioVolumeSetting(
                session.ProcessName,
                session.VolumePercent / 100f,
                session.IsMuted))
            .ToArray();
        var savedDevices = AudioOutputDevices
            .Concat(AudioInputDevices)
            .Select(device => new AudioDeviceVolumeSetting(
                device.Id,
                device.IsInput,
                device.VolumePercent / 100f,
                device.IsMuted))
            .ToArray();
        if (savedSessions.Length == 0 && savedDevices.Length == 0)
        {
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.NoAudioToSave"];
            return;
        }

        var profile = new AudioVolumeProfile(name, savedSessions, savedDevices);
        AudioProfiles.Add(profile);
        SelectedAudioProfile = profile;
        try
        {
            await _settingsService.SaveAsync(CurrentSettings, cancellationToken);
            AudioProfileNameInput = string.Empty;
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.ProfileSaved"];
            OnPropertyChanged(nameof(HasAudioProfiles));
        }
        catch (IOException exception)
        {
            AudioProfiles.Remove(profile);
            SelectedAudioProfile = null;
            Log.Error(exception, "Could not save an audio profile.");
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.ProfileSaveFailed"];
        }
        catch (UnauthorizedAccessException exception)
        {
            AudioProfiles.Remove(profile);
            SelectedAudioProfile = null;
            Log.Error(exception, "Access was denied while saving an audio profile.");
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.ProfileSaveFailed"];
        }
    }

    [RelayCommand]
    private void ApplyAudioProfile()
    {
        var profile = SelectedAudioProfile;
        if (profile is null)
        {
            return;
        }

        var settings = profile.Sessions.ToDictionary(
            setting => setting.ProcessName,
            StringComparer.OrdinalIgnoreCase);
        var deviceSettings = (profile.Devices ?? [])
            .ToDictionary(
                setting => $"{setting.IsInput}\u001f{setting.DeviceId}",
                StringComparer.OrdinalIgnoreCase);
        var appliedCount = 0;
        foreach (var device in AudioOutputDevices.Concat(AudioInputDevices))
        {
            var key = $"{device.IsInput}\u001f{device.Id}";
            if (!deviceSettings.TryGetValue(key, out var setting))
            {
                continue;
            }

            device.VolumePercent = (int)Math.Round(setting.Volume * 100, MidpointRounding.AwayFromZero);
            device.IsMuted = setting.IsMuted;
            appliedCount++;
        }

        foreach (var session in AudioSessions)
        {
            if (!settings.TryGetValue(session.ProcessName, out var setting))
            {
                continue;
            }

            session.VolumePercent = (int)Math.Round(setting.Volume * 100, MidpointRounding.AwayFromZero);
            session.IsMuted = setting.IsMuted;
            appliedCount++;
        }

        AudioSessionsStatusText = appliedCount == 0
            ? LocalizationManager.Instance["Sound.ProfileNoMatches"]
            : string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Instance["Sound.ProfileApplied"],
                appliedCount);
    }

    [RelayCommand]
    private async Task DeleteAudioProfileAsync(CancellationToken cancellationToken)
    {
        var profile = SelectedAudioProfile;
        if (profile is null)
        {
            return;
        }

        AudioProfiles.Remove(profile);
        SelectedAudioProfile = null;
        try
        {
            await _settingsService.SaveAsync(CurrentSettings, cancellationToken);
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.ProfileDeleted"];
            OnPropertyChanged(nameof(HasAudioProfiles));
        }
        catch (IOException exception)
        {
            AudioProfiles.Add(profile);
            SelectedAudioProfile = profile;
            Log.Error(exception, "Could not delete an audio profile.");
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.ProfileSaveFailed"];
        }
        catch (UnauthorizedAccessException exception)
        {
            AudioProfiles.Add(profile);
            SelectedAudioProfile = profile;
            Log.Error(exception, "Access was denied while deleting an audio profile.");
            AudioSessionsStatusText = LocalizationManager.Instance["Sound.ProfileSaveFailed"];
        }
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
        foreach (var profile in settings.AudioProfiles ?? [])
        {
            if (!string.IsNullOrWhiteSpace(profile.Name)
                && profile.Sessions is { } sessions)
            {
                AudioProfiles.Add(new AudioVolumeProfile(
                    profile.Name,
                    sessions.Where(session => !string.IsNullOrWhiteSpace(session.ProcessName))
                        .Select(session => session with { Volume = Math.Clamp(session.Volume, 0f, 1f) })
                        .ToArray(),
                    (profile.Devices ?? [])
                        .Select(device => device with { Volume = Math.Clamp(device.Volume, 0f, 1f) })
                        .ToArray()));
            }
        }
        SelectedAudioProfile = AudioProfiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasAudioProfiles));
        foreach (var shortcut in settings.Shortcuts ?? [])
        {
            if (IsValidShortcut(shortcut.Name, shortcut.Uri))
            {
                Shortcuts.Add(shortcut);
            }
        }
        _manualGames = settings.ManualGames ?? [];
        NotifyShortcutCollectionChanged();
        _selectedMusicService = MusicServices.FirstOrDefault(service =>
            string.Equals(service.Id, settings.MusicService, StringComparison.OrdinalIgnoreCase))?.Id
            ?? "Spotify";
        ApplySelectedMusicService();
        ApplyHomeTileLayout(settings.HomeTiles);
        RefreshMusicVolume();
        await RefreshMusicSessionAsync();
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
    private async Task OpenMusicAppAsync(CancellationToken cancellationToken)
    {
        var musicTile = HomeTiles.First(tile =>
            tile.Id is "spotify" or "deezer" or "youtubemusic");
        await LaunchTileAsync(musicTile, cancellationToken);
    }

    [RelayCommand]
    private void ToggleHomeEditMode()
    {
        IsHomeEditMode = !IsHomeEditMode;
    }

    [RelayCommand]
    private async Task ResetHomeTileLayoutAsync(CancellationToken cancellationToken)
    {
        foreach (var tile in HomeTiles)
        {
            tile.IsVisible = true;
            tile.ColumnSpan = 1;
            tile.RowSpan = 1;
        }

        for (var targetIndex = 0; targetIndex < DefaultHomeTileOrder.Length; targetIndex++)
        {
            var tile = HomeTiles.FirstOrDefault(item =>
                string.Equals(item.LayoutId, DefaultHomeTileOrder[targetIndex], StringComparison.Ordinal));
            if (tile is null)
            {
                continue;
            }

            var currentIndex = HomeTiles.IndexOf(tile);
            if (currentIndex != targetIndex)
            {
                HomeTiles.Move(currentIndex, targetIndex);
            }
        }

        RefreshVisibleHomeTiles();
        await PersistHomeTileLayoutAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task MoveHomeTileUpAsync(HomeTileViewModel? tile, CancellationToken cancellationToken)
    {
        await MoveHomeTileAsync(tile, -1, cancellationToken);
    }

    [RelayCommand]
    private async Task MoveHomeTileDownAsync(HomeTileViewModel? tile, CancellationToken cancellationToken)
    {
        await MoveHomeTileAsync(tile, 1, cancellationToken);
    }

    [RelayCommand]
    private async Task ReorderHomeTileAsync(
        HomeTileDropRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return;
        }

        var source = HomeTiles.FirstOrDefault(tile =>
            string.Equals(tile.LayoutId, request.SourceId, StringComparison.Ordinal));
        var target = HomeTiles.FirstOrDefault(tile =>
            string.Equals(tile.LayoutId, request.TargetId, StringComparison.Ordinal));
        if (source is null || target is null)
        {
            return;
        }

        var sourceIndex = HomeTiles.IndexOf(source);
        var targetIndex = HomeTiles.IndexOf(target);
        if (sourceIndex == targetIndex)
        {
            return;
        }

        HomeTiles.Move(sourceIndex, sourceIndex < targetIndex ? targetIndex - 1 : targetIndex);
        RefreshVisibleHomeTiles();
        await PersistHomeTileLayoutAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ToggleHomeTileVisibilityAsync(HomeTileViewModel? tile, CancellationToken cancellationToken)
    {
        if (tile is null)
        {
            return;
        }

        tile.IsVisible = !tile.IsVisible;
        RefreshVisibleHomeTiles();
        await PersistHomeTileLayoutAsync(cancellationToken);
    }

    [RelayCommand]
    private Task DecreaseHomeTileWidthAsync(HomeTileViewModel? tile, CancellationToken cancellationToken) =>
        ResizeHomeTileAsync(tile, -1, 0, cancellationToken);

    [RelayCommand]
    private Task IncreaseHomeTileWidthAsync(HomeTileViewModel? tile, CancellationToken cancellationToken) =>
        ResizeHomeTileAsync(tile, 1, 0, cancellationToken);

    [RelayCommand]
    private Task DecreaseHomeTileHeightAsync(HomeTileViewModel? tile, CancellationToken cancellationToken) =>
        ResizeHomeTileAsync(tile, 0, -1, cancellationToken);

    [RelayCommand]
    private Task IncreaseHomeTileHeightAsync(HomeTileViewModel? tile, CancellationToken cancellationToken) =>
        ResizeHomeTileAsync(tile, 0, 1, cancellationToken);

    private async Task ResizeHomeTileAsync(
        HomeTileViewModel? tile,
        int columnDelta,
        int rowDelta,
        CancellationToken cancellationToken)
    {
        if (tile is null)
        {
            return;
        }

        var columnSpan = tile.ColumnSpan + columnDelta;
        var rowSpan = tile.RowSpan + rowDelta;
        if (columnSpan is < 1 or > 3 || rowSpan is < 1 or > 3)
        {
            return;
        }

        tile.ColumnSpan = columnSpan;
        tile.RowSpan = rowSpan;
        await PersistHomeTileLayoutAsync(cancellationToken);
    }

    private async Task MoveHomeTileAsync(
        HomeTileViewModel? tile,
        int offset,
        CancellationToken cancellationToken)
    {
        if (tile is null)
        {
            return;
        }

        var currentIndex = HomeTiles.IndexOf(tile);
        var targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= HomeTiles.Count)
        {
            return;
        }

        HomeTiles.Move(currentIndex, targetIndex);
        RefreshVisibleHomeTiles();
        await PersistHomeTileLayoutAsync(cancellationToken);
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

    [RelayCommand]
    private async Task ToggleMusicPlaybackAsync()
    {
        await SendMusicMediaCommandAsync(
            MediaTransportCommand.TogglePlayPause,
            MediaControlService.TogglePlayPause,
            "Home.MediaControl.Pause");
    }

    [RelayCommand]
    private async Task PlayNextMusicTrackAsync()
    {
        await SendMusicMediaCommandAsync(
            MediaTransportCommand.PlayNext,
            MediaControlService.PlayNextTrack,
            "Home.MediaControl.Next");
    }

    [RelayCommand]
    private async Task PlayPreviousMusicTrackAsync()
    {
        await SendMusicMediaCommandAsync(
            MediaTransportCommand.PlayPrevious,
            MediaControlService.PlayPreviousTrack,
            "Home.MediaControl.Previous");
    }

    [RelayCommand]
    private void ToggleMusicMute()
    {
        if (!CanAdjustMusicVolume)
        {
            return;
        }

        try
        {
            if (!WindowsAudioSessionService.TrySetMuted(
                    SelectedMusicService,
                    !IsMusicMuted,
                    _currentTrackSourceAppUserModelId))
            {
                CanAdjustMusicVolume = false;
                LaunchMessage = LocalizationManager.Instance["Home.MediaControl.Failed"];
                return;
            }

            IsMusicMuted = !IsMusicMuted;
        }
        catch (COMException exception)
        {
            CanAdjustMusicVolume = false;
            Log.Warning(exception, "Could not mute the audio session for {MusicService}.", SelectedMusicService);
            LaunchMessage = LocalizationManager.Instance["Home.MediaControl.Failed"];
        }
        catch (Win32Exception exception)
        {
            CanAdjustMusicVolume = false;
            Log.Warning(exception, "Could not mute the audio session for {MusicService}.", SelectedMusicService);
            LaunchMessage = LocalizationManager.Instance["Home.MediaControl.Failed"];
        }
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

            var ownedGames = new List<GameEntry>();
            _steamOwnedLibraryLoadFailed = false;
            _steamOwnedLibraryFailureDetail = null;
            if (!string.IsNullOrWhiteSpace(SteamApiKey))
            {
                foreach (var provider in _gameProviders.OfType<ISteamOwnedGameProvider>())
                {
                    try
                    {
                        ownedGames.AddRange(await provider.ScanOwnedAsync(SteamApiKey, cancellationToken));
                    }
                    catch (HttpRequestException exception)
                    {
                        failedProviders++;
                        _steamOwnedLibraryLoadFailed = true;
                        _steamOwnedLibraryFailureDetail = exception.StatusCode is { } statusCode
                            ? $"HTTP {(int)statusCode} ({statusCode})"
                            : exception.HttpRequestError.ToString();
                        Log.Warning(exception, "Could not load the Steam owned-games library.");
                    }
                    catch (IOException exception)
                    {
                        failedProviders++;
                        _steamOwnedLibraryLoadFailed = true;
                        _steamOwnedLibraryFailureDetail = LocalizationManager.Instance["Games.OwnedLocalProfileUnavailable"];
                        Log.Warning(exception, "Could not read the local Steam profile for the owned-games library.");
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        failedProviders++;
                        _steamOwnedLibraryLoadFailed = true;
                        _steamOwnedLibraryFailureDetail = LocalizationManager.Instance["Games.OwnedLocalProfileUnavailable"];
                        Log.Warning(exception, "Access was denied while reading the local Steam profile.");
                    }
                    catch (JsonException exception)
                    {
                        failedProviders++;
                        _steamOwnedLibraryLoadFailed = true;
                        _steamOwnedLibraryFailureDetail = LocalizationManager.Instance["Games.OwnedInvalidResponse"];
                        Log.Warning(exception, "Steam returned invalid owned-games data.");
                    }
                    catch (InvalidOperationException exception)
                    {
                        failedProviders++;
                        _steamOwnedLibraryLoadFailed = true;
                        _steamOwnedLibraryFailureDetail = exception.Message;
                        Log.Warning(exception, "The current Steam profile could not be identified.");
                    }
                }
            }

            var uniqueGames = discoveredGames
                .Concat(ownedGames)
                .ToArray();
            var mergedGames = GameLibraryFilter.MergeDuplicates(uniqueGames).ToArray();
            Games.Clear();
            foreach (var game in OrderGames(mergedGames))
            {
                Games.Add(game);
            }

            _failedGameProviders = failedProviders;
            _hasScannedGames = true;
            OnPropertyChanged(nameof(SteamGames));
            if (SelectedAchievementGame is null || !mergedGames.Any(game => game == SelectedAchievementGame))
            {
                SelectedAchievementGame = Games.FirstOrDefault(game => game.ProviderId == "steam");
            }
            UpdateGamesStatusText();
            OnPropertyChanged(nameof(HasGames));
            OnPropertyChanged(nameof(IsGamesEmpty));
            OnPropertyChanged(nameof(IsSteamOwnedGamesUnavailable));
            OnPropertyChanged(nameof(SteamOwnedGamesStatusText));
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
    private async Task AddManualGameAsync(string? executablePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            LaunchMessage = LocalizationManager.Instance["Games.ManualInvalid"];
            return;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(executablePath);
        }
        catch (ArgumentException)
        {
            LaunchMessage = LocalizationManager.Instance["Games.ManualInvalid"];
            return;
        }
        catch (IOException)
        {
            LaunchMessage = LocalizationManager.Instance["Games.ManualInvalid"];
            return;
        }
        catch (NotSupportedException)
        {
            LaunchMessage = LocalizationManager.Instance["Games.ManualInvalid"];
            return;
        }

        var extension = Path.GetExtension(normalizedPath);
        if (!File.Exists(normalizedPath)
            || (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)))
        {
            LaunchMessage = LocalizationManager.Instance["Games.ManualInvalid"];
            return;
        }

        if (_manualGames.Any(game => string.Equals(
                game.ExecutablePath,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase)))
        {
            LaunchMessage = LocalizationManager.Instance["Games.ManualDuplicate"];
            return;
        }

        var game = new ManualGameDefinition(
            Guid.NewGuid(),
            Path.GetFileNameWithoutExtension(normalizedPath),
            normalizedPath);
        var updatedGames = _manualGames.Append(game).ToArray();
        try
        {
            await _settingsService.SaveAsync(
                CurrentSettings with { ManualGames = updatedGames },
                cancellationToken);
            _manualGames = updatedGames;
            await RefreshGamesAsync(cancellationToken);
            LaunchMessage = LocalizationManager.Instance["Games.ManualAdded"];
        }
        catch (IOException exception)
        {
            Log.Error(exception, "Could not save a manually added game.");
            LaunchMessage = LocalizationManager.Instance["Games.ManualSaveFailed"];
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Error(exception, "Access was denied while saving a manually added game.");
            LaunchMessage = LocalizationManager.Instance["Games.ManualSaveFailed"];
        }
    }

    [RelayCommand]
    private async Task SaveSteamApiKeyAsync(CancellationToken cancellationToken)
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
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Error(exception, "Access was denied while saving the Steam API key.");
            AchievementsStatusText = LocalizationManager.Instance["Achievements.KeySaveFailed"];
            return;
        }
        catch (CryptographicException exception)
        {
            Log.Error(exception, "The Steam API key could not be encrypted.");
            AchievementsStatusText = LocalizationManager.Instance["Achievements.KeySaveFailed"];
            return;
        }

        await RefreshGamesAsync(cancellationToken);
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

        var requestId = Interlocked.Increment(ref _achievementLoadId);
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Interlocked.Exchange(ref _achievementLoadCancellation, requestCancellation)?.Cancel();
        IsLoadingAchievements = true;
        try
        {
            var achievements = await _gameAchievementService.GetAchievementsAsync(
                selectedGame,
                SteamApiKey,
                requestCancellation.Token);
            if (!IsCurrentAchievementLoad(requestId, selectedGame, requestCancellation))
            {
                return;
            }

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
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException exception)
        {
            Log.Warning(
                "Steam achievement request timed out for {GameId}: {ErrorType}.",
                selectedGame.ExternalId,
                exception.GetType().Name);
            if (IsCurrentAchievementLoad(requestId, selectedGame, requestCancellation))
            {
                AchievementsStatusText = LocalizationManager.Instance["Achievements.RequestFailed"];
            }
        }
        catch (HttpRequestException exception)
        {
            Log.Warning(
                "Steam achievement request failed for {GameId} with HTTP status {StatusCode} and error category {ErrorCategory}.",
                selectedGame.ExternalId,
                exception.StatusCode,
                exception.HttpRequestError);
            if (IsCurrentAchievementLoad(requestId, selectedGame, requestCancellation))
            {
                AchievementsStatusText = exception.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                        LocalizationManager.Instance["Achievements.AccessDenied"],
                    System.Net.HttpStatusCode.NotFound =>
                        LocalizationManager.Instance["Achievements.GameUnavailable"],
                    _ => LocalizationManager.Instance["Achievements.RequestFailed"]
                };
            }
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "Could not read the local Steam profile.");
            if (IsCurrentAchievementLoad(requestId, selectedGame, requestCancellation))
            {
                AchievementsStatusText = LocalizationManager.Instance["Achievements.ProfileUnavailable"];
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "Access to the local Steam profile was denied.");
            if (IsCurrentAchievementLoad(requestId, selectedGame, requestCancellation))
            {
                AchievementsStatusText = LocalizationManager.Instance["Achievements.ProfileUnavailable"];
            }
        }
        catch (JsonException exception)
        {
            Log.Warning(exception, "Steam returned invalid achievement data for {GameId}.", selectedGame.ExternalId);
            if (IsCurrentAchievementLoad(requestId, selectedGame, requestCancellation))
            {
                AchievementsStatusText = LocalizationManager.Instance["Achievements.InvalidResponse"];
            }
        }
        catch (InvalidOperationException exception)
        {
            Log.Information(exception, "Steam achievements are unavailable for {GameId}.", selectedGame.ExternalId);
            if (IsCurrentAchievementLoad(requestId, selectedGame, requestCancellation))
            {
                AchievementsStatusText = exception.Message switch
                {
                    "No recently used Steam profile could be found." =>
                        LocalizationManager.Instance["Achievements.ProfileUnavailable"],
                    "Steam did not expose an achievement schema for this game." =>
                        LocalizationManager.Instance["Achievements.GameUnavailable"],
                    _ => string.Format(
                        CultureInfo.CurrentCulture,
                        LocalizationManager.Instance["Achievements.ApiError"],
                        exception.Message)
                };
            }
        }
        finally
        {
            if (requestId == Volatile.Read(ref _achievementLoadId))
            {
                Interlocked.CompareExchange(ref _achievementLoadCancellation, null, requestCancellation);
                IsLoadingAchievements = false;
            }

            requestCancellation.Dispose();
        }
    }

    private bool IsCurrentAchievementLoad(
        long requestId,
        GameEntry selectedGame,
        CancellationTokenSource requestCancellation) =>
        requestId == Volatile.Read(ref _achievementLoadId)
        && ReferenceEquals(SelectedAchievementGame, selectedGame)
        && ReferenceEquals(Volatile.Read(ref _achievementLoadCancellation), requestCancellation)
        && !requestCancellation.IsCancellationRequested;

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
        CancelAchievementLoad();
        _clockTimer.Dispose();
        _mediaSessionTimer.Dispose();
        _audioMeterTimer.Dispose();
        _systemMetricsTimer.Dispose();
        NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChanged;
        LocalizationManager.Instance.PropertyChanged -= LocalizationChanged;
        _audioDeviceNotificationService.DevicesChanged -= AudioDevicesChanged;
        StopAudioDeviceNotifications();
        GC.SuppressFinalize(this);
    }

    public void StartSystemMetrics() =>
        _systemMetricsTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));

    public void StopSystemMetrics() =>
        _systemMetricsTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    public void ShowDatabaseRecoveredNotice() =>
        LaunchMessage = LocalizationManager.Instance["Database.Recovered"];

    public void ShowDatabaseBackupFailureNotice() =>
        LaunchMessage = LocalizationManager.Instance["Database.BackupFailed"];

    public void SetApplicationVisible(bool isVisible)
    {
        if (_isApplicationVisible == isVisible)
        {
            return;
        }

        _isApplicationVisible = isVisible;
        if (!isVisible)
        {
            _clockTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _mediaSessionTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _audioMeterTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            StopSystemMetrics();
            StopAudioDeviceNotifications();
            return;
        }

        _clockTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(1));
        _mediaSessionTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        StartSystemMetrics();
        if (IsSound)
        {
            try
            {
                _audioDeviceNotificationService.Start();
            }
            catch (COMException exception)
            {
                Log.Warning(exception, "Could not subscribe to Windows audio device changes.");
                AudioDevicesStatusText = LocalizationManager.Instance["Sound.Unavailable"];
            }
            catch (Win32Exception exception)
            {
                Log.Warning(exception, "Could not subscribe to Windows audio device changes.");
                AudioDevicesStatusText = LocalizationManager.Instance["Sound.Unavailable"];
            }

            RefreshAudioMixer();
            _audioMeterTimer.Change(TimeSpan.FromMilliseconds(33), TimeSpan.FromMilliseconds(33));
        }
    }

    private void RefreshSystemMetrics()
    {
        if (!_isApplicationVisible)
        {
            return;
        }

        try
        {
            var snapshot = _systemMetricsService.Read();
            _synchronizationContext?.Post(
                static state =>
                {
                    var (viewModel, metrics) = ((MainViewModel, SystemMetricsSnapshot))state!;
                    viewModel._cpuUsagePercent = metrics.CpuUsagePercent;
                    viewModel._memoryUsagePercent = metrics.MemoryUsagePercent;
                    viewModel.OnPropertyChanged(nameof(SystemMetricsText));
                },
                (this, snapshot));
        }
        catch (Win32Exception exception)
        {
            Log.Warning(exception, "Could not read Windows system performance metrics.");
        }
    }

    private void NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        _synchronizationContext?.Post(
            static state =>
            {
                var (viewModel, isAvailable) = ((MainViewModel, bool))state!;
                viewModel.IsOnline = isAvailable;
            },
            (this, e.IsAvailable));
    }

    private async Task RefreshMusicSessionAsync()
    {
        if (Interlocked.Exchange(ref _isRefreshingMediaSession, 1) != 0)
        {
            return;
        }

        try
        {
            var snapshot = await WindowsMediaSessionService.GetCurrentSessionAsync(
                SelectedMusicService,
                CancellationToken.None);
            CurrentTrackTitle = snapshot?.Title ?? string.Empty;
            CurrentTrackArtist = snapshot?.Artist ?? string.Empty;
            IsMusicPlaying = snapshot?.IsPlaying ?? false;
            _currentTrackSourceAppUserModelId = snapshot?.SourceAppUserModelId;
            CurrentTrackArtwork = snapshot?.Artwork;
            RefreshMusicVolume();
        }
        catch (COMException exception)
        {
            ClearCurrentTrack();
            Log.Debug(exception, "Could not read the current Windows media session.");
        }
        catch (InvalidOperationException exception)
        {
            ClearCurrentTrack();
            Log.Debug(exception, "The current Windows media session is unavailable.");
        }
        catch (IOException exception)
        {
            ClearCurrentTrack();
            Log.Debug(exception, "Could not read the current media thumbnail.");
        }
        catch (System.IO.FileFormatException exception)
        {
            ClearCurrentTrack();
            Log.Debug(exception, "The current media thumbnail has an unsupported format.");
        }
        finally
        {
            Volatile.Write(ref _isRefreshingMediaSession, 0);
        }
    }

    private void ClearCurrentTrack()
    {
        CurrentTrackTitle = string.Empty;
        CurrentTrackArtist = string.Empty;
        IsMusicPlaying = false;
        _currentTrackSourceAppUserModelId = null;
        CurrentTrackArtwork = null;
    }

    private void RefreshMusicState()
    {
        _ = RefreshMusicSessionAsync();
        RefreshMusicVolume();
    }

    private void RefreshMusicVolume()
    {
        try
        {
            var volume = WindowsAudioSessionService.GetVolume(
                SelectedMusicService,
                _currentTrackSourceAppUserModelId);
            CanAdjustMusicVolume = volume.HasValue;
            if (volume.HasValue)
            {
                _isUpdatingMusicVolume = true;
                MusicVolume = volume.Value;
                IsMusicMuted = WindowsAudioSessionService.GetIsMuted(
                    SelectedMusicService,
                    _currentTrackSourceAppUserModelId) ?? false;
            }
            else
            {
                IsMusicMuted = false;
            }
        }
        catch (COMException exception)
        {
            CanAdjustMusicVolume = false;
            Log.Debug(exception, "Could not read the audio session for {MusicService}.", SelectedMusicService);
        }
        catch (Win32Exception exception)
        {
            CanAdjustMusicVolume = false;
            Log.Debug(exception, "Could not read the volume for {MusicService}.", SelectedMusicService);
        }
        finally
        {
            _isUpdatingMusicVolume = false;
        }
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
        OnPropertyChanged(nameof(SteamOwnedGamesStatusText));
        UpdateLocalizedProperties();
    }

    private void UpdateLocalizedProperties()
    {
        ComingSoonText = LocalizationManager.Instance["Home.Empty"];
        ConnectivityText = LocalizationManager.Instance[IsOnline ? "Home.Online" : "Home.Offline"];
        OnPropertyChanged(nameof(HomeLayoutModeLabel));
        OnPropertyChanged(nameof(SystemMetricsText));
        OnPropertyChanged(nameof(MusicPlaybackTooltip));
        OnPropertyChanged(nameof(MusicPlayerTrackTitle));
        OnPropertyChanged(nameof(MusicPlayerTrackArtist));
        OnPropertyChanged(nameof(MusicMuteTooltip));
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
        var filtered = GameLibraryFilter.Filter(
            Games,
            GameSearchQuery,
            SelectedGameProvider,
            SelectedGameCollection);
        FilteredGames.Clear();
        foreach (var game in filtered)
        {
            FilteredGames.Add(game);
        }

        OnPropertyChanged(nameof(HasFilteredGames));
        OnPropertyChanged(nameof(IsGameSearchEmpty));
        OnPropertyChanged(nameof(IsSteamOwnedGamesUnavailable));
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

        var installedCount = Games.Count(game => game.IsInstalled).ToString(CultureInfo.CurrentCulture);
        var ownedSteamCount = Games.Count(game => game.ProviderId == "steam" && game.IsOwned)
            .ToString(CultureInfo.CurrentCulture);
        GamesStatusText = _failedGameProviders == 0
            ? string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Instance["Games.ScanComplete"],
                installedCount,
                ownedSteamCount)
            : string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Instance["Games.ScanPartial"],
                installedCount,
                ownedSteamCount,
                _failedGameProviders);
    }

    private AppSettings CurrentSettings => new(
        SelectedTheme,
        SelectedLanguage,
        RunAtStartup,
        StartMinimized,
        CloseToTray,
        MusicService: SelectedMusicService,
        Shortcuts: Shortcuts.ToArray(),
        AudioProfiles: AudioProfiles.ToArray(),
        ManualGames: _manualGames.ToArray(),
        HomeTiles: HomeTiles
            .Select(tile => new HomeTileLayoutEntry(
                tile.LayoutId,
                tile.IsVisible,
                tile.ColumnSpan,
                tile.RowSpan))
            .ToArray());

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

    private void ApplyHomeTileLayout(IReadOnlyList<HomeTileLayoutEntry>? savedLayout)
    {
        if (savedLayout is null || savedLayout.Count == 0)
        {
            RefreshVisibleHomeTiles();
            return;
        }

        var tilesById = HomeTiles.ToDictionary(tile => tile.LayoutId, StringComparer.Ordinal);
        var savedIds = new HashSet<string>(StringComparer.Ordinal);
        if (savedLayout.Any(entry =>
                entry is null
                || !tilesById.ContainsKey(entry.Id)
                || entry.ColumnSpan is < 1 or > 3
                || entry.RowSpan is < 1 or > 3
                || !savedIds.Add(entry.Id)))
        {
            Log.Warning("The saved home tile layout is invalid; using the default layout.");
            RefreshVisibleHomeTiles();
            return;
        }

        foreach (var entry in savedLayout)
        {
            tilesById[entry.Id].IsVisible = entry.IsVisible;
            tilesById[entry.Id].ColumnSpan = entry.ColumnSpan;
            tilesById[entry.Id].RowSpan = entry.RowSpan;
        }

        var orderedTiles = savedLayout
            .Select(entry => tilesById[entry.Id])
            .Concat(HomeTiles.Where(tile => !savedIds.Contains(tile.LayoutId)))
            .ToArray();
        for (var targetIndex = 0; targetIndex < orderedTiles.Length; targetIndex++)
        {
            var currentIndex = HomeTiles.IndexOf(orderedTiles[targetIndex]);
            if (currentIndex != targetIndex)
            {
                HomeTiles.Move(currentIndex, targetIndex);
            }
        }

        RefreshVisibleHomeTiles();
    }

    private void RefreshVisibleHomeTiles()
    {
        VisibleHomeTiles.Clear();
        foreach (var tile in HomeTiles)
        {
            if (tile.IsVisible || IsHomeEditMode)
            {
                VisibleHomeTiles.Add(tile);
            }
        }
    }

    private async Task PersistHomeTileLayoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _settingsService.SaveAsync(CurrentSettings, cancellationToken);
            HomeLayoutStatusText = LocalizationManager.Instance["Home.Layout.Saved"];
        }
        catch (IOException exception)
        {
            Log.Error(exception, "Could not save the home tile layout.");
            HomeLayoutStatusText = LocalizationManager.Instance["Home.Layout.SaveFailed"];
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Error(exception, "Access was denied while saving the home tile layout.");
            HomeLayoutStatusText = LocalizationManager.Instance["Home.Layout.SaveFailed"];
        }
    }

    private async Task SendMusicMediaCommandAsync(
        MediaTransportCommand command,
        Action fallbackCommand,
        string commandName)
    {
        try
        {
            var sent = false;
            try
            {
                sent = await WindowsMediaSessionService.SendCommandAsync(
                    SelectedMusicService,
                    _currentTrackSourceAppUserModelId,
                    command,
                    CancellationToken.None);
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException)
            {
                Log.Debug(exception, "Could not send media command {CommandName} through the active {MusicService} session.", commandName, SelectedMusicService);
            }

            if (!sent)
            {
                fallbackCommand();
            }

            LaunchMessage = LocalizationManager.Instance["Home.MediaControl.Sent"];
            await RefreshMusicSessionAsync();
        }
        catch (Win32Exception exception)
        {
            Log.Warning(exception, "Could not send the media command {CommandName} for {MusicService}.", commandName, SelectedMusicService);
            LaunchMessage = LocalizationManager.Instance["Home.MediaControl.Failed"];
        }
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
