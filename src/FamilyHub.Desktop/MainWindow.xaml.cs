using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace FamilyHub.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitWebAsync();
    }

    private async Task InitWebAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(App.DataDir, "WebView2"));
            await Web.EnsureCoreWebView2Async(env);
            Web.CoreWebView2.Settings.AreDevToolsEnabled = Debugger.IsAttached;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.NavigationCompleted += (_, _) => Status.Visibility = Visibility.Collapsed;
            // Links to other sites open in the normal browser, not inside the app.
            Web.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
            };
            Web.Source = new Uri(App.LocalUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Status.Text = "Family Hub needs the Microsoft Edge WebView2 Runtime, which is missing on this PC.\n\n" +
                          "Install it from Microsoft (search \"WebView2 Runtime\"), then open Family Hub again.\n\n" +
                          $"In the meantime you can use Family Hub in your browser at {App.LocalUrl}";
        }
    }
}
