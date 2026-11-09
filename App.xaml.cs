using System.Windows;
using System.Windows.Threading;
using QuickOrbit.Services;

namespace QuickOrbit;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private MainController? _controller;
    private DispatcherTimer? _showTimer;
    private bool _dispatcherErrorShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        AppPaths.EnsureDirectories();

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            Shutdown();
            return;
        }

        _controller = new MainController();
        _singleInstance.SecondInstance += () => Dispatcher.BeginInvoke(new Action(() => _controller?.ToggleOverlay()));
        _controller.Start();

        var openSettings = e.Args.Any(argument =>
            string.Equals(argument, "--settings", StringComparison.OrdinalIgnoreCase));

        if (openSettings)
        {
            Dispatcher.BeginInvoke(new Action(() => _controller.ShowSettings()),
                DispatcherPriority.ApplicationIdle);
        }
        else
        {
            // A manual launch should be visible; the short delay lets Explorer finish
            // transferring foreground focus so Deactivated does not hide it at once.
            _showTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _showTimer.Tick += (_, _) =>
            {
                _showTimer?.Stop();
                _controller?.ShowOverlay(sticky: true);
            };
            _showTimer.Start();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_dispatcherErrorShown)
        {
            return;
        }

        _dispatcherErrorShown = true;
        try
        {
            MessageBox.Show("Произошла непредвиденная ошибка:\n\n" + e.Exception.Message,
                "Quick Orbit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // The application is already shutting down.
        }

        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is Exception exception)
            {
                MessageBox.Show("Критическая ошибка Quick Orbit:\n\n" + exception.Message,
                    "Quick Orbit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch
        {
            // Nothing useful can be done at this point.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Shutdown();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
