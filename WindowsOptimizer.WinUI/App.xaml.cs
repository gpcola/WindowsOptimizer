using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
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
            string logPath = WriteStartupFailure(ex);
            ShowStartupFailureWindow(ex, logPath);
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        string logPath = WriteStartupFailure(e.Exception);
        e.Handled = true;
        ShowStartupFailureWindow(e.Exception, logPath);
    }

    private static string WriteStartupFailure(Exception exception)
    {
        try
        {
            string logDir = GetLogDirectory();
            Directory.CreateDirectory(logDir);

            string logPath = Path.Combine(logDir, "startup-error.log");
            var builder = new StringBuilder();
            builder.AppendLine("Windows Optimizer startup failure");
            builder.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine(exception.ToString());
            builder.AppendLine();

            File.AppendAllText(logPath, builder.ToString());
            return logPath;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void ShowStartupFailureWindow(Exception exception, string logPath)
    {
        try
        {
            var text = new TextBlock
            {
                Text = "Windows Optimizer could not start.\n\n" +
                       exception.GetType().Name + ": " + exception.Message + "\n\n" +
                       (string.IsNullOrWhiteSpace(logPath) ? "Startup log could not be written." : "Startup log:\n" + logPath),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24)
            };

            var openLogButton = new Button
            {
                Content = "Open startup log",
                Margin = new Thickness(24, 0, 8, 24)
            };
            openLogButton.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(logPath) && File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
                }
            };

            var closeButton = new Button
            {
                Content = "Close",
                Margin = new Thickness(0, 0, 24, 24)
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(openLogButton);
            buttons.Children.Add(closeButton);

            var panel = new StackPanel();
            panel.Children.Add(text);
            panel.Children.Add(buttons);

            var errorWindow = new Window
            {
                Title = "Windows Optimizer startup error",
                Content = panel
            };
            closeButton.Click += (_, _) => errorWindow.Close();
            errorWindow.Activate();
        }
        catch
        {
            // Last-resort fallback: avoid crashing while attempting to show the crash details.
        }
    }

    private static string GetLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "1LG Digital",
            "Windows Optimizer");
    }
}
