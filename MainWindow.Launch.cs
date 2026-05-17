using System;
using System.Diagnostics;
using System.Windows;

namespace WindowsOptimizer
{
    public partial class MainWindow
    {
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            logger.Log("Windows Optimizer by 1LG Digital loaded.");
            logger.Log("Website: https://www.1lg.com | Email: info@1lg.com");
        }

        private static void OpenExternalLink(string target, Action<string>? log = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                log?.Invoke("Unable to open external link: " + ex.Message);
            }
        }
    }
}
