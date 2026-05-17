using System.Windows;

namespace WindowsOptimizer
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            var splash = new SplashWindow
            {
                Owner = mainWindow
            };

            splash.ShowDialog();
            mainWindow.Show();
        }
    }
}
