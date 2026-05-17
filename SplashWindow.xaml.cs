using System;
using System.Diagnostics;
using System.Windows;

namespace WindowsOptimizer
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        private void VisitWebsite_Click(object sender, RoutedEventArgs e)
        {
            OpenExternal("https://www.1lg.com");
        }

        private void Email_Click(object sender, RoutedEventArgs e)
        {
            OpenExternal("mailto:info@1lg.com?subject=Windows%20Optimizer%20enquiry");
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static void OpenExternal(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch
            {
                // Ignore shell launch failures. The visible contact details remain on screen.
            }
        }
    }
}
