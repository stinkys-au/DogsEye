using System.IO;
using System.Windows;

namespace DogsEye.App;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!e.Args.Contains("--smoke"))
        {
            instance = new Mutex(true, "Local\\DogsEye.Desktop", out bool created);
            if (!created)
            {
                MessageBox.Show("DogsEye is already running. Use its existing window.", "DogsEye", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }
        DispatcherUnhandledException += (_, args) =>
        {
            if (MainWindow is MainWindow window) window.StopTracking();
            MessageBox.Show(args.Exception.Message, "DogsEye stopped", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            args.Handled = true;
        };
        // Smoke mode reads Tobii but cannot enable mouse output or respond to global keys.
        var main = new MainWindow(e.Args.Contains("--smoke"));
        MainWindow = main;
        main.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
