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
            bool ok1 = ExecuteStandard("Stop-Service WSearch -ErrorAction SilentlyContinue");
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
            log("Removing conservative bloat-app allowlist...");
            bool overallSuccess = true;

            // Exact package names only. Broad wildcards can match unrelated Microsoft or OEM apps.
            string[] apps =
            {
                "Clipchamp.Clipchamp",
                "Microsoft.MicrosoftSolitaireCollection",
                "Microsoft.Getstarted",
                "Microsoft.BingNews",
                "Microsoft.BingWeather",
                "Microsoft.SkypeApp"
            };

            foreach (var app in apps)
            {
                string script = @"
$packageName = '__PACKAGE__'
$pkgs = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
if (-not $pkgs) {
    'SKIPPED:NO_MATCH:' + $packageName
}
else {
    foreach ($pkg in $pkgs) {
        $running = $false
        if (-not [string]::IsNullOrWhiteSpace($pkg.InstallLocation)) {
            foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
                try {
                    if ($process.Path -and $process.Path.StartsWith($pkg.InstallLocation, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $running = $true
                        break
                    }
                }
                catch { }
            }
        }

        if ($running) {
            'SKIPPED:RUNNING:' + $pkg.Name
            continue
        }

        try {
            Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction Stop
            'REMOVED:' + $pkg.Name
        }
        catch {
            $msg = $_.Exception.Message
            if ($msg -match '0x80073CFA' -or $msg -match 'cannot be uninstalled on a per-user basis' -or $msg -match 'part of Windows') {
                'SKIPPED:PROTECTED:' + $pkg.Name
            }
            else {
                'WARNING:' + $pkg.Name
            }
        }
    }
}".Replace("__PACKAGE__", app.Replace("'", "''"));

                var result = PowerShellHelper.Run(script);
                bool hadResultForPackage = false;

                foreach (var line in SplitLines(result.StdOut))
                {
                    if (line.StartsWith("SKIPPED:NO_MATCH:", StringComparison.OrdinalIgnoreCase))
                    {
                        log($"No installed match for optional app: {app}");
                        hadResultForPackage = true;
                    }
                    else if (line.StartsWith("SKIPPED:RUNNING:", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = line.Substring("SKIPPED:RUNNING:".Length).Trim();
                        log($"Skipped running app rather than forcing it closed: {name}");
                        hadResultForPackage = true;
                    }
                    else if (line.StartsWith("SKIPPED:PROTECTED:", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = line.Substring("SKIPPED:PROTECTED:".Length).Trim();
                        log($"Skipped protected/system app: {name}");
                        hadResultForPackage = true;
                    }
                    else if (line.StartsWith("REMOVED:", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = line.Substring("REMOVED:".Length).Trim();
                        log($"Removed optional app: {name}");
                        hadResultForPackage = true;
                    }
                    else if (line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = line.Substring("WARNING:".Length).Trim();
                        log($"WARNING: App removal returned a warning for {name}");
                        hadResultForPackage = true;
                        overallSuccess = false;
                    }
                }

                if (!hadResultForPackage && !result.Success)
                    overallSuccess = false;
            }

            return overallSuccess;
        }

        public bool CleanWinSxS()
        {
            log("Cleaning WinSxS...");

            var preflight = PowerShellHelper.Run(@"
$pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
           (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
$servicing = Get-Process -Name TiWorker,TrustedInstaller,MoUsoCoreWorker -ErrorAction SilentlyContinue
if ($pending -or $servicing) { 'SERVICING_BUSY' } else { 'SERVICING_IDLE' }");

            if (ContainsAny(preflight.StdOut, "SERVICING_BUSY"))
            {
                log("Skipped WinSxS cleanup because Windows servicing is active or a servicing reboot is pending.");
                return false;
            }

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
            log("Checking Windows Update state before cache cleanup...");

            var result = PowerShellHelper.Run(@"
$updateProcesses = Get-Process -Name TiWorker,TrustedInstaller,MoUsoCoreWorker,UsoClient,wuauclt -ErrorAction SilentlyContinue
$pendingReboot = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
                 (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
$activeBits = @()
if (Get-Command Get-BitsTransfer -ErrorAction SilentlyContinue) {
    try {
        $activeBits = @(Get-BitsTransfer -AllUsers -ErrorAction Stop | Where-Object { $_.JobState -in @('Connecting','Transferring','TransientError') })
    }
    catch { }
}

if ($updateProcesses -or $pendingReboot -or $activeBits.Count -gt 0) {
    'SKIPPED:UPDATE_BUSY'
    return
}

$service = Get-Service -Name wuauserv -ErrorAction SilentlyContinue
$wasRunning = $false
$stoppedByUs = $false
try {
    if ($null -ne $service -and $service.Status -eq 'Running') {
        $wasRunning = $true
        Stop-Service -Name wuauserv -ErrorAction Stop
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
        $stoppedByUs = $true
    }

    $downloadPath = Join-Path $env:SystemRoot 'SoftwareDistribution\Download'
    $removed = 0
    $skipped = 0
    if (Test-Path -LiteralPath $downloadPath) {
        foreach ($item in Get-ChildItem -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue) {
            try {
                Remove-Item -LiteralPath $item.FullName -Recurse -ErrorAction Stop
                $removed++
            }
            catch {
                $skipped++
            }
        }
    }

    if ($skipped -gt 0) {
        'UPDATE_CACHE_PARTIAL:' + $removed + ':' + $skipped
    }
    else {
        'UPDATE_CACHE_CLEARED:' + $removed
    }
}
catch {
    'UPDATE_CACHE_WARNING:' + $_.Exception.Message
}
finally {
    if ($wasRunning -and $stoppedByUs) {
        try {
            Start-Service -Name wuauserv -ErrorAction Stop
            'SERVICE_STATE_RESTORED:wuauserv'
        }
        catch {
            'SERVICE_RESTORE_WARNING:wuauserv:' + $_.Exception.Message
        }
    }
}");

            bool success = result.Success;
            bool sawCompletion = false;

            foreach (var line in SplitLines(result.StdOut))
            {
                if (line.Equals("SKIPPED:UPDATE_BUSY", StringComparison.OrdinalIgnoreCase))
                {
                    log("Skipped Windows Update cache cleanup because servicing, an update transfer, or a required reboot is active.");
                    return false;
                }
                if (line.StartsWith("UPDATE_CACHE_CLEARED:", StringComparison.OrdinalIgnoreCase))
                {
                    log("Windows Update download cache safely cleared while the update subsystem was idle.");
                    sawCompletion = true;
                }
                else if (line.StartsWith("UPDATE_CACHE_PARTIAL:", StringComparison.OrdinalIgnoreCase))
                {
                    log("Windows Update cache cleanup was partial; in-use or protected entries were left untouched.");
                    sawCompletion = true;
                    success = false;
                }
                else if (line.StartsWith("SERVICE_STATE_RESTORED:", StringComparison.OrdinalIgnoreCase))
                {
                    log("Restored the Windows Update service to its previous running state.");
                }
                else if (line.StartsWith("UPDATE_CACHE_WARNING:", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("SERVICE_RESTORE_WARNING:", StringComparison.OrdinalIgnoreCase))
                {
                    log("WARNING: " + line);
                    success = false;
                }
            }

            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                log("WARNING: " + result.StdErr.Trim());
                success = false;
            }

            return success && sawCompletion;
        }

        public bool DisableHibernation()
        {
            log("Disabling hibernation...");
            return ExecuteStandard("powercfg -h off");
        }

        public bool CleanTempFiles()
        {
            log("Cleaning only stale, top-level temporary files that are not in use...");

            var result = PowerShellHelper.Run(@"
$cutoff = (Get-Date).AddDays(-30)
$allowedExtensions = @('.tmp','.temp','.log','.etl','.dmp','.bak')
$roots = @($env:TEMP, (Join-Path $env:SystemRoot 'Temp')) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
$removed = 0
$skipped = 0

foreach ($root in $roots) {
    foreach ($file in Get-ChildItem -LiteralPath $root -Force -File -ErrorAction SilentlyContinue) {
        if ($file.LastWriteTime -ge $cutoff) { continue }
        if ($allowedExtensions -notcontains $file.Extension.ToLowerInvariant()) { continue }

        $stream = $null
        try {
            $stream = [System.IO.File]::Open($file.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $stream.Dispose()
            $stream = $null
            Remove-Item -LiteralPath $file.FullName -ErrorAction Stop
            $removed++
        }
        catch {
            if ($null -ne $stream) { $stream.Dispose() }
            $skipped++
        }
    }

    foreach ($dir in Get-ChildItem -LiteralPath $root -Force -Directory -ErrorAction SilentlyContinue) {
        if ($dir.LastWriteTime -ge $cutoff) { continue }
        try {
            if (-not (Get-ChildItem -LiteralPath $dir.FullName -Force -ErrorAction Stop | Select-Object -First 1)) {
                Remove-Item -LiteralPath $dir.FullName -ErrorAction Stop
            }
        }
        catch { }
    }
}

'TEMP_CLEAN_RESULT:' + $removed + ':' + $skipped
");

            bool success = result.Success;
            foreach (var line in SplitLines(result.StdOut))
            {
                if (line.StartsWith("TEMP_CLEAN_RESULT:", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split(':');
                    string removed = parts.Length > 1 ? parts[1] : "0";
                    string skipped = parts.Length > 2 ? parts[2] : "0";
                    log($"Safe temp cleanup removed {removed} stale top-level file(s); {skipped} in-use/protected file(s) were skipped.");
                }
            }

            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                log("WARNING: " + result.StdErr.Trim());
                success = false;
            }

            return success;
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
elseif (@($svc.DependentServices | Where-Object {{ $_.Status -eq 'Running' }}).Count -gt 0) {{
    'SKIPPED:RUNNING_DEPENDENTS:' + $serviceName
}}
else {{
    $hadWarning = $false
    try {{
        if ($svc.Status -ne 'Stopped') {{ Stop-Service -Name $serviceName -ErrorAction Stop }}
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
                    else if (line.StartsWith("SKIPPED:RUNNING_DEPENDENTS:", StringComparison.OrdinalIgnoreCase))
                        log($"Skipped service because another running service depends on it: {service}");
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
