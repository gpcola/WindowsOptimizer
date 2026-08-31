using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WindowsOptimizer
{
    public partial class MainWindow : Window
    {
        private readonly CleanupSafety cleanupSafety;
        private readonly Optimizer optimizer;
        private readonly NetworkOptimizer networkOptimizer;
        private readonly BenchmarkHelper benchmark;
        private readonly Logger logger;
        private bool isBusy;

        public MainWindow()
        {
            InitializeComponent();

            logger = new Logger(LogToUi);
            cleanupSafety = new CleanupSafety(logger.Log);
            optimizer = new Optimizer(logger.Log, cleanupSafety);
            networkOptimizer = new NetworkOptimizer(logger.Log);
            benchmark = new BenchmarkHelper();

            LoadCleanupExclusions();
            RefreshDiskInfo();
            RefreshNetworkStatus();
            RefreshMediaStreamingStatus();
            UpdateOperationProgress(0, 1, "Ready", "Protected application data will never be selected for cleanup.", false);
            LogToUi("Ready. Safe housekeeping mode is active.");
        }

        private void LogToUi(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText(message + Environment.NewLine);
                txtLog.ScrollToEnd();
            });
        }

        private void RefreshDiskInfo()
        {
            try
            {
                double free = DiskHelper.GetFreeSpaceGB("C");
                double total = DiskHelper.GetTotalSpaceGB("C");
                txtSpaceInfo.Text = $"Disk C: {free:N1} GB free / {total:N1} GB total";
            }
            catch (Exception ex)
            {
                txtSpaceInfo.Text = "Disk info unavailable";
                logger.Log("ERR: " + ex.Message);
            }
        }

        private void RefreshNetworkStatus()
        {
            try
            {
                string status = networkOptimizer.GetStatus();
                txtNetworkStatus.Text = status;
                SetQuickNetworkStatus(status);
            }
            catch (Exception ex)
            {
                txtNetworkStatus.Text = "Network status could not be determined.";
                SetQuickNetworkStatus(txtNetworkStatus.Text);
                logger.Log("WARNING: Network status: " + ex.Message);
            }
        }

        private async void RunSelected_Click(object sender, RoutedEventArgs e)
        {
            var actions = new List<(string Name, Func<bool> Execute)>();

            if (chkTemp.IsChecked == true)
                actions.Add(("Clean stale temp files", optimizer.CleanTempFiles));

            if (chkRecycle.IsChecked == true)
                actions.Add(("Empty Recycle Bin", optimizer.EmptyRecycleBin));

            if (chkUpdates.IsChecked == true)
                actions.Add(("Clear Windows Update cache", optimizer.ClearUpdateCache));

            if (chkWinSxS.IsChecked == true)
                actions.Add(("Clean Windows component store", optimizer.CleanComponentStore));

            if (chkBloat.IsChecked == true)
            {
                var confirm = MessageBox.Show(
                    "This optional action uninstalls only a small, explicit allowlist of consumer apps. " +
                    "It does not remove Windows features or system components.\n\n" +
                    "Continue with optional app removal?",
                    "Confirm optional app removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                    actions.Add(("Remove optional consumer apps", optimizer.RemoveBloatApps));
                else
                    logger.Log("Optional consumer app removal was skipped by the user.");
            }

            if (!actions.Any())
            {
                logger.Log("No housekeeping actions selected.");
                return;
            }

            await RunWorkflowAsync(actions, "Run selected");
        }

        private async void RunSafeHousekeeping_Click(object sender, RoutedEventArgs e)
        {
            var actions = new List<(string Name, Func<bool> Execute)>
            {
                ("Clean stale temp files", optimizer.CleanTempFiles),
                ("Empty Recycle Bin", optimizer.EmptyRecycleBin),
                ("Clear Windows Update cache", optimizer.ClearUpdateCache),
                ("Clean Windows component store", optimizer.CleanComponentStore)
            };

            await RunWorkflowAsync(actions, "Safe housekeeping");
        }

        private async void OptimizeSystemDrive_Click(object sender, RoutedEventArgs e)
        {
            var actions = new List<(string Name, Func<bool> Execute)>
            {
                ("Optimize system drive", optimizer.OptimizeSystemDrive)
            };

            await RunWorkflowAsync(actions, "System drive optimization");
        }

        private async void OptimizeNetwork_Click(object sender, RoutedEventArgs e)
        {
            await RunNetworkOptimizationAsync();
        }

        private async Task RunNetworkOptimizationAsync()
        {
            if (isBusy)
            {
                logger.Log("Another operation is already running.");
                SetQuickNetworkStatus("Another operation is already running.");
                return;
            }

            isBusy = true;
            SetBusyState(true);
            SetQuickNetworkButtonEnabled(false);
            UpdateOperationProgress(
                0,
                0,
                "Optimizing Ethernet & Wi-Fi",
                "Applying supported Windows settings to physical adapters only. VPN settings are preserved.",
                true);

            try
            {
                txtNetworkStatus.Text = "Optimizing the physical Ethernet/Wi-Fi adapters without altering PIA VPN settings...";
                SetQuickNetworkStatus(txtNetworkStatus.Text);

                bool success = await Task.Run(networkOptimizer.Optimize);
                RefreshNetworkStatus();

                if (success)
                {
                    logger.Log("Network optimization completed. A reconnect or restart may be needed for every adapter power-management change to become active.");
                    UpdateOperationProgress(1, 1, "Network optimization complete", "Supported physical-adapter settings were applied successfully.", false);
                }
                else
                {
                    logger.Log("Network optimization completed with warnings or unsupported adapter settings. Unsupported items were left unchanged.");
                    UpdateOperationProgress(1, 1, "Network optimization completed with warnings", "Unsupported settings were left unchanged. Review the activity log for details.", false);
                }
            }
            catch (Exception ex)
            {
                txtNetworkStatus.Text = "Network optimization stopped safely after an error.";
                SetQuickNetworkStatus(txtNetworkStatus.Text);
                UpdateOperationProgress(0, 1, "Network optimization stopped", "No further network changes were attempted. Review the activity log.", false);
                logger.Log("ERR: Network optimization: " + ex.Message);
            }
            finally
            {
                isBusy = false;
                SetBusyState(false);
                SetQuickNetworkButtonEnabled(true);
            }
        }

        private void RefreshNetwork_Click(object sender, RoutedEventArgs e)
        {
            RefreshNetworkStatus();
            logger.Log("Network status refreshed.");
        }

        private void OpenPia_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Private Internet Access",
                    "pia-client.exe");

                if (!File.Exists(path))
                {
                    logger.Log("Private Internet Access was not found at its standard install location.");
                    return;
                }

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                logger.Log("Opened Private Internet Access. For local streaming, verify Settings > Network > Allow LAN Traffic is enabled.");
            }
            catch (Exception ex)
            {
                logger.Log("ERR: Could not open Private Internet Access: " + ex.Message);
            }
        }

        private async Task RunWorkflowAsync(
            List<(string Name, Func<bool> Execute)> actions,
            string mode)
        {
            if (isBusy)
            {
                logger.Log("Another operation is already running.");
                UpdateOperationProgress(0, 1, "Busy", "Wait for the current operation to finish before starting another.", false);
                return;
            }

            isBusy = true;
            SetBusyState(true);
            bool workflowSucceeded = true;

            try
            {
                UpdateOperationProgress(
                    0,
                    0,
                    $"{mode}: preparing",
                    "Capturing a before snapshot and validating the cleanup queue.",
                    true);

                var beforeMetrics = benchmark.CaptureMetrics();
                txtBenchmarkBefore.Text = benchmark.FormatSnapshot(beforeMetrics);
                logger.Log("Automatic pre-run metrics captured.");
                logger.Log($"{mode} started. {actions.Count} action(s) queued.");

                await Task.Run(() =>
                {
                    for (int index = 0; index < actions.Count; index++)
                    {
                        var action = actions[index];
                        UpdateOperationProgress(
                            index,
                            actions.Count,
                            $"Running: {action.Name}",
                            $"Step {index + 1} of {actions.Count}. Protected and in-use items are skipped.",
                            true);

                        logger.Log($"Running: {action.Name}");
                        bool success = action.Execute();
                        workflowSucceeded &= success;

                        logger.Log(success
                            ? $"Completed: {action.Name}"
                            : $"Skipped or completed with warnings: {action.Name}");

                        UpdateOperationProgress(
                            index + 1,
                            actions.Count,
                            success ? $"Completed: {action.Name}" : $"Completed with warnings: {action.Name}",
                            $"Step {index + 1} of {actions.Count} finished.",
                            index + 1 < actions.Count);
                    }
                });

                RefreshDiskInfo();

                UpdateOperationProgress(
                    actions.Count,
                    actions.Count,
                    $"{mode}: finishing",
                    "Capturing the after snapshot and preparing the run summary.",
                    true);

                var afterMetrics = benchmark.CaptureMetrics();
                var impact = benchmark.BuildRunImpact(beforeMetrics, afterMetrics);

                txtBenchmarkAfter.Text = benchmark.FormatSnapshot(afterMetrics);
                txtBenchmarkComparison.Text =
                    benchmark.BuildRunSummary(beforeMetrics, afterMetrics, actions.Count, rebootRecommended: false);

                SetQuickRunImpact(impact.Headline, impact.Detail);

                logger.Log("Automatic post-run metrics captured.");
                logger.Log($"{impact.Headline}. {impact.Detail}");
                logger.Log($"{mode} finished.");

                UpdateOperationProgress(
                    actions.Count,
                    actions.Count,
                    workflowSucceeded
                        ? $"{mode} complete — {impact.Headline}"
                        : $"{mode} complete with warnings — {impact.Headline}",
                    impact.Detail,
                    false);
            }
            catch (Exception ex)
            {
                UpdateOperationProgress(
                    0,
                    1,
                    $"{mode} stopped safely",
                    "An error stopped the workflow. Review the activity log for details.",
                    false);
                logger.Log("ERR: " + ex.Message);
            }
            finally
            {
                SetBusyState(false);
                isBusy = false;
            }
        }

        private void SetBusyState(bool busy)
        {
            Dispatcher.Invoke(() =>
            {
                btnRunSelected.IsEnabled = !busy;
                btnRunSafeHousekeeping.IsEnabled = !busy;
                btnOptimizeDrive.IsEnabled = !busy;
                btnOptimizeNetwork.IsEnabled = !busy;
                btnEnableMediaStreaming.IsEnabled = !busy;
                btnDisableMediaStreaming.IsEnabled = !busy;
                btnSaveExclusions.IsEnabled = !busy;
            });
        }

        private void LoadCleanupExclusions()
        {
            txtBuiltInExclusions.Text = string.Join(
                Environment.NewLine,
                cleanupSafety.BuiltInDisplayPaths);

            txtCustomExclusions.Text = string.Join(
                Environment.NewLine,
                cleanupSafety.LoadCustomExclusions());

            txtExclusionStatus.Text =
                "Built-in protections are always active. Add one custom folder per line if you want additional locations excluded.";
        }

        private void SaveExclusions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string[] paths = txtCustomExclusions.Text
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Trim())
                    .Where(path => path.Length > 0)
                    .ToArray();

                cleanupSafety.SaveCustomExclusions(paths);
                LoadCleanupExclusions();
                txtExclusionStatus.Text =
                    $"Saved {cleanupSafety.LoadCustomExclusions().Count} custom exclusion(s). These paths are now protected from cleanup.";
            }
            catch (Exception ex)
            {
                txtExclusionStatus.Text = "Could not save custom exclusions. Review the activity log.";
                logger.Log("ERR: Could not save cleanup exclusions: " + ex.Message);
            }
        }

        private void UpdateOperationProgress(
            int completed,
            int total,
            string status,
            string detail,
            bool active)
        {
            Dispatcher.Invoke(() =>
            {
                operationProgress.IsIndeterminate = active && total <= 0;

                if (total > 0)
                {
                    operationProgress.IsIndeterminate = false;
                    operationProgress.Minimum = 0;
                    operationProgress.Maximum = total;
                    operationProgress.Value = Math.Clamp(completed, 0, total);
                }
                else if (!active)
                {
                    operationProgress.Value = 0;
                }

                txtProgressStatus.Text = status;
                txtProgressDetail.Text = detail;
                SetQuickProgress(completed, total, status, detail, active);
            });
        }

        private void RefreshDisk_Click(object sender, RoutedEventArgs e)
        {
            RefreshDiskInfo();
            logger.Log("Disk info refreshed.");
        }

        private void BenchmarkBefore_Click(object sender, RoutedEventArgs e)
        {
            txtBenchmarkBefore.Text = benchmark.TakeSnapshot();
            logger.Log("Benchmark BEFORE snapshot taken.");
        }

        private void BenchmarkAfter_Click(object sender, RoutedEventArgs e)
        {
            txtBenchmarkAfter.Text = benchmark.TakeSnapshot();
            logger.Log("Benchmark AFTER snapshot taken.");
        }

        private void CompareBenchmark_Click(object sender, RoutedEventArgs e)
        {
            txtBenchmarkComparison.Text = benchmark.Compare(txtBenchmarkBefore.Text, txtBenchmarkAfter.Text);
            logger.Log("Benchmark comparison updated.");
        }

        private void OpenStartupApps_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsUri("ms-settings:startupapps", "Startup Apps");
        }

        private void OpenStorageSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsUri("ms-settings:storagesense", "Storage Settings");
        }

        private void OpenSettingsUri(string uri, string label)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                logger.Log($"Opened {label}.");
            }
            catch (Exception ex)
            {
                logger.Log($"ERR: Could not open {label}: {ex.Message}");
            }
        }
    }
}
