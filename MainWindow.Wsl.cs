using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WindowsOptimizer.Models;

namespace WindowsOptimizer
{
    public partial class MainWindow
    {
        private readonly ObservableCollection<WslDistroEntry> wslDistros = new();
        private bool wslTabInitialized;

        private void WslStorageTab_Loaded(object sender, RoutedEventArgs e)
        {
            if (wslTabInitialized) return;

            wslTabInitialized = true;
            dgWslDistros.ItemsSource = wslDistros;
            txtWslGlobalConfig.Text = WslHelper.ReadGlobalConfigSummary();
            txtWslStatus.Text = "Click Scan WSL to list installed distributions and storage locations.";
        }

        private async void ScanWsl_Click(object sender, RoutedEventArgs e)
        {
            await ScanWslDistrosAsync();
        }

        private async Task ScanWslDistrosAsync()
        {
            try
            {
                txtWslStatus.Text = "Scanning WSL distros...";
                logger.Log("Scanning WSL distros.");

                var detected = await WslHelper.GetDistrosAsync();
                wslDistros.Clear();

                foreach (var distro in detected)
                {
                    wslDistros.Add(distro);
                }

                txtWslGlobalConfig.Text = WslHelper.ReadGlobalConfigSummary();
                txtWslStatus.Text = $"Found {wslDistros.Count} WSL distro(s).";
                logger.Log($"WSL scan complete. {wslDistros.Count} distro(s) found.");
            }
            catch (Exception ex)
            {
                txtWslStatus.Text = "WSL scan failed: " + ex.Message;
                logger.Log("ERR: WSL scan failed: " + ex.Message);
            }
        }

        private async void RefreshWslSizes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!wslDistros.Any())
                {
                    await ScanWslDistrosAsync();
                    return;
                }

                txtWslStatus.Text = "Refreshing WSL file sizes...";
                logger.Log("Refreshing WSL file sizes.");

                foreach (var distro in wslDistros)
                {
                    await WslHelper.RefreshReportedSizesAsync(distro);
                }

                txtWslStatus.Text = "WSL file sizes updated.";
                logger.Log("WSL file sizes updated.");
            }
            catch (Exception ex)
            {
                txtWslStatus.Text = "WSL size refresh failed: " + ex.Message;
                logger.Log("ERR: WSL size refresh failed: " + ex.Message);
            }
        }

        private async void EstimateWslCaches_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedWslDistros().ToList();
            if (!selected.Any())
            {
                txtWslStatus.Text = "Select one or more WSL distros first.";
                return;
            }

            try
            {
                txtWslStatus.Text = "Estimating Linux-side cache usage...";

                foreach (var distro in selected)
                {
                    logger.Log($"Estimating WSL cache usage for {distro.Name}.");
                    await WslHelper.EstimateCacheSizeAsync(distro);
                }

                txtWslStatus.Text = "Cache estimates updated. These are Linux-side paths and should not be moved directly from Windows.";
            }
            catch (Exception ex)
            {
                txtWslStatus.Text = "Cache estimate failed: " + ex.Message;
                logger.Log("ERR: WSL cache estimate failed: " + ex.Message);
            }
        }

        private async void CleanSelectedWslCaches_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedWslDistros().ToList();
            if (!selected.Any())
            {
                txtWslStatus.Text = "Select one or more WSL distros first.";
                return;
            }

            var confirm = MessageBox.Show(
                "This will run safe cache cleanup commands inside the selected WSL distro(s). It will not move Linux filesystem folders from Windows. Continue?",
                "Clean WSL caches",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                foreach (var distro in selected)
                {
                    txtWslStatus.Text = $"Cleaning common caches inside {distro.Name}...";
                    logger.Log($"Cleaning common WSL caches for {distro.Name}.");
                    await WslHelper.CleanCommonCachesAsync(distro);
                    await WslHelper.EstimateCacheSizeAsync(distro);
                    await WslHelper.RefreshReportedSizesAsync(distro);
                }

                txtWslStatus.Text = "Selected WSL cache cleanup completed.";
                logger.Log("Selected WSL cache cleanup completed.");
            }
            catch (Exception ex)
            {
                txtWslStatus.Text = "WSL cache cleanup failed: " + ex.Message;
                logger.Log("ERR: WSL cache cleanup failed: " + ex.Message);
            }
        }

        private async void MoveSelectedWslDistros_Click(object sender, RoutedEventArgs e)
        {
            var selected = wslDistros.Where(d => d.IsSelectedForMove).ToList();
            if (!selected.Any())
            {
                txtWslStatus.Text = "Tick the Move box for at least one distro first.";
                return;
            }

            string target = txtWslMoveTarget.Text.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                txtWslStatus.Text = "Enter a target folder such as D:\\WSL before moving distros.";
                return;
            }

            var confirm = MessageBox.Show(
                "This uses WSL export/import rather than raw file copying. The safer default renames the original distro and leaves it in place as a recovery copy until you remove it manually. Continue?",
                "Move selected WSL distros safely",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                foreach (var distro in selected)
                {
                    var options = new WslMoveOptions
                    {
                        TargetRoot = target,
                        KeepExportBackup = chkKeepWslExportBackup.IsChecked == true,
                        RenameExistingDistroDuringMove = chkRenameWslOriginal.IsChecked == true
                    };

                    await WslHelper.MoveDistroByExportImportAsync(
                        distro,
                        options,
                        message => Dispatcher.Invoke(() =>
                        {
                            txtWslStatus.Text = message;
                            logger.Log(message);
                        }));
                }

                await ScanWslDistrosAsync();
                txtWslStatus.Text = "Selected WSL move workflow completed.";
            }
            catch (Exception ex)
            {
                txtWslStatus.Text = "WSL move failed: " + ex.Message;
                logger.Log("ERR: WSL move failed: " + ex.Message);
            }
        }

        private async void ResizeSelectedWslVhd_Click(object sender, RoutedEventArgs e)
        {
            if (dgWslDistros.SelectedItem is not WslDistroEntry distro)
            {
                txtWslStatus.Text = "Select one WSL 2 distro first.";
                return;
            }

            string size = txtWslVhdMaxSize.Text.Trim();
            if (string.IsNullOrWhiteSpace(size))
            {
                txtWslStatus.Text = "Enter a size such as 256GB, 512GB, or 1TB.";
                return;
            }

            var confirm = MessageBox.Show(
                $"Update the maximum VHD size for {distro.Name} to {size}? WSL will be shut down first.",
                "Update WSL VHD max size",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                txtWslStatus.Text = $"Updating VHD max size for {distro.Name}...";
                logger.Log($"Updating WSL VHD max size for {distro.Name} to {size}.");
                await WslHelper.ResizeDistroVhdAsync(distro, size);
                await WslHelper.RefreshReportedSizesAsync(distro);
                txtWslStatus.Text = $"VHD max size update completed for {distro.Name}.";
            }
            catch (Exception ex)
            {
                txtWslStatus.Text = "WSL VHD resize failed: " + ex.Message;
                logger.Log("ERR: WSL VHD resize failed: " + ex.Message);
            }
        }

        private async void ShutdownWsl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                txtWslStatus.Text = "Shutting down WSL...";
                await WslHelper.ShutdownAsync();
                txtWslStatus.Text = "WSL has been shut down.";
                logger.Log("WSL shutdown completed.");
            }
            catch (Exception ex)
            {
                txtWslStatus.Text = "WSL shutdown failed: " + ex.Message;
                logger.Log("ERR: WSL shutdown failed: " + ex.Message);
            }
        }

        private void OpenWslConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = WslHelper.GetGlobalConfigPath();
                if (!File.Exists(path))
                {
                    File.WriteAllText(path,
                        "# Global WSL 2 settings. Changes normally require wsl --shutdown.\r\n" +
                        "[wsl2]\r\n" +
                        "# memory=4GB\r\n" +
                        "# processors=2\r\n" +
                        "# swap=8GB\r\n" +
                        "# defaultVhdSize=256GB\r\n" +
                        "# networkingMode=mirrored\r\n" +
                        "# dnsTunneling=true\r\n" +
                        "# autoProxy=true\r\n\r\n" +
                        "[experimental]\r\n" +
                        "# sparseVhd=true\r\n" +
                        "# autoMemoryReclaim=dropCache\r\n");
                }

                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
                txtWslGlobalConfig.Text = WslHelper.ReadGlobalConfigSummary();
            }
            catch (Exception ex)
            {
                txtWslStatus.Text = "Unable to open .wslconfig: " + ex.Message;
                logger.Log("ERR: Unable to open .wslconfig: " + ex.Message);
            }
        }

        private void RefreshWslConfig_Click(object sender, RoutedEventArgs e)
        {
            txtWslGlobalConfig.Text = WslHelper.ReadGlobalConfigSummary();
            logger.Log("WSL config summary refreshed.");
        }

        private void BrowseWslMoveTarget_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Choose WSL target folder",
                    Multiselect = false
                };

                if (dialog.ShowDialog(this) == true)
                {
                    txtWslMoveTarget.Text = dialog.FolderName;
                }
            }
            catch (Exception ex)
            {
                txtWslStatus.Text = "Unable to browse for folder: " + ex.Message;
                logger.Log("ERR: Unable to browse for WSL target folder: " + ex.Message);
            }
        }

        private void OpenSelectedWslLocation_Click(object sender, RoutedEventArgs e)
        {
            if (dgWslDistros.SelectedItem is not WslDistroEntry distro)
            {
                txtWslStatus.Text = "Select a WSL distro first.";
                return;
            }

            string path = !string.IsNullOrWhiteSpace(distro.InstallPath) ? distro.InstallPath : Path.GetDirectoryName(distro.VhdxPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                txtWslStatus.Text = "The selected distro location is not available.";
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }

        private void CopySelectedWslPath_Click(object sender, RoutedEventArgs e)
        {
            if (dgWslDistros.SelectedItem is not WslDistroEntry distro)
            {
                txtWslStatus.Text = "Select a WSL distro first.";
                return;
            }

            Clipboard.SetText(!string.IsNullOrWhiteSpace(distro.VhdxPath) ? distro.VhdxPath : distro.InstallPath);
            logger.Log("Copied selected WSL path to clipboard.");
        }

        private System.Collections.Generic.IEnumerable<WslDistroEntry> GetSelectedWslDistros()
        {
            if (dgWslDistros.SelectedItems.Count > 0)
            {
                return dgWslDistros.SelectedItems.OfType<WslDistroEntry>();
            }

            return wslDistros.Where(d => d.IsSelectedForMove);
        }
    }
}
