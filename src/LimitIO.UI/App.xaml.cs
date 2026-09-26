using System.Threading;
using System.Windows;
using LimitIO.UI.Ipc;
using LimitIO.UI.Tray;
using LimitIO.UI.Views;

namespace LimitIO.UI;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private ServiceClient? _serviceClient;
    private TrayIconManager? _trayIconManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Global\\LimitIO.UI.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // Already running (e.g. it auto-starts at login and the user also has a Start Menu shortcut) -
            // quietly exit rather than showing a second tray icon.
            Shutdown();
            return;
        }

        _serviceClient = new ServiceClient();

        var status = await _serviceClient.GetStatusAsync();
        if (status is null)
        {
            System.Windows.MessageBox.Show(
                "LimitIO couldn't reach its background service. Limits will not be enforced until this is " +
                "resolved - try restarting your computer, or reinstalling LimitIO if the problem continues.",
                "LimitIO", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        else if (!status.PasswordConfigured)
        {
            var wizard = new FirstRunSetupWizard(_serviceClient);
            wizard.ShowDialog();
        }

        _trayIconManager = new TrayIconManager(_serviceClient);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();
        _serviceClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
