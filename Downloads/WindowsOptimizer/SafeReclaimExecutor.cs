using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace WindowsOptimizer
{
    public sealed class SafeReclaimResult
    {
        public double FreeBeforeGb { get; init; }
        public double FreeAfterGb { get; init; }
        public int DeletedTargetCount { get; init; }

        public double DeltaGb => Math.Round(FreeAfterGb - FreeBeforeGb, 2);
    }

    public static class SafeReclaimExecutor
    {
        private static readonly string[] CleanupPaths =
        {
            "%TEMP%",
            "%USERPROFILE%\\AppData\\Local\\Temp",
            "C:\\Windows\\Temp",
            "C:\\Windows\\SoftwareDistribution\\Download",
            "C:\\Windows\\DeliveryOptimization",
            "C:\\Windows\\Logs\\CBS",
            "%LOCALAPPDATA%\\Microsoft\\Windows\\INetCache",
            "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default\\Cache",
            "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Cache",
            "%APPDATA%\\Mozilla\\Firefox\\Profiles\\*\\cache2"
        };

        public static SafeReclaimResult Execute(Action<string> log, bool runDismCleanup)
        {
            if (log is null)
                throw new ArgumentNullException(nameof(log));

            double freeBefore = DiskHelper.GetFreeSpaceGB("C");
            log("Safe reclaim (compiled mode) started.");
            log($"Free space on C before cleanup: {freeBefore} GB");

            TryClearRecycleBin(log);
            StopUpdateServices(log);

            int deletedTargets = 0;
            foreach (string configuredPath in CleanupPaths)
            {
                string expanded = Environment.ExpandEnvironmentVariables(configuredPath);
                deletedTargets += DeletePathTargets(expanded, log);
            }

            StartUpdateServices(log);

            if (runDismCleanup)
                TryRunDismCleanup(log);

            double freeAfter = DiskHelper.GetFreeSpaceGB("C");
            log($"Free space on C after cleanup: {freeAfter} GB");
            log($"Safe reclaim (compiled mode) completed. Deleted targets: {deletedTargets}");

            return new SafeReclaimResult
            {
                FreeBeforeGb = freeBefore,
                FreeAfterGb = freeAfter,
                DeletedTargetCount = deletedTargets
            };
        }

        private static int DeletePathTargets(string path, Action<string> log)
        {
            int deleted = 0;
            List<string> targets = ResolveTargets(path);

            if (targets.Count == 0)
            {
                log($"Skip (not found): {path}");
                return 0;
            }

            foreach (string target in targets)
            {
                try
                {
                    if (Directory.Exists(target))
                        Directory.Delete(target, true);
                    else if (File.Exists(target))
                        File.Delete(target);
                    else
                        continue;

                    deleted++;
                    log($"Deleted: {target}");
                }
                catch (Exception ex)
                {
                    log($"Skipped (locked or denied): {target} - {ex.Message}");
                }
            }

            return deleted;
        }

        private static List<string> ResolveTargets(string path)
        {
            var targets = new List<string>();

            if (path.IndexOf('*') >= 0 || path.IndexOf('?') >= 0)
            {
                try
                {
                    string? parent = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                        targets.AddRange(Directory.GetFileSystemEntries(parent, Path.GetFileName(path)));
                }
                catch
                {
                    // Intentionally ignored; caller logs skipped path.
                }

                return targets;
            }

            if (Directory.Exists(path) || File.Exists(path))
                targets.Add(path);

            return targets;
        }

        private static void TryClearRecycleBin(Action<string> log)
        {
            try
            {
                var result = PowerShellHelper.Run("Clear-RecycleBin -Force -ErrorAction SilentlyContinue");
                if (!result.Success && !string.IsNullOrWhiteSpace(result.StdErr))
                    log("Recycle Bin clear warning: " + result.StdErr);
                else
                    log("Recycle Bin clear requested.");
            }
            catch (Exception ex)
            {
                log("Recycle Bin clear skipped: " + ex.Message);
            }
        }

        private static void StopUpdateServices(Action<string> log)
        {
            foreach (string service in new[] { "wuauserv", "bits" })
            {
                var result = PowerShellHelper.Run($"Stop-Service -Name '{service}' -Force -ErrorAction SilentlyContinue");
                if (result.Success)
                    log($"Stopped service: {service}");
                else
                    log($"Could not stop service {service}: {result.StdErr}");
            }
        }

        private static void StartUpdateServices(Action<string> log)
        {
            foreach (string service in new[] { "wuauserv", "bits" })
            {
                var result = PowerShellHelper.Run($"Start-Service -Name '{service}' -ErrorAction SilentlyContinue");
                if (result.Success)
                    log($"Started service: {service}");
                else
                    log($"Could not start service {service}: {result.StdErr}");
            }
        }

        private static void TryRunDismCleanup(Action<string> log)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "Dism.exe",
                    Arguments = "/Online /Cleanup-Image /StartComponentCleanup /ResetBase",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null)
                {
                    log("DISM cleanup failed: process creation returned null.");
                    return;
                }

                process.WaitForExit();
                if (process.ExitCode == 0)
                    log("DISM component cleanup completed.");
                else
                    log($"DISM component cleanup exited with code {process.ExitCode}.");
            }
            catch (Exception ex)
            {
                log("DISM cleanup failed: " + ex.Message);
            }
        }
    }
}
