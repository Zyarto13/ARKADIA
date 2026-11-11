using System.Windows;
using System.Windows.Forms;
using Arkadia.App.Localization;

namespace Arkadia.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private MainWindow? _window;
    private bool _noticeShown;

    public TrayIconService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(LocalizationManager.Instance["Tray.Open"], null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(LocalizationManager.Instance["Tray.Quit"], null, (_, _) => Quit());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ARKADIA",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
        LocalizationManager.Instance.PropertyChanged += LocalizationChanged;
    }

    public void Attach(MainWindow window) => _window = window;

    public void HideToTray()
    {
        _window?.Hide();
        if (_noticeShown)
        {
            return;
        }

        _noticeShown = true;
        _notifyIcon.BalloonTipTitle = "ARKADIA";
        _notifyIcon.BalloonTipText = LocalizationManager.Instance["Tray.HiddenNotice"];
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    public void Quit()
    {
        if (_window is not null)
        {
            _window.IsQuitting = true;
            _window.Close();
        }

        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        LocalizationManager.Instance.PropertyChanged -= LocalizationChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void LocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_notifyIcon.ContextMenuStrip is { Items.Count: >= 3 } menu)
        {
            menu.Items[0].Text = LocalizationManager.Instance["Tray.Open"];
            menu.Items[2].Text = LocalizationManager.Instance["Tray.Quit"];
        }
    }
}
