using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Windows;
using Arkadia.App.Services;
using Arkadia.App.ViewModels;
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
                services.AddSingleton<Core.Services.IAppDatabase, ArkadiaDatabase>();
                services.AddSingleton<Core.Services.IStartupTaskService, StartupTaskService>();
                services.AddSingleton<Core.Providers.IGameProvider, SteamGameProvider>();
                services.AddSingleton<Core.Providers.IGameProvider, EpicGamesProvider>();
                services.AddSingleton<Core.Services.IGameAchievementService>(serviceProvider =>
                    new SteamAchievementService(serviceProvider.GetRequiredService<HttpClient>()));
                services.AddSingleton<HttpClient>();
                services.AddSingleton<GitHubUpdateService>();
                services.AddSingleton<SteamApiKeyStore>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.Start();
        await _host.Services.GetRequiredService<Core.Services.IAppDatabase>()
            .InitializeAsync(CancellationToken.None);
        await _host.Services.GetRequiredService<MainViewModel>().InitializeAsync();
        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
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
