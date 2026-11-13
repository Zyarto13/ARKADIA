using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Windows;
using Arkadia.App.Services;
using Arkadia.App.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Arkadia.Infrastructure.Storage;
using Arkadia.Infrastructure.Providers;

namespace Arkadia.App;

public partial class App : System.Windows.Application, IDisposable
{
    private const string InstanceMutexName = @"Local\ARKADIA.SingleInstance";
    private const string ActivationEventName = @"Local\ARKADIA.Activate";
    private IHost? _host;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private readonly CancellationTokenSource _activationCancellation = new();
    private Task? _activationListener;

    private void Application_DispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var exceptionType = e.Exception.GetType().FullName ?? e.Exception.GetType().Name;
        Log.Fatal("An unhandled UI exception occurred: {ExceptionType}.", exceptionType);
        if (_host is null)
        {
            return;
        }

        try
        {
            var settings = _host.Services
                .GetRequiredService<Core.Services.ISettingsService>()
                .LoadAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (settings.Telemetry is { CrashReports: true } telemetry)
            {
                _host.Services
                    .GetRequiredService<TelemetryService>()
                    .RecordAsync(
                        "crash.unhandled_exception",
                        new { exceptionType },
                        telemetry,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException
                or InvalidOperationException or JsonException or SqliteException)
        {
            Log.Warning(
                "Could not queue an anonymous crash event ({FailureType}).",
                exception.GetType().FullName);
        }
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        var ownsMutex = false;
        _instanceMutex = new Mutex(initiallyOwned: false, InstanceMutexName);
        try
        {
            ownsMutex = _instanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            await ActivateExistingInstanceAsync();
            Shutdown();
            return;
        }

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _activationListener = Task.Run(ListenForActivation);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Arkadia",
            "logs");
        Directory.CreateDirectory(logDirectory);

        _host = Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .UseSerilog((_, logger) => logger
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDirectory, "arkadia-.log"),
                    formatProvider: CultureInfo.InvariantCulture,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14))
            .ConfigureServices(services =>
            {
                services.AddSingleton<Core.Services.IExternalLauncher, ExternalLauncher>();
                services.AddSingleton<Core.Services.ISettingsService>(_ => new JsonSettingsService());
                services.AddSingleton<ArkadiaDatabase>();
                services.AddSingleton<Core.Services.IAppDatabase>(provider =>
                    provider.GetRequiredService<ArkadiaDatabase>());
                services.AddSingleton<DataManagementService>();
                services.AddSingleton(provider => new TelemetryService(
                    provider.GetRequiredService<ArkadiaDatabase>()));
                services.AddSingleton<Core.Services.IStartupTaskService, StartupTaskService>();
                services.AddSingleton<Core.Providers.IGameProvider>(serviceProvider =>
                    new SteamGameProvider(
                        serviceProvider.GetRequiredService<Core.Services.IExternalLauncher>(),
                        serviceProvider.GetRequiredService<HttpClient>()));
                services.AddSingleton<Core.Providers.IGameProvider, EpicGamesProvider>();
                services.AddSingleton<Core.Providers.IGameProvider, MinecraftGameProvider>();
                services.AddSingleton<Core.Providers.IGameProvider, CurseForgeGameProvider>();
                services.AddSingleton<Core.Providers.IGameProvider, RinaorcGameProvider>();
                services.AddSingleton<Core.Providers.IGameProvider, ManualGameProvider>();
                services.AddSingleton<Core.Services.IGameAchievementService>(serviceProvider =>
                    new SteamAchievementService(serviceProvider.GetRequiredService<HttpClient>()));
                services.AddSingleton<HttpClient>();
                services.AddSingleton(provider => new GameArtworkService(
                    provider.GetRequiredService<HttpClient>()));
                services.AddSingleton(provider => new GitHubUpdateService(
                    provider.GetRequiredService<HttpClient>()));
                services.AddSingleton<SteamApiKeyStore>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.Start();
        var database = _host.Services.GetRequiredService<ArkadiaDatabase>();
        bool databaseRecovered;
        try
        {
            databaseRecovered = await database.RestoreLatestBackupIfCorruptedAsync(CancellationToken.None);
        }
        catch (InvalidDataException exception)
        {
            Log.Fatal(exception, "The local ARKADIA database is corrupted and cannot be recovered.");
            System.Windows.MessageBox.Show(
                Localization.LocalizationManager.Instance["Database.RecoveryFailed"],
                "ARKADIA",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        await database.InitializeAsync(CancellationToken.None);
        var backupFailed = false;
        try
        {
            await database.CreateDailyBackupAsync(CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            Log.Error(exception, "Could not create the daily ARKADIA database backup.");
            backupFailed = true;
        }

        var viewModel = _host.Services.GetRequiredService<MainViewModel>();
        await viewModel.InitializeAsync();
        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
        if (databaseRecovered)
        {
            viewModel.ShowDatabaseRecoveredNotice();
        }
        else if (backupFailed)
        {
            viewModel.ShowDatabaseBackupFailureNotice();
        }
        _host.Services.GetRequiredService<TrayIconService>();
        if (e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            _host.Services.GetRequiredService<TrayIconService>().HideToTray();
        }
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        if (_host is null)
        {
            Dispose();
            return;
        }

        await _host.StopAsync();
        _host.Services.GetRequiredService<TrayIconService>().Dispose();
        _host.Dispose();
        Dispose();
        Log.CloseAndFlush();
    }

    public void Dispose()
    {
        _activationCancellation.Cancel();
        _activationListener?.GetAwaiter().GetResult();
        _activationEvent?.Dispose();
        _instanceMutex?.Dispose();
        _activationCancellation.Dispose();
        _activationEvent = null;
        _instanceMutex = null;
        GC.SuppressFinalize(this);
    }

    private static async Task ActivateExistingInstanceAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                activationEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }
    }

    private void ListenForActivation()
    {
        var handles = new[] { _activationEvent!, _activationCancellation.Token.WaitHandle };
        while (WaitHandle.WaitAny(handles) == 0)
        {
            Dispatcher.BeginInvoke(() =>
                _host?.Services.GetRequiredService<TrayIconService>().ShowWindow());
        }
    }
}
