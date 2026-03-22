using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WindowsOptimizer.Models;

namespace WindowsOptimizer
{
    public sealed class SnapshotManager
    {
        private readonly Action<string> log;
        private readonly string snapshotFolder;
        private readonly string latestSnapshotPath;

        private static readonly string[] ManagedServices =
        {
            "DiagTrack","dmwappushservice","MapsBroker","WSearch",
            "RetailDemo","RemoteRegistry","Fax","XblAuthManager",
            "XblGameSave","XboxNetApiSvc","WbioSrvc","SharedAccess",
            "PhoneSvc","WalletService","PrintNotify"
        };

        public SnapshotManager(Action<string> logger)
        {
            log = logger;
            snapshotFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsOptimizer",
                "Snapshots");
            Directory.CreateDirectory(snapshotFolder);
            latestSnapshotPath = Path.Combine(snapshotFolder, "latest-snapshot.json");
        }

        public string SnapshotFolder => snapshotFolder;
        public string LatestSnapshotPath => latestSnapshotPath;

        public bool CreateSnapshot(string notes = "Automatic pre-run snapshot")
        {
            try
            {
                var snapshot = new AppSnapshot
                {
                    CreatedAt = DateTime.Now,
                    Notes = notes,
                    IndexingStartupType = GetServiceStartupType("WSearch"),
                    AutomaticManagedPagefile = GetBoolCommand("(Get-CimInstance Win32_ComputerSystem).AutomaticManagedPagefile"),
                    BackgroundAppsUserValueExists = RegistryValueExists(@"HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled"),
                    BackgroundAppsPolicyValueExists = RegistryValueExists(@"HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground")
                };

                if (snapshot.BackgroundAppsUserValueExists)
                    snapshot.BackgroundAppsUserValue = GetNullableIntRegistry(@"HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled");

                if (snapshot.BackgroundAppsPolicyValueExists)
                    snapshot.BackgroundAppsPolicyValue = GetNullableIntRegistry(@"HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground");

                foreach (var service in ManagedServices)
                    snapshot.ServiceStartupTypes[service] = GetServiceStartupType(service);

                snapshot.Pagefiles = GetPagefileSnapshot();

                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(latestSnapshotPath, json);

                string timestampPath = Path.Combine(snapshotFolder, $"snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(timestampPath, json);

                log($"Snapshot backup created: {latestSnapshotPath}");
                return true;
            }
            catch (Exception ex)
            {
                log($"ERR: Snapshot creation failed: {ex.Message}");
                return false;
            }
        }

        public bool RestoreLatestSnapshot()
        {
            if (!File.Exists(latestSnapshotPath))
            {
                log("No snapshot backup found to restore.");
                return false;
            }

            try
            {
                var snapshot = JsonSerializer.Deserialize<AppSnapshot>(File.ReadAllText(latestSnapshotPath));
                if (snapshot == null)
                {
                    log("Snapshot file could not be read.");
                    return false;
                }

                log($"Restoring snapshot from {snapshot.CreatedAt:g}...");

                if (!string.IsNullOrWhiteSpace(snapshot.IndexingStartupType))
                    SetServiceStartupType("WSearch", snapshot.IndexingStartupType);

                foreach (var pair in snapshot.ServiceStartupTypes)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Value))
                        SetServiceStartupType(pair.Key, pair.Value!);
                }

                RestoreRegistryValue(
                    @"HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
                    "GlobalUserDisabled",
                    snapshot.BackgroundAppsUserValueExists,
                    snapshot.BackgroundAppsUserValue);

                RestoreRegistryValue(
                    @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy",
                    "LetAppsRunInBackground",
                    snapshot.BackgroundAppsPolicyValueExists,
                    snapshot.BackgroundAppsPolicyValue);

                RestorePagefiles(snapshot);

                log("Snapshot restore completed.");
                return true;
            }
            catch (Exception ex)
            {
                log($"ERR: Snapshot restore failed: {ex.Message}");
                return false;
            }
        }

        private void RestorePagefiles(AppSnapshot snapshot)
        {
            if (snapshot.AutomaticManagedPagefile)
            {
                PowerShellHelper.Run("Get-CimInstance Win32_PageFileSetting -ErrorAction SilentlyContinue | Remove-CimInstance -ErrorAction SilentlyContinue");
                PowerShellHelper.Run("$cs = Get-CimInstance Win32_ComputerSystem; Set-CimInstance -InputObject $cs -Property @{ AutomaticManagedPagefile = $true } | Out-Null");
                log("Pagefile restored to automatic management. Reboot recommended.");
                return;
            }

            PowerShellHelper.Run("$cs = Get-CimInstance Win32_ComputerSystem; Set-CimInstance -InputObject $cs -Property @{ AutomaticManagedPagefile = $false } | Out-Null");
            PowerShellHelper.Run("Get-CimInstance Win32_PageFileSetting -ErrorAction SilentlyContinue | Remove-CimInstance -ErrorAction SilentlyContinue");

            foreach (var pf in snapshot.Pagefiles.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
            {
                PowerShellHelper.Run($"New-CimInstance -ClassName Win32_PageFileSetting -Property @{{ Name = '{pf.Name}'; InitialSize = {pf.InitialSize}; MaximumSize = {pf.MaximumSize} }} | Out-Null");
                log($"Restored pagefile setting: {pf.Name} ({pf.InitialSize} MB / {pf.MaximumSize} MB)");
            }

            if (snapshot.Pagefiles.Any())
                log("Pagefile restore requested. Reboot recommended.");
        }

        private static bool RegistryValueExists(string path, string name)
        {
            var result = PowerShellHelper.Run(
                $"if (Get-ItemProperty -Path '{path}' -Name '{name}' -ErrorAction SilentlyContinue) {{ 'true' }} else {{ 'false' }}");
            return result.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static int? GetNullableIntRegistry(string path, string name)
        {
            var result = PowerShellHelper.Run(
                $"$v = (Get-ItemProperty -Path '{path}' -Name '{name}' -ErrorAction SilentlyContinue).{name}; if ($null -ne $v) {{ $v }}");
            return int.TryParse(result.StdOut.Trim(), out int value) ? value : null;
        }

        private static bool GetBoolCommand(string command)
        {
            var result = PowerShellHelper.Run(command);
            return bool.TryParse(result.StdOut.Trim(), out bool value) && value;
        }

        private static string? GetServiceStartupType(string serviceName)
        {
            var result = PowerShellHelper.Run($"(Get-CimInstance Win32_Service -Filter 'Name=\'{serviceName}\'').StartMode");
            string value = result.StdOut.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static void SetServiceStartupType(string serviceName, string mode)
        {
            string mapped = mode.Trim().ToLowerInvariant() switch
            {
                "auto" => "Automatic",
                "automatic" => "Automatic",
                "manual" => "Manual",
                "disabled" => "Disabled",
                _ => "Manual"
            };

            PowerShellHelper.Run($"if (Get-Service -Name '{serviceName}' -ErrorAction SilentlyContinue) {{ Set-Service -Name '{serviceName}' -StartupType {mapped} -ErrorAction SilentlyContinue }}");
        }

        private void RestoreRegistryValue(string path, string name, bool shouldExist, int? value)
        {
            if (shouldExist && value.HasValue)
            {
                PowerShellHelper.Run($"New-Item -Path '{path}' -Force | Out-Null");
                PowerShellHelper.Run($"Set-ItemProperty -Path '{path}' -Name '{name}' -Value {value.Value} -Type DWord");
                log($"Restored registry value {name} = {value.Value}");
            }
            else
            {
                PowerShellHelper.Run($"Remove-ItemProperty -Path '{path}' -Name '{name}' -ErrorAction SilentlyContinue");
                log($"Removed registry value {name}");
            }
        }

        private static List<PagefileSettingSnapshot> GetPagefileSnapshot()
        {
            var result = PowerShellHelper.Run(
                "Get-CimInstance Win32_PageFileSetting | Select-Object Name, InitialSize, MaximumSize | ConvertTo-Json -Compress");

            if (string.IsNullOrWhiteSpace(result.StdOut))
                return new List<PagefileSettingSnapshot>();

            try
            {
                var json = result.StdOut.Trim();

                if (json.StartsWith("["))
                    return JsonSerializer.Deserialize<List<PagefileSettingSnapshot>>(json) ?? new List<PagefileSettingSnapshot>();

                var single = JsonSerializer.Deserialize<PagefileSettingSnapshot>(json);
                return single == null ? new List<PagefileSettingSnapshot>() : new List<PagefileSettingSnapshot> { single };
            }
            catch
            {
                return new List<PagefileSettingSnapshot>();
            }
        }
    }
}
