using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WindowsOptimizer.Models;

namespace WindowsOptimizer
{
    public partial class MainWindow : Window
    {
        private readonly Optimizer optimizer;
        private readonly RestoreManager restoreManager;
        private readonly BenchmarkHelper benchmark;
        private readonly SnapshotManager snapshotManager;
        private readonly Logger logger;
        private readonly StorageAdvisor storageAdvisor;
        private readonly ObservableCollection<UserFolderEntry> userFolders = new();
        private readonly ObservableCollection<PathEntry> vsPaths = new();
        private readonly ObservableCollection<StorageCandidate> candidates = new();
        private bool isBusy;
        private CancellationTokenSource? candidateScanCancellation;
        private string? lastExportedScriptPath;

        public MainWindow()
        {
            InitializeComponent();

            logger = new Logger(LogToUi);
            snapshotManager = new SnapshotManager(logger.Log);
            optimizer = new Optimizer(logger.Log);
            restoreManager = new RestoreManager(logger.Log, snapshotManager);
            benchmark = new BenchmarkHelper();
            storageAdvisor = new StorageAdvisor(logger.Log);

            dgUserFolders.ItemsSource = userFolders;
            dgVsPaths.ItemsSource = vsPaths;
            dgCandidates.ItemsSource = candidates;

            PopulatePagefileDriveChoices();
            RefreshDiskInfo();
            RefreshUserFolders();
            RefreshVsPaths();
            RefreshOneDrive();
            RefreshCopilotSuggestions();
            txtCandidateScanRoot.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            txtCandidateMoveTarget.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            LogToUi("Ready.");
        }

        private void LogToUi(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText(message + Environment.NewLine);
                txtLog.ScrollToEnd();
            });
        }

        private string GetSafeReclaimScriptExportDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsOptimizer", "Scripts");
        }

        private string EnsureSafeReclaimScriptExported()
        {
            string exportDir = GetSafeReclaimScriptExportDirectory();
            lastExportedScriptPath = SafeReclaimScriptBuilder.Export(exportDir);
            return lastExportedScriptPath;
        }

        private void RefreshDiskInfo()
        {
            try
            {
                double free = DiskHelper.GetFreeSpaceGB("C");
                double total = DiskHelper.GetTotalSpaceGB("C");
                txtSpaceInfo.Text = $"Disk C: {free} GB free / {total} GB total";
            }
            catch (Exception ex)
            {
                txtSpaceInfo.Text = "Disk info unavailable";
                logger.Log("ERR: " + ex.Message);
            }
        }

        private void PopulatePagefileDriveChoices()
        {
            var drives = DiskHelper.GetFixedDriveLetters().ToList();
            cmbPagefileDrive.ItemsSource = drives;
            if (drives.Contains("D")) cmbPagefileDrive.SelectedItem = "D";
            else if (drives.Any(d => d != "C")) cmbPagefileDrive.SelectedItem = drives.First(d => d != "C");
            else if (drives.Any()) cmbPagefileDrive.SelectedItem = drives.First();
        }

        private async void RunSelected_Click(object sender, RoutedEventArgs e)
        {
            PagefileOptions pagefileOptions = GetPagefileOptionsFromUi();
            bool shouldCreateSnapshot = chkCreateSnapshot.IsChecked == true;
            var actions = new List<(string Name, Func<bool> Execute)>();
            if (chkIndexing.IsChecked == true) actions.Add(("Disable indexing", () => optimizer.DisableIndexing()));
            if (chkFeatures.IsChecked == true) actions.Add(("Remove optional features", () => optimizer.RemoveOptionalFeatures()));
            if (chkBloat.IsChecked == true) actions.Add(("Remove bloat apps", () => optimizer.RemoveBloatApps()));
            if (chkWinSxS.IsChecked == true) actions.Add(("Clean WinSxS", () => optimizer.CleanWinSxS()));
            if (chkUpdates.IsChecked == true) actions.Add(("Clear update cache", () => optimizer.ClearUpdateCache()));
            if (chkHibernate.IsChecked == true) actions.Add(("Disable hibernation", () => optimizer.DisableHibernation()));
            if (chkTemp.IsChecked == true) actions.Add(("Clean temp files", () => optimizer.CleanTempFiles()));
            if (chkRestore.IsChecked == true) actions.Add(("Delete restore points", () => optimizer.DeleteRestorePoints()));
            if (chkPagefile.IsChecked == true) actions.Add(("Move pagefile", () => optimizer.MovePagefile(pagefileOptions)));
            if (chkServices.IsChecked == true) actions.Add(("Disable services", () => optimizer.DisableServices()));
            if (chkBackground.IsChecked == true) actions.Add(("Disable background apps", () => optimizer.DisableBackgroundApps()));
            if (!actions.Any())
            {
                logger.Log("No optimizations selected.");
                return;
            }
            await RunWorkflowAsync(actions, "Run selected", shouldCreateSnapshot);
        }

        private async void RunAll_Click(object sender, RoutedEventArgs e)
        {
            PagefileOptions pagefileOptions = GetPagefileOptionsFromUi();
            bool shouldCreateSnapshot = chkCreateSnapshot.IsChecked == true;
            var actions = new List<(string Name, Func<bool> Execute)>
            {
                ("Disable indexing", () => optimizer.DisableIndexing()),
                ("Remove optional features", () => optimizer.RemoveOptionalFeatures()),
                ("Remove bloat apps", () => optimizer.RemoveBloatApps()),
                ("Clean WinSxS", () => optimizer.CleanWinSxS()),
                ("Clear update cache", () => optimizer.ClearUpdateCache()),
                ("Disable hibernation", () => optimizer.DisableHibernation()),
                ("Clean temp files", () => optimizer.CleanTempFiles()),
                ("Delete restore points", () => optimizer.DeleteRestorePoints()),
                ("Move pagefile", () => optimizer.MovePagefile(pagefileOptions)),
                ("Disable services", () => optimizer.DisableServices()),
                ("Disable background apps", () => optimizer.DisableBackgroundApps())
            };
            await RunWorkflowAsync(actions, "Run all", shouldCreateSnapshot);
        }

        private async Task RunWorkflowAsync(List<(string Name, Func<bool> Execute)> actions, string mode, bool shouldCreateSnapshot)
        {
            if (isBusy)
            {
                logger.Log("Another operation is already running.");
                return;
            }

            isBusy = true;
            SetBusyState(true);
            try
            {
                var beforeMetrics = benchmark.CaptureMetrics();
                txtBenchmarkBefore.Text = benchmark.FormatSnapshot(beforeMetrics);
                logger.Log("Automatic pre-run metrics captured.");
                logger.Log($"{mode} started. {actions.Count} action(s) queued.");
                if (shouldCreateSnapshot) snapshotManager.CreateSnapshot("Automatic pre-run snapshot");

                int successCount = 0;
                int warningCount = 0;

                await Task.Run(() =>
                {
                    foreach (var action in actions)
                    {
                        logger.Log($"Running: {action.Name}");
                        bool success = action.Execute();
                        if (success)
                        {
                            successCount++;
                            logger.Log($"Completed: {action.Name}");
                        }
                        else
                        {
                            warningCount++;
                            logger.Log($"Completed with warnings: {action.Name}");
                        }
                    }
                });

                RefreshDiskInfo();
                var afterMetrics = benchmark.CaptureMetrics();
                bool rebootRecommended = ActionsSuggestReboot(actions.Select(a => a.Name));
                txtBenchmarkAfter.Text = benchmark.FormatSnapshot(afterMetrics);
                txtBenchmarkComparison.Text = benchmark.BuildRunSummary(beforeMetrics, afterMetrics, actions.Count, successCount, warningCount, rebootRecommended);
                logger.Log("Automatic post-run metrics captured.");
                logger.Log("Run summary updated.");
                if (rebootRecommended) logger.Log("Reboot recommended to fully realise some applied changes.");
                RefreshUserFolders();
                RefreshCopilotSuggestions();
                logger.Log($"{mode} finished.");
            }
            catch (Exception ex)
            {
                logger.Log("ERR: " + ex.Message);
            }
            finally
            {
                SetBusyState(false);
                isBusy = false;
            }
        }

        private PagefileOptions GetPagefileOptionsFromUi()
        {
            string drive = (cmbPagefileDrive.SelectedItem?.ToString() ?? "D").Trim().TrimEnd(':');
            int initial = ParsePositiveInt(txtPagefileInitial.Text, 2048);
            int maximum = ParsePositiveInt(txtPagefileMaximum.Text, 4096);
            return new PagefileOptions { DriveLetter = drive, InitialSizeMb = initial, MaximumSizeMb = maximum };
        }

        private static int ParsePositiveInt(string? value, int fallback)
        {
            return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
        }

        private static bool ActionsSuggestReboot(IEnumerable<string> actionNames)
        {
            string[] rebootLikely = { "Remove optional features", "Move pagefile", "Disable hibernation", "Disable services" };
            return actionNames.Any(name => rebootLikely.Contains(name, StringComparer.OrdinalIgnoreCase));
        }

        private void SetBusyState(bool busy)
        {
            Dispatcher.Invoke(() =>
            {
                btnRunSelected.IsEnabled = !busy;
                btnRunAll.IsEnabled = !busy;
            });
        }

        private void RefreshDisk_Click(object sender, RoutedEventArgs e)
        {
            RefreshDiskInfo();
            logger.Log("Disk info refreshed.");
        }

        private void RestoreServices_Click(object sender, RoutedEventArgs e) => restoreManager.RestoreServicesAndBackgroundApps();
        private void RestorePagefile_Click(object sender, RoutedEventArgs e) => restoreManager.RestorePagefileDefault();
        private void RestoreIndexing_Click(object sender, RoutedEventArgs e) => restoreManager.RestoreIndexing();
        private void CreateSnapshotNow_Click(object sender, RoutedEventArgs e) => restoreManager.CreateSnapshotNow();
        private void RestoreSnapshot_Click(object sender, RoutedEventArgs e) => restoreManager.RestoreLatestSnapshot();

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

        private void chkPagefile_Checked(object sender, RoutedEventArgs e) => PagefileOptionsPanel.Visibility = Visibility.Visible;
        private void chkPagefile_Unchecked(object sender, RoutedEventArgs e) => PagefileOptionsPanel.Visibility = Visibility.Collapsed;

        private void RefreshUserFolders()
        {
            userFolders.Clear();
            foreach (var item in storageAdvisor.GetUserFolders()) userFolders.Add(item);
        }

        private void RefreshUserFolders_Click(object sender, RoutedEventArgs e)
        {
            RefreshUserFolders();
            logger.Log("User folder analysis refreshed.");
        }

        private void OpenSelectedUserFolder_Click(object sender, RoutedEventArgs e)
        {
            if (dgUserFolders.SelectedItem is UserFolderEntry entry && entry.Exists)
                storageAdvisor.OpenInExplorer(entry.CurrentPath);
        }

        private void CopySelectedUserFolderPath_Click(object sender, RoutedEventArgs e)
        {
            if (dgUserFolders.SelectedItem is UserFolderEntry entry)
            {
                Clipboard.SetText(entry.CurrentPath ?? string.Empty);
                logger.Log("Copied selected user folder path to clipboard.");
            }
        }

        private void RefreshOneDrive()
        {
            var od = storageAdvisor.GetOneDriveEntry();
            txtOneDriveRoot.Text = $"Detected root: {od.Path}";
            txtOneDriveStatus.Text = od.Notes;
        }

        private void RefreshOneDrive_Click(object sender, RoutedEventArgs e)
        {
            RefreshOneDrive();
            logger.Log("OneDrive status refreshed.");
        }

        private void OpenOneDriveFolder_Click(object sender, RoutedEventArgs e)
        {
            var od = storageAdvisor.GetOneDriveEntry();
            if (!storageAdvisor.OpenInExplorer(od.Path)) logger.Log("OneDrive folder could not be opened.");
        }

        private void OpenOneDriveApp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string exe = Path.Combine(localAppData, "Microsoft", "OneDrive", "OneDrive.exe");
                if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                else logger.Log("OneDrive executable not found in the standard local path.");
            }
            catch (Exception ex)
            {
                logger.Log("ERR: " + ex.Message);
            }
        }

        private void RefreshVsPaths()
        {
            vsPaths.Clear();
            foreach (var entry in storageAdvisor.GetVisualStudioLocations()) vsPaths.Add(entry);
        }

        private void RefreshVsPaths_Click(object sender, RoutedEventArgs e)
        {
            RefreshVsPaths();
            logger.Log("Visual Studio storage paths refreshed.");
        }

        private void OpenSelectedVsPath_Click(object sender, RoutedEventArgs e)
        {
            if (dgVsPaths.SelectedItem is PathEntry entry)
                storageAdvisor.OpenInExplorer(entry.Path);
        }

        private void OpenVsInstaller_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var installer = vsPaths.FirstOrDefault(x => x.Name == "Visual Studio Installer" && File.Exists(x.Path));
                if (installer != null) Process.Start(new ProcessStartInfo(installer.Path) { UseShellExecute = true });
                else logger.Log("Visual Studio Installer was not found in the standard path.");
            }
            catch (Exception ex)
            {
                logger.Log("ERR: " + ex.Message);
            }
        }

        private async void ScanCandidates_Click(object sender, RoutedEventArgs e)
        {
            if (candidateScanCancellation != null)
            {
                logger.Log("A candidate scan is already running.");
                return;
            }

            string root = txtCandidateScanRoot.Text?.Trim() ?? string.Empty;
            candidates.Clear();
            txtCandidateStatus.Text = "Scanning...";
            btnScanCandidates.IsEnabled = false;
            btnCancelCandidateScan.IsEnabled = true;
            logger.Log("Scanning large file and folder candidates...");
            candidateScanCancellation = new CancellationTokenSource();

            try
            {
                var found = await Task.Run(() => storageAdvisor.ScanCandidates(root, 80, candidateScanCancellation.Token));
                foreach (var item in found) candidates.Add(item);
                txtCandidateStatus.Text = $"Candidate scan completed. {found.Count} item(s) listed.";
                logger.Log($"Candidate scan completed. {found.Count} item(s) listed.");
            }
            catch (OperationCanceledException)
            {
                txtCandidateStatus.Text = "Candidate scan cancelled.";
                logger.Log("Candidate scan cancelled.");
            }
            catch (Exception ex)
            {
                txtCandidateStatus.Text = "Candidate scan failed.";
                logger.Log("ERR: " + ex.Message);
            }
            finally
            {
                candidateScanCancellation.Dispose();
                candidateScanCancellation = null;
                btnScanCandidates.IsEnabled = true;
                btnCancelCandidateScan.IsEnabled = false;
                RefreshCopilotSuggestions();
            }
        }

        private void CancelCandidateScan_Click(object sender, RoutedEventArgs e)
        {
            candidateScanCancellation?.Cancel();
        }

        private void MoveSelectedCandidate_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedCandidates();
            if (selected.Count == 0)
            {
                logger.Log("No candidate selected.");
                return;
            }

            string target = txtCandidateMoveTarget.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            {
                logger.Log("Enter an existing destination folder before moving a candidate.");
                return;
            }

            int moved = 0;
            foreach (var candidate in selected.ToList())
            {
                if (storageAdvisor.MoveCandidate(candidate, target))
                {
                    candidates.Remove(candidate);
                    moved++;
                }
            }

            logger.Log($"Moved {moved} candidate(s).");
            RefreshUserFolders();
            RefreshCopilotSuggestions();
        }

        private void QuarantineSelectedCandidate_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedCandidates();
            if (selected.Count == 0)
            {
                logger.Log("No candidate selected.");
                return;
            }

            var result = MessageBox.Show($"Move {selected.Count} selected item(s) to the app quarantine folder?", "Confirm quarantine", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            int quarantined = 0;
            foreach (var candidate in selected.ToList())
            {
                if (storageAdvisor.QuarantineCandidate(candidate))
                {
                    candidates.Remove(candidate);
                    quarantined++;
                }
            }

            logger.Log($"Quarantined {quarantined} candidate(s).");
            RefreshUserFolders();
            RefreshCopilotSuggestions();
        }

        private void DeleteSelectedCandidate_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedCandidates();
            if (selected.Count == 0)
            {
                logger.Log("No candidate selected.");
                return;
            }

            var result = MessageBox.Show($"Delete {selected.Count} selected item(s) permanently? This does not use the Recycle Bin.", "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            int deleted = 0;
            foreach (var candidate in selected.ToList())
            {
                if (storageAdvisor.DeleteCandidate(candidate))
                {
                    candidates.Remove(candidate);
                    deleted++;
                }
            }

            logger.Log($"Deleted {deleted} candidate(s) permanently.");
            RefreshUserFolders();
            RefreshCopilotSuggestions();
        }

        private void OpenSelectedCandidate_Click(object sender, RoutedEventArgs e)
        {
            if (dgCandidates.SelectedItem is StorageCandidate candidate)
                storageAdvisor.OpenInExplorer(candidate.Path);
        }

        private void RefreshCopilotSuggestions()
        {
            txtCopilotSummary.Text = storageAdvisor.BuildCopilotSummary(candidates);
        }

        private void RefreshCopilotSuggestions_Click(object sender, RoutedEventArgs e)
        {
            RefreshCopilotSuggestions();
            logger.Log("Copilot suggestions refreshed.");
        }

        private void CopyCopilotSummary_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtCopilotSummary.Text ?? string.Empty);
            logger.Log("Copied Copilot summary to clipboard.");
        }

        private void OpenStorageSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                logger.Log("ERR: " + ex.Message);
            }
        }

        private void OpenCopilot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-copilot:") { UseShellExecute = true });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://copilot.microsoft.com/") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    logger.Log("ERR: " + ex.Message);
                }
            }
        }

        private void ExportSafeReclaimScript_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = EnsureSafeReclaimScriptExported();
                logger.Log($"Safe reclaim script exported to: {path}");
                MessageBox.Show($"Safe reclaim script exported to:\n\n{path}", "Script exported", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                logger.Log("ERR: " + ex.Message);
            }
        }

        private async void RunSafeReclaimScript_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dismPrompt = MessageBox.Show(
                    "Run DISM component cleanup (/StartComponentCleanup /ResetBase) after safe reclaim?\n\nThis is Microsoft-supported but irreversible.",
                    "Safe reclaim options",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                bool runDismCleanup = dismPrompt == MessageBoxResult.Yes;
                var result = await Task.Run(() => SafeReclaimExecutor.Execute(logger.Log, runDismCleanup));
                logger.Log($"Safe reclaim (compiled mode) delta: {result.DeltaGb} GB");
                RefreshDiskInfo();
            }
            catch (Exception ex)
            {
                logger.Log("ERR: " + ex.Message);
            }
        }

        private void PreviewSafeReclaimScript_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = EnsureSafeReclaimScriptExported();
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
                logger.Log($"Opened safe reclaim script preview in Notepad: {path}");
            }
            catch (Exception ex)
            {
                logger.Log("ERR: " + ex.Message);
            }
        }

        private void OpenSafeReclaimScriptFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string exportDir = GetSafeReclaimScriptExportDirectory();
                Directory.CreateDirectory(exportDir);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exportDir}\"") { UseShellExecute = true });
                logger.Log($"Opened safe reclaim script folder: {exportDir}");
            }
            catch (Exception ex)
            {
                logger.Log("ERR: " + ex.Message);
            }
        }

        private List<StorageCandidate> GetSelectedCandidates()
        {
            var items = new List<StorageCandidate>();
            foreach (var item in dgCandidates.SelectedItems)
            {
                if (item is StorageCandidate candidate)
                    items.Add(candidate);
            }

            if (items.Count == 0 && dgCandidates.SelectedItem is StorageCandidate single)
                items.Add(single);

            return items
                .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }
    }
}
