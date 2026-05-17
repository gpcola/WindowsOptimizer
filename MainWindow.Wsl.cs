using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WindowsOptimizer.Models;

namespace WindowsOptimizer
{
    public partial class MainWindow
    {
        private readonly ObservableCollection<WslDistroEntry> wslDistros = new();
        private bool wslTabInitialized;

        private DataGrid dgWslDistros = null!;
        private TextBox txtWslGlobalConfig = null!;
        private TextBlock txtWslStatus = null!;
        private TextBox txtWslMoveTarget = null!;
        private CheckBox chkKeepWslExportBackup = null!;
        private CheckBox chkRenameWslOriginal = null!;
        private TextBox txtWslVhdMaxSize = null!;

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            EnsureWslStorageTab();
        }

        private void EnsureWslStorageTab()
        {
            if (wslTabInitialized) return;

            var tabs = FindVisualChild<TabControl>(this);
            if (tabs == null)
            {
                logger.Log("Unable to add WSL Storage tab because the main TabControl could not be found.");
                return;
            }

            var tab = new TabItem
            {
                Header = "WSL Storage",
                Content = BuildWslStorageContent()
            };

            int helpIndex = -1;
            for (int index = 0; index < tabs.Items.Count; index++)
            {
                if ((tabs.Items[index] as TabItem)?.Header?.ToString() == "Help")
                {
                    helpIndex = index;
                    break;
                }
            }

            if (helpIndex >= 0) tabs.Items.Insert(helpIndex, tab);
            else tabs.Items.Add(tab);

            dgWslDistros.ItemsSource = wslDistros;
            txtWslGlobalConfig.Text = WslHelper.ReadGlobalConfigSummary();
            txtWslStatus.Text = "Click Scan WSL to list installed distributions, VHD locations, cache estimates, and configuration summaries.";
            wslTabInitialized = true;
        }

        private FrameworkElement BuildWslStorageContent()
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topCard = BuildCard();
            var topPanel = new StackPanel();
            topCard.Child = topPanel;

            topPanel.Children.Add(new TextBlock
            {
                Text = "WSL distro storage",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            topPanel.Children.Add(new TextBlock
            {
                Text = "Review WSL distro locations, physical VHDX size, Linux-reported usage, cache estimates, and safe relocation options. Linux cache folders are estimated or cleaned from inside WSL; they are not moved directly from Windows.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var topButtons = new WrapPanel();
            topButtons.Children.Add(WslButton("Scan WSL", ScanWsl_Click, 105));
            topButtons.Children.Add(WslButton("Refresh Sizes", RefreshWslSizes_Click, 125));
            topButtons.Children.Add(WslButton("Estimate Caches", EstimateWslCaches_Click, 135));
            topButtons.Children.Add(WslButton("Clean Selected Caches", CleanSelectedWslCaches_Click, 170));
            topButtons.Children.Add(WslButton("WSL Shutdown", ShutdownWsl_Click, 125));
            topButtons.Children.Add(WslButton("Open .wslconfig", OpenWslConfig_Click, 135));
            topButtons.Children.Add(WslButton("Refresh Config", RefreshWslConfig_Click, 125));
            topPanel.Children.Add(topButtons);

            Grid.SetRow(topCard, 0);
            root.Children.Add(topCard);

            dgWslDistros = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Extended,
                Margin = new Thickness(0, 0, 0, 10)
            };

            dgWslDistros.Columns.Add(new DataGridCheckBoxColumn { Header = "Move", Binding = new Binding(nameof(WslDistroEntry.IsSelectedForMove)), Width = 60 });
            dgWslDistros.Columns.Add(TextColumn("Name", nameof(WslDistroEntry.DisplayName), 170));
            dgWslDistros.Columns.Add(TextColumn("State", nameof(WslDistroEntry.State), 90));
            dgWslDistros.Columns.Add(TextColumn("WSL", nameof(WslDistroEntry.VersionDisplay), 60));
            dgWslDistros.Columns.Add(TextColumn("Install Path", nameof(WslDistroEntry.InstallPath), 260));
            dgWslDistros.Columns.Add(TextColumn("VHDX Path", nameof(WslDistroEntry.VhdxPath), 320));
            dgWslDistros.Columns.Add(TextColumn("VHDX Size", nameof(WslDistroEntry.VhdxSizeDisplay), 105));
            dgWslDistros.Columns.Add(TextColumn("Linux Used", nameof(WslDistroEntry.LinuxUsedDisplay), 110));
            dgWslDistros.Columns.Add(TextColumn("Cache Estimate", nameof(WslDistroEntry.CacheEstimateDisplay), 125));
            dgWslDistros.Columns.Add(TextColumn("Default User", nameof(WslDistroEntry.DefaultUser), 110));
            dgWslDistros.Columns.Add(TextColumn("Last Updated", nameof(WslDistroEntry.LastSizeRefreshDisplay), 155));
            dgWslDistros.Columns.Add(TextColumn("/etc/wsl.conf", nameof(WslDistroEntry.ConfigSummary), 260));

            Grid.SetRow(dgWslDistros, 1);
            root.Children.Add(dgWslDistros);

            var bottomCard = BuildCard();
            var bottomPanel = new StackPanel();
            bottomCard.Child = bottomPanel;

            bottomPanel.Children.Add(new TextBlock
            {
                Text = "Safe move, config, and VHD size options",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var movePanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            movePanel.Children.Add(new TextBlock { Text = "Target folder:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
            txtWslMoveTarget = new TextBox { Width = 360, Text = "D:\\WSL", Margin = new Thickness(0, 0, 8, 8), ToolTip = "Example: D:\\WSL" };
            movePanel.Children.Add(txtWslMoveTarget);
            movePanel.Children.Add(WslButton("Browse Target", BrowseWslMoveTarget_Click, 130));
            movePanel.Children.Add(WslButton("Open Location", OpenSelectedWslLocation_Click, 130));
            movePanel.Children.Add(WslButton("Copy Path", CopySelectedWslPath_Click, 105));
            movePanel.Children.Add(WslButton("Move Selected Safely", MoveSelectedWslDistros_Click, 170));
            bottomPanel.Children.Add(movePanel);

            var optionsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            chkRenameWslOriginal = new CheckBox
            {
                Content = "Rename original during move and keep it as a recovery copy",
                IsChecked = true,
                Margin = new Thickness(0, 0, 20, 8)
            };
            chkKeepWslExportBackup = new CheckBox
            {
                Content = "Keep export backup after import",
                IsChecked = true,
                Margin = new Thickness(0, 0, 20, 8)
            };
            optionsPanel.Children.Add(chkRenameWslOriginal);
            optionsPanel.Children.Add(chkKeepWslExportBackup);
            bottomPanel.Children.Add(optionsPanel);

            var resizePanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            resizePanel.Children.Add(new TextBlock { Text = "VHD max size:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
            txtWslVhdMaxSize = new TextBox { Width = 100, Text = "256GB", Margin = new Thickness(0, 0, 8, 8) };
            resizePanel.Children.Add(txtWslVhdMaxSize);
            resizePanel.Children.Add(WslButton("Update VHD Max Size", ResizeSelectedWslVhd_Click, 170));
            bottomPanel.Children.Add(resizePanel);

            bottomPanel.Children.Add(new TextBlock
            {
                Text = "Global .wslconfig summary",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            txtWslGlobalConfig = new TextBox
            {
                Height = 115,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 8)
            };
            bottomPanel.Children.Add(txtWslGlobalConfig);

            txtWslStatus = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold
            };
            bottomPanel.Children.Add(txtWslStatus);

            Grid.SetRow(bottomCard, 2);
            root.Children.Add(bottomCard);

            return root;
        }

        private static Border BuildCard()
        {
            return new Border
            {
                Background = SystemColors.ControlBrush,
                BorderBrush = SystemColors.ActiveBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private static Button WslButton(string content, RoutedEventHandler handler, double width)
        {
            var button = new Button
            {
                Content = content,
                Width = width,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 8)
            };
            button.Click += handler;
            return button;
        }

        private static DataGridTextColumn TextColumn(string header, string binding, double width)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding),
                Width = width
            };
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                {
                    return typed;
                }

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
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

        private IEnumerable<WslDistroEntry> GetSelectedWslDistros()
        {
            if (dgWslDistros.SelectedItems.Count > 0)
            {
                return dgWslDistros.SelectedItems.OfType<WslDistroEntry>();
            }

            return wslDistros.Where(d => d.IsSelectedForMove);
        }
    }
}
