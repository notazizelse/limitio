using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LimitIO.UI.Diagnostics;
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

        // Previously nothing caught exceptions raised outside a normal method call (a bad await in a
        // fire-and-forget event handler, an exception on a background thread) - any single one of these
        // would silently take the whole tray app down with no trace of why. These three handlers are the
        // full set of places .NET can deliver an exception that a plain try/catch around a method body
        // won't see, and each one logs and tries to keep the app alive rather than let Windows report it
        // as "not responding" and kill it.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Global\\LimitIO.UI.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // Already running (e.g. it auto-starts at login and the user also has a Start Menu shortcut) -
            // quietly exit rather than showing a second tray icon.
            Shutdown();
            return;
        }

        try
        {
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
        catch (Exception ex)
        {
            UiLog.Error("Startup failed", ex);
            System.Windows.MessageBox.Show(
                "LimitIO ran into a problem starting up and will close. Check %LocalAppData%\\LimitIO\\logs " +
                "for details, or try reinstalling.",
                "LimitIO", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        UiLog.Error("Unhandled exception on the UI thread", e.Exception);
        // Mark handled so a single bad event handler (a menu click, a timer tick) can't take the whole
        // tray app down - the alternative (letting it propagate) is exactly the "stops responding, then
        // closes" failure mode this exists to prevent.
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            UiLog.Error("Unhandled exception on a background thread (fatal - process will terminate)", ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        UiLog.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _trayIconManager?.Dispose();
            _serviceClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _singleInstanceMutex?.Dispose();
        }
        catch (Exception ex)
        {
            UiLog.Error("Error during shutdown", ex);
        }
        base.OnExit(e);
    }
}
