using System;
using WindowsOptimizer.Models;

namespace WindowsOptimizer
{
    public sealed class Optimizer
    {
        private readonly Action<string> log;

        public Optimizer(Action<string> logger)
        {
            log = logger;
        }

        public bool DisableIndexing()
        {
            log("Disabling indexing...");
            bool ok1 = ExecuteStandard("Stop-Service WSearch -Force -ErrorAction SilentlyContinue");
            bool ok2 = ExecuteStandard("Set-Service WSearch -StartupType Disabled -ErrorAction SilentlyContinue");
            return ok1 && ok2;
        }

        public bool RemoveOptionalFeatures()
        {
            log("Removing optional features...");
            bool overallSuccess = true;

            string[] features =
            {
                "WindowsMediaPlayer",
                "Printing-XPSServices-Features",
                "WorkFolders-Client",
                "FaxServicesClientPackage",
                "Internet-Print-Client",
                "SMB1Protocol"
            };

            foreach (var feature in features)
            {
                var result = PowerShellHelper.Run($@"
$featureName = '{feature}'
$f = Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction SilentlyContinue
if ($null -eq $f) {{
    'SKIPPED:NOT_PRESENT:' + $featureName
}}
elseif ($f.State -eq 'Disabled') {{
    'SKIPPED:ALREADY_DISABLED:' + $featureName
}}
else {{
    try {{
        $r = Disable-WindowsOptionalFeature -Online -FeatureName $featureName -NoRestart -ErrorAction Stop
        if ($r.RestartNeeded) {{
            'REBOOT_REQUIRED:' + $featureName
        }}
        else {{
            'DISABLED:' + $featureName
        }}
    }}
    catch {{
        'WARNING:' + $featureName + ':' + $_.Exception.Message
    }}
}}");

                foreach (var line in SplitLines(result.StdOut))
                {
                    if (line.StartsWith("SKIPPED:NOT_PRESENT:", StringComparison.OrdinalIgnoreCase))
                        log($"Skipped optional feature not present on this Windows build: {feature}");
                    else if (line.StartsWith("SKIPPED:ALREADY_DISABLED:", StringComparison.OrdinalIgnoreCase))
                        log($"Skipped optional feature already disabled: {feature}");
                    else if (line.StartsWith("REBOOT_REQUIRED:", StringComparison.OrdinalIgnoreCase))
                        log($"Optional feature disabled: {feature}. Reboot required to complete the change.");
                    else if (line.StartsWith("DISABLED:", StringComparison.OrdinalIgnoreCase))
                        log($"Optional feature disabled: {feature}");
                    else if (line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                    {
                        log($"WARNING: Optional feature change returned a warning for {feature}.");
                        overallSuccess = false;
                    }
                }
            }

            return overallSuccess;
        }

        public bool RemoveBloatApps()
        {
            log("Removing bloat apps...");
            bool overallSuccess = true;

            string[] apps =
            {
                "*xbox*","*bing*","*skype*","*zune*",
                "*officehub*","*solitaire*","*clipchamp*",
                "*getstarted*","*yourphone*"
            };

            foreach (var app in apps)
            {
                var result = PowerShellHelper.Run($@"
$pattern = '{app}'
$pkgs = Get-AppxPackage $pattern -ErrorAction SilentlyContinue
if (-not $pkgs) {{
    'SKIPPED:NO_MATCH:' + $pattern
}}
else {{
    foreach ($pkg in $pkgs) {{
        try {{
            Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction Stop
            'REMOVED:' + $pkg.Name
        }}
        catch {{
            $msg = $_.Exception.Message
            if ($msg -match '0x80073CFA' -or $msg -match 'cannot be uninstalled on a per-user basis' -or $msg -match 'part of Windows') {{
                'SKIPPED:PROTECTED:' + $pkg.Name
            }}
            else {{
                'WARNING:' + $pkg.Name
            }}
        }}
    }}
}}");

                bool hadResultForPattern = false;
                foreach (var line in SplitLines(result.StdOut))
                {
                    if (line.StartsWith("SKIPPED:NO_MATCH:", StringComparison.OrdinalIgnoreCase))
                    {
                        log($"Skipped bloat app pattern with no installed matches: {app}");
                        hadResultForPattern = true;
                    }
                    else if (line.StartsWith("SKIPPED:PROTECTED:", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = line.Substring("SKIPPED:PROTECTED:".Length).Trim();
                        log($"Skipped system app that cannot be removed per-user: {name}");
                        hadResultForPattern = true;
                    }
                    else if (line.StartsWith("REMOVED:", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = line.Substring("REMOVED:".Length).Trim();
                        log($"Removed app: {name}");
                        hadResultForPattern = true;
                    }
                    else if (line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = line.Substring("WARNING:".Length).Trim();
                        log($"WARNING: App removal returned a warning for {name}");
                        hadResultForPattern = true;
                        overallSuccess = false;
                    }
                }

                if (!hadResultForPattern && !result.Success)
                    overallSuccess = false;
            }

            return overallSuccess;
        }

        public bool CleanWinSxS()
        {
            log("Cleaning WinSxS...");
            var result = PowerShellHelper.Run("Dism.exe /Online /Cleanup-Image /StartComponentCleanup /ResetBase");

            if (ContainsAny(result.StdOut, "0x800f0806", "pending operations") || ContainsAny(result.StdErr, "0x800f0806", "pending operations"))
            {
                log("WARNING: WinSxS cleanup skipped because Windows servicing has pending operations. Reboot first, then try again.");
                return false;
            }

            return LogResult(result, nonZeroIsWarning: true, commandName: "Clean WinSxS");
        }

        public bool ClearUpdateCache()
        {
            log("Clearing update cache...");
            bool ok1 = ExecuteStandard("Stop-Service wuauserv -Force -ErrorAction SilentlyContinue");
            bool ok2 = ExecuteStandard(@"Remove-Item 'C:\Windows\SoftwareDistribution\Download\*' -Recurse -Force -ErrorAction SilentlyContinue");
            bool ok3 = ExecuteStandard("Start-Service wuauserv -ErrorAction SilentlyContinue");
            return ok1 && ok2 && ok3;
        }

        public bool DisableHibernation()
        {
            log("Disabling hibernation...");
            return ExecuteStandard("powercfg -h off");
        }

        public bool CleanTempFiles()
        {
            log("Cleaning temp files...");
            bool overallSuccess = true;

            var userTemp = PowerShellHelper.Run(@"Remove-Item ""$env:LOCALAPPDATA\Temp\*"" -Recurse -Force -ErrorAction SilentlyContinue");
            overallSuccess &= LogResult(userTemp, nonZeroIsWarning: true, skipExitCodeWhenNoUsefulOutput: true, commandName: "Clean user temp files");

            var windowsTemp = PowerShellHelper.Run(@"Remove-Item 'C:\Windows\Temp\*' -Recurse -Force -ErrorAction SilentlyContinue");
            overallSuccess &= LogResult(windowsTemp, nonZeroIsWarning: true, skipExitCodeWhenNoUsefulOutput: true, commandName: "Clean Windows temp files");

            if (!overallSuccess)
                log("WARNING: Temp cleanup completed partially. Some files may have been locked or in use.");

            return overallSuccess;
        }

        public bool DeleteRestorePoints()
        {
            log("Deleting restore points...");
            var result = PowerShellHelper.Run("vssadmin delete shadows /all /quiet");

            if (ContainsAny(result.StdOut, "No items found that satisfy the query.") || ContainsAny(result.StdErr, "No items found that satisfy the query."))
            {
                log("Skipped restore point deletion because no restore points were present.");
                return true;
            }

            return LogResult(result, nonZeroIsWarning: true, commandName: "Delete restore points");
        }

        public bool MovePagefile(PagefileOptions options)
        {
            if (options.InitialSizeMb <= 0 || options.MaximumSizeMb <= 0)
            {
                log("ERR: Pagefile sizes must be greater than zero.");
                return false;
            }

            if (options.MaximumSizeMb < options.InitialSizeMb)
            {
                log("ERR: Maximum pagefile size must be greater than or equal to initial size.");
                return false;
            }

            if (!DiskHelper.DriveExists(options.DriveLetter))
            {
                log($"ERR: Selected pagefile drive {options.DriveLetter}: does not exist or is not ready.");
                return false;
            }

            string drive = options.DriveLetter.Trim().TrimEnd(':').ToUpperInvariant();
            string pagefilePath = $@"{drive}:\pagefile.sys";

            log($"Moving pagefile to {drive}: ({options.InitialSizeMb} MB / {options.MaximumSizeMb} MB)...");

            var result = PowerShellHelper.Run($@"
$path = '{pagefilePath}'
$initial = {options.InitialSizeMb}
$maximum = {options.MaximumSizeMb}
try {{
    $cs = Get-CimInstance Win32_ComputerSystem
    Set-CimInstance -InputObject $cs -Property @{{ AutomaticManagedPagefile = $false }} | Out-Null
    Get-CimInstance Win32_PageFileSetting -ErrorAction SilentlyContinue | Remove-CimInstance -ErrorAction SilentlyContinue
    New-CimInstance -ClassName Win32_PageFileSetting -Property @{{ Name = $path; InitialSize = $initial; MaximumSize = $maximum }} | Out-Null
    'PAGEFILE_UPDATED'
}}
catch {{
    'WARNING:' + $_.Exception.Message
}}");

            bool success = false;
            foreach (var line in SplitLines(result.StdOut))
            {
                if (line.Equals("PAGEFILE_UPDATED", StringComparison.OrdinalIgnoreCase))
                {
                    log("Pagefile configuration change requested. Reboot required to complete the change.");
                    success = true;
                }
                else if (line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                {
                    log("WARNING: Pagefile operation returned a warning. Check the log and reboot before reassessing.");
                }
            }

            if (!string.IsNullOrWhiteSpace(result.StdErr))
                log("WARNING: " + result.StdErr);

            return success;
        }

        public bool DisableServices()
        {
            log("Disabling unnecessary services...");
            bool overallSuccess = true;

            string[] services =
            {
                "DiagTrack","dmwappushservice","MapsBroker","WSearch",
                "RetailDemo","RemoteRegistry","Fax","XblAuthManager",
                "XblGameSave","XboxNetApiSvc","WbioSrvc","SharedAccess",
                "PhoneSvc","WalletService","PrintNotify"
            };

            foreach (var service in services)
            {
                var result = PowerShellHelper.Run($@"
$serviceName = '{service}'
$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $svc) {{
    'SKIPPED:NOT_FOUND:' + $serviceName
}}
else {{
    $hadWarning = $false
    try {{
        if ($svc.Status -ne 'Stopped') {{ Stop-Service -Name $serviceName -Force -ErrorAction Stop }}
    }}
    catch {{
        $hadWarning = $true
        'WARNING:STOP:' + $serviceName
    }}

    try {{
        Set-Service -Name $serviceName -StartupType Disabled -ErrorAction Stop
        if ($hadWarning) {{ 'WARNING:DISABLED_WITH_STOP_WARNING:' + $serviceName }} else {{ 'DISABLED:' + $serviceName }}
    }}
    catch {{
        'WARNING:SET_STARTUP:' + $serviceName
    }}
}}");

                foreach (var line in SplitLines(result.StdOut))
                {
                    if (line.StartsWith("SKIPPED:NOT_FOUND:", StringComparison.OrdinalIgnoreCase))
                        log($"Skipped service not present on this system: {service}");
                    else if (line.StartsWith("DISABLED:", StringComparison.OrdinalIgnoreCase))
                        log($"Service disabled: {service}");
                    else if (line.StartsWith("WARNING:DISABLED_WITH_STOP_WARNING:", StringComparison.OrdinalIgnoreCase))
                    {
                        log($"WARNING: Service startup was disabled, but stopping the running service returned a warning: {service}");
                        overallSuccess = false;
                    }
                    else if (line.StartsWith("WARNING:STOP:", StringComparison.OrdinalIgnoreCase))
                    {
                        log($"WARNING: Service stop returned a warning: {service}");
                        overallSuccess = false;
                    }
                    else if (line.StartsWith("WARNING:SET_STARTUP:", StringComparison.OrdinalIgnoreCase))
                    {
                        log($"WARNING: Service startup type could not be changed: {service}");
                        overallSuccess = false;
                    }
                }
            }

            return overallSuccess;
        }

        public bool DisableBackgroundApps()
        {
            log("Disabling background apps...");
            bool ok1 = ExecuteStandard(@"New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' -Force | Out-Null");
            bool ok2 = ExecuteStandard(@"Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' -Name GlobalUserDisabled -Value 1 -Type DWord");
            bool ok3 = ExecuteStandard(@"New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy' -Force | Out-Null");
            bool ok4 = ExecuteStandard(@"Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy' -Name LetAppsRunInBackground -Value 2 -Type DWord");
            return ok1 && ok2 && ok3 && ok4;
        }

        private bool ExecuteStandard(string command)
        {
            var result = PowerShellHelper.Run(command);
            return LogResult(result, nonZeroIsWarning: false, skipExitCodeWhenNoUsefulOutput: false, commandName: string.Empty);
        }

        private bool LogResult(PowerShellResult result, bool nonZeroIsWarning, bool skipExitCodeWhenNoUsefulOutput = false, string commandName = "")
        {
            string stdOut = CleanForDisplay(result.StdOut);
            string stdErr = CleanForDisplay(result.StdErr);

            if (!string.IsNullOrWhiteSpace(stdOut))
                log(stdOut);

            if (!string.IsNullOrWhiteSpace(stdErr))
            {
                if (nonZeroIsWarning)
                    log("WARNING: " + stdErr);
                else
                    log("ERR: " + stdErr);
            }

            if (result.Success)
                return true;

            if (skipExitCodeWhenNoUsefulOutput && string.IsNullOrWhiteSpace(stdOut) && string.IsNullOrWhiteSpace(stdErr))
                return false;

            if (!string.IsNullOrWhiteSpace(commandName))
            {
                if (nonZeroIsWarning)
                    log($"WARNING: {commandName} exited with code {result.ExitCode}");
                else
                    log($"Command exited with code {result.ExitCode}");
            }
            else if (!skipExitCodeWhenNoUsefulOutput || !string.IsNullOrWhiteSpace(stdOut) || !string.IsNullOrWhiteSpace(stdErr))
            {
                if (nonZeroIsWarning)
                    log($"WARNING: Command exited with code {result.ExitCode}");
                else
                    log($"Command exited with code {result.ExitCode}");
            }

            return false;
        }

        private static string CleanForDisplay(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim();
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            foreach (var needle in needles)
            {
                if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string[] SplitLines(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

    }
}
