using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WindowsOptimizer.Models;

namespace WindowsOptimizer
{
    public sealed class WslMoveOptions
    {
        public string TargetRoot { get; set; } = string.Empty;
        public bool KeepExportBackup { get; set; } = true;
        public bool RenameExistingDistroDuringMove { get; set; } = true;
    }

    public static class WslHelper
    {
        private const string LxssRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

        public static async Task<List<WslDistroEntry>> GetDistrosAsync()
        {
            var result = await RunProcessAsync("wsl.exe", "--list --verbose");
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(BuildProcessError("wsl.exe --list --verbose", result));
            }

            var distros = ParseWslList(result.StandardOutput);
            using var lxss = Registry.CurrentUser.OpenSubKey(LxssRegistryPath);

            foreach (var distro in distros)
            {
                PopulateRegistryLocation(distro, lxss);
                UpdateVhdxFileSize(distro);
                distro.ConfigSummary = await ReadDistroConfigSummaryAsync(distro);
            }

            return distros;
        }

        public static void UpdateVhdxFileSize(WslDistroEntry distro)
        {
            if (!string.IsNullOrWhiteSpace(distro.VhdxPath) && File.Exists(distro.VhdxPath))
            {
                distro.VhdxSizeBytes = new FileInfo(distro.VhdxPath).Length;
            }

            distro.LastSizeRefresh = DateTime.Now;
        }

        public static async Task RefreshReportedSizesAsync(WslDistroEntry distro)
        {
            UpdateVhdxFileSize(distro);

            if (distro.Version == 2)
            {
                var usage = await RunProcessAsync("wsl.exe", $"-d {QuoteArg(distro.Name)} -- bash -lc \"df -B1 / | awk 'NR==2 {{print $3}}'\"");
                if (usage.ExitCode == 0 && long.TryParse(usage.StandardOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var usedBytes))
                {
                    distro.LinuxUsedBytes = usedBytes;
                }
            }

            distro.LastSizeRefresh = DateTime.Now;
        }

        public static async Task EstimateCacheSizeAsync(WslDistroEntry distro)
        {
            string script = "du -sb /var/cache/apt /var/lib/apt/lists ~/.cache /tmp /var/tmp 2>/dev/null | awk '{s+=$1} END{print s+0}'";
            var result = await RunProcessAsync("wsl.exe", $"-d {QuoteArg(distro.Name)} -- bash -lc {QuoteArg(script)}");

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(BuildProcessError($"cache estimate for {distro.Name}", result));
            }

            if (long.TryParse(result.StandardOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
            {
                distro.CacheEstimateBytes = bytes;
            }

            distro.LastSizeRefresh = DateTime.Now;
        }

        public static async Task CleanCommonCachesAsync(WslDistroEntry distro)
        {
            string script = "set -e; " +
                            "if command -v apt-get >/dev/null 2>&1; then sudo apt-get clean || true; fi; " +
                            "rm -rf ~/.cache/* /tmp/* /var/tmp/* 2>/dev/null || true";
            var result = await RunProcessAsync("wsl.exe", $"-d {QuoteArg(distro.Name)} -- bash -lc {QuoteArg(script)}");

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(BuildProcessError($"cache cleanup for {distro.Name}", result));
            }
        }

        public static async Task MoveDistroByExportImportAsync(WslDistroEntry distro, WslMoveOptions options, Action<string>? log = null)
        {
            if (string.IsNullOrWhiteSpace(options.TargetRoot))
            {
                throw new InvalidOperationException("Target folder is required.");
            }

            Directory.CreateDirectory(options.TargetRoot);

            string safeName = MakeSafeFileName(distro.Name);
            string distroTargetFolder = Path.Combine(options.TargetRoot, safeName);
            Directory.CreateDirectory(distroTargetFolder);

            string exportPath = Path.Combine(distroTargetFolder, $"{safeName}.tar");
            string importedInstallPath = Path.Combine(distroTargetFolder, "Distro");
            Directory.CreateDirectory(importedInstallPath);

            string temporaryName = $"{distro.Name}-pre-move-{DateTime.Now:yyyyMMddHHmmss}";

            log?.Invoke($"Shutting down WSL before moving {distro.Name}...");
            await RunProcessRequiredAsync("wsl.exe", "--shutdown");

            log?.Invoke($"Exporting {distro.Name} to {exportPath}...");
            await RunProcessRequiredAsync("wsl.exe", $"--export {QuoteArg(distro.Name)} {QuoteArg(exportPath)}");

            if (!File.Exists(exportPath) || new FileInfo(exportPath).Length <= 0)
            {
                throw new InvalidOperationException("The WSL export did not produce a usable backup file. The original distro has not been changed.");
            }

            if (options.RenameExistingDistroDuringMove)
            {
                log?.Invoke($"Renaming original distro to {temporaryName} before importing moved copy...");
                await RunProcessRequiredAsync("wsl.exe", $"--rename {QuoteArg(distro.Name)} {QuoteArg(temporaryName)}");

                try
                {
                    log?.Invoke($"Importing moved distro back as {distro.Name}...");
                    await RunProcessRequiredAsync("wsl.exe", $"--import {QuoteArg(distro.Name)} {QuoteArg(importedInstallPath)} {QuoteArg(exportPath)}");

                    if (distro.IsDefault)
                    {
                        await RunProcessRequiredAsync("wsl.exe", $"--set-default {QuoteArg(distro.Name)}");
                    }

                    log?.Invoke("Import completed. Leaving the renamed original distro in place as a recovery copy until manually removed.");
                }
                catch
                {
                    log?.Invoke("Import failed. Restoring original distro name...");
                    await RunProcessRequiredAsync("wsl.exe", $"--rename {QuoteArg(temporaryName)} {QuoteArg(distro.Name)}");
                    throw;
                }
            }
            else
            {
                log?.Invoke("Unregistering original distro after verified export...");
                await RunProcessRequiredAsync("wsl.exe", $"--unregister {QuoteArg(distro.Name)}");
                await RunProcessRequiredAsync("wsl.exe", $"--import {QuoteArg(distro.Name)} {QuoteArg(importedInstallPath)} {QuoteArg(exportPath)}");

                if (distro.IsDefault)
                {
                    await RunProcessRequiredAsync("wsl.exe", $"--set-default {QuoteArg(distro.Name)}");
                }
            }

            if (!options.KeepExportBackup && File.Exists(exportPath))
            {
                log?.Invoke("Removing export backup because Keep export backup is disabled...");
                File.Delete(exportPath);
            }

            log?.Invoke($"WSL move workflow completed for {distro.Name}.");
        }

        public static async Task ResizeDistroVhdAsync(WslDistroEntry distro, string size)
        {
            if (distro.Version != 2)
            {
                throw new InvalidOperationException("VHD resizing is only available for WSL 2 distros.");
            }

            if (string.IsNullOrWhiteSpace(size))
            {
                throw new InvalidOperationException("Enter a size such as 256GB, 512GB, or 1TB.");
            }

            await RunProcessRequiredAsync("wsl.exe", "--shutdown");
            await RunProcessRequiredAsync("wsl.exe", $"--manage {QuoteArg(distro.Name)} --resize {QuoteArg(size.Trim())}");
        }

        public static async Task ShutdownAsync()
        {
            await RunProcessRequiredAsync("wsl.exe", "--shutdown");
        }

        public static string GetGlobalConfigPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");
        }

        public static string ReadGlobalConfigSummary()
        {
            string path = GetGlobalConfigPath();
            if (!File.Exists(path))
            {
                return ".wslconfig does not exist yet. Use Open .wslconfig to create a starter file.";
            }

            var lines = File.ReadAllLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(24);

            return string.Join(Environment.NewLine, lines);
        }

        public static async Task<string> ReadDistroConfigSummaryAsync(WslDistroEntry distro)
        {
            var result = await RunProcessAsync("wsl.exe", $"-d {QuoteArg(distro.Name)} -- bash -lc \"if [ -f /etc/wsl.conf ]; then cat /etc/wsl.conf; else echo 'No /etc/wsl.conf'; fi\"");
            if (result.ExitCode != 0)
            {
                return "Unable to read /etc/wsl.conf";
            }

            return string.Join(Environment.NewLine, result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(20));
        }

        private static List<WslDistroEntry> ParseWslList(string output)
        {
            var result = new List<WslDistroEntry>();

            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Replace("\0", string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("NAME", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isDefault = line.StartsWith("*", StringComparison.Ordinal);
                line = line.TrimStart('*').Trim();

                var match = Regex.Match(line, @"^(?<name>.+?)\s{2,}(?<state>\S+)\s{2,}(?<version>\d+)$");
                if (!match.Success)
                {
                    continue;
                }

                result.Add(new WslDistroEntry
                {
                    Name = match.Groups["name"].Value.Trim(),
                    State = match.Groups["state"].Value.Trim(),
                    Version = int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture),
                    IsDefault = isDefault
                });
            }

            return result;
        }

        private static void PopulateRegistryLocation(WslDistroEntry distro, RegistryKey? lxss)
        {
            if (lxss == null) return;

            foreach (string subKeyName in lxss.GetSubKeyNames())
            {
                using var subKey = lxss.OpenSubKey(subKeyName);
                string? name = subKey?.GetValue("DistributionName") as string;

                if (!string.Equals(name, distro.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? basePath = subKey?.GetValue("BasePath") as string;
                distro.RegistryKey = subKeyName;
                distro.InstallPath = basePath ?? string.Empty;
                object? defaultUid = subKey?.GetValue("DefaultUid");
                distro.DefaultUser = defaultUid == null ? "Unknown" : $"UID {defaultUid}";

                if (!string.IsNullOrWhiteSpace(basePath))
                {
                    string vhdxPath = Path.Combine(basePath, "ext4.vhdx");
                    if (File.Exists(vhdxPath))
                    {
                        distro.VhdxPath = vhdxPath;
                    }
                }

                return;
            }
        }

        private static async Task RunProcessRequiredAsync(string fileName, string arguments)
        {
            var result = await RunProcessAsync(fileName, arguments);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(BuildProcessError($"{fileName} {arguments}", result));
            }
        }

        private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start {fileName}.");
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }

        private static string BuildProcessError(string command, ProcessResult result)
        {
            string stderr = string.IsNullOrWhiteSpace(result.StandardError) ? "No stderr returned." : result.StandardError.Trim();
            string stdout = string.IsNullOrWhiteSpace(result.StandardOutput) ? "No stdout returned." : result.StandardOutput.Trim();
            return $"Command failed: {command}{Environment.NewLine}{stderr}{Environment.NewLine}{stdout}";
        }

        private static string QuoteArg(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string MakeSafeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }

        private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    }
}
