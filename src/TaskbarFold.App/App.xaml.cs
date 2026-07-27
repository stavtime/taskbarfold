using System.Windows;
using System.Windows.Threading;
using TaskbarFold.App.Services;

namespace TaskbarFold.App;

public partial class App : Application
{
    private readonly CommandLineOptions _options;
    private AppHost? _host;

    public App(CommandLineOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // A resident that dies on the first unexpected exception would take the user's
        // pinned groups with it, silently. Log and keep going instead.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = new AppHost();
        _host.Start(showSettings: !_options.StartHidden);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _host = null;

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception on the UI thread", e.Exception);
        e.Handled = true;
    }
}
