using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Text;

namespace WindowsOptimizer.WinUI;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        UnhandledException += App_UnhandledException;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            window = new MainWindow();
            window.Activate();
        }
        catch (Exception ex)
        {
            WriteStartupFailure(ex);
            throw;
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteStartupFailure(e.Exception);
    }

    private static void WriteStartupFailure(Exception exception)
    {
        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "1LG Digital",
                "Windows Optimizer");

            Directory.CreateDirectory(logDir);

            string logPath = Path.Combine(logDir, "startup-error.log");
            var builder = new StringBuilder();
            builder.AppendLine("Windows Optimizer startup failure");
            builder.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine(exception.ToString());
            builder.AppendLine();

            File.AppendAllText(logPath, builder.ToString());
        }
        catch
        {
            // Avoid secondary failures while logging a startup problem.
        }
    }
}
