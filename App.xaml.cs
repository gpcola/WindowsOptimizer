using System;
using System.Windows;
using System.Windows.Threading;

namespace WindowsOptimizer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            EventHandler? contentRendered = null;
            contentRendered = (_, _) =>
            {
                mainWindow.ContentRendered -= contentRendered;
                mainWindow.Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(mainWindow.EnableQuickModeShell));
            };

            mainWindow.ContentRendered += contentRendered;
            mainWindow.Show();
        }
    }
}
