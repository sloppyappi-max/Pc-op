using System.IO;
using System.Windows;
using FamilyHub.Server;
using Microsoft.AspNetCore.Builder;

namespace FamilyHub.Desktop;

public partial class App : Application
{
    private WebApplication? _server;
    private Mutex? _singleInstance;

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FamilyHub");

    public static string LocalUrl => $"http://localhost:{HubServer.DefaultPort}/";

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Only one copy may run, since both would want the same port and data file.
        _singleInstance = new Mutex(true, "FamilyHub.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Family Hub is already running.", "Family Hub");
            Shutdown();
            return;
        }

        try
        {
            _server = HubServer.Build(DataDir);
            await _server.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Family Hub couldn't start its local server on port {HubServer.DefaultPort}.\n\n{ex.Message}",
                "Family Hub", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_server is not null)
        {
            await _server.StopAsync();
            await _server.DisposeAsync();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
