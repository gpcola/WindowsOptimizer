using System;
using System.Linq;

namespace WindowsOptimizer
{
    public sealed class Optimizer
    {
        private readonly Action<string> log;
        private readonly CleanupSafety cleanupSafety;

        public Optimizer(Action<string> logger, CleanupSafety cleanupSafety)
        {
            log = logger;
            this.cleanupSafety = cleanupSafety;
        }

        public bool CleanTempFiles()
        {
            log("Cleaning only stale, top-level temporary files that are not in use...");

            DateTime cutoff = DateTime.Now.AddDays(-30);
            var allowedExtensions = new HashSet<string>(
                new[] { ".tmp", ".temp", ".log", ".etl", ".dmp", ".bak" },
                StringComparer.OrdinalIgnoreCase);

            string[] roots =
            {
                Path.GetTempPath(),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "Temp")
            };

            int removed = 0;
            int skipped = 0;
            bool rootRejected = false;

            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!cleanupSafety.IsSafeTempRoot(root))
                {
                    rootRejected = true;
                    log($"Safety guard skipped an unexpected or protected temp root: {root}");
                    continue;
                }

                var rootDirectory = new DirectoryInfo(root);

                IEnumerable<FileInfo> files;
                try
                {
                    files = rootDirectory.EnumerateFiles("*", SearchOption.TopDirectoryOnly).ToArray();
                }
                catch (Exception ex)
                {
                    log($"WARNING: Could not enumerate temp root {root}: {ex.Message}");
                    skipped++;
                    continue;
                }

                foreach (FileInfo file in files)
                {
                    if (file.LastWriteTime >= cutoff ||
                        !allowedExtensions.Contains(file.Extension) ||
                        cleanupSafety.ShouldSkip(file))
                    {
                        continue;
                    }

                    try
                    {
                        using (var stream = new FileStream(
                            file.FullName,
                            FileMode.Open,
                            FileAccess.ReadWrite,
                            FileShare.None))
                        {
                        }

                        File.Delete(file.FullName);
                        removed++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                IEnumerable<DirectoryInfo> directories;
                try
                {
                    directories = rootDirectory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly).ToArray();
                }
                catch
                {
                    directories = Array.Empty<DirectoryInfo>();
                }

                foreach (DirectoryInfo directory in directories)
                {
                    if (directory.LastWriteTime >= cutoff || cleanupSafety.ShouldSkip(directory))
                        continue;

                    try
                    {
                        if (!directory.EnumerateFileSystemInfos().Any())
                            directory.Delete();
                    }
                    catch
                    {
                        skipped++;
                    }
                }
            }

            log($"Safe temp cleanup removed {removed} stale file(s); {skipped} in-use/protected item(s) were skipped.");
            log("Browser profiles, Microsoft Store app data, identity stores and custom exclusions were not eligible cleanup targets.");

            return !rootRejected;
        }

        public bool EmptyRecycleBin()
        {
            log("Emptying the Windows Recycle Bin...");

            var result = PowerShellHelper.Run(@"
try {
    Clear-RecycleBin -Force -ErrorAction Stop
    'RECYCLE_BIN_CLEARED'
}
catch {
    $message = $_.Exception.Message
    if ($message -match 'empty' -or
        $message -match 'cannot find' -or
        $message -match 'does not exist') {
        'RECYCLE_BIN_ALREADY_EMPTY'
    }
    else {
        throw
    }
}
");

            foreach (string line in SplitLines(result.StdOut))
            {
                if (line.Equals("RECYCLE_BIN_CLEARED", StringComparison.OrdinalIgnoreCase))
                {
                    log("Recycle Bin emptied.");
                    return true;
                }

                if (line.Equals("RECYCLE_BIN_ALREADY_EMPTY", StringComparison.OrdinalIgnoreCase))
                {
                    log("Recycle Bin was already empty.");
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(result.StdErr))
                log("WARNING: Recycle Bin cleanup: " + result.StdErr.Trim());

            return result.Success;
        }

        public bool ClearUpdateCache()
        {
            log("Checking Windows Update state before cache cleanup...");

            var result = PowerShellHelper.Run(@"
$updateProcesses = Get-Process -Name TiWorker,TrustedInstaller,MoUsoCoreWorker,UsoClient,wuauclt -ErrorAction SilentlyContinue
$pendingReboot =
    (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
    (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')

$activeBits = @()
if (Get-Command Get-BitsTransfer -ErrorAction SilentlyContinue) {
    try {
        $activeBits = @(
            Get-BitsTransfer -AllUsers -ErrorAction Stop |
            Where-Object { $_.JobState -in @('Connecting','Transferring','TransientError') })
    }
    catch { }
}

if ($updateProcesses -or $pendingReboot -or $activeBits.Count -gt 0) {
    'SKIPPED|UPDATE_BUSY'
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

    'UPDATE_CACHE_RESULT|' + $removed + '|' + $skipped
}
finally {
    if ($wasRunning -and $stoppedByUs) {
        try {
            Start-Service -Name wuauserv -ErrorAction Stop
            'SERVICE_RESTORED|wuauserv'
        }
        catch {
            'SERVICE_RESTORE_WARNING|wuauserv|' + $_.Exception.Message
        }
    }
}
");

            bool success = result.Success;
            bool sawResult = false;

            foreach (string line in SplitLines(result.StdOut))
            {
                if (line.Equals("SKIPPED|UPDATE_BUSY", StringComparison.OrdinalIgnoreCase))
                {
                    log("Skipped Windows Update cache cleanup because servicing, an update transfer, or a required reboot is active.");
                    return false;
                }

                if (line.StartsWith("UPDATE_CACHE_RESULT|", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split('|');
                    string removed = parts.Length > 1 ? parts[1] : "0";
                    string skipped = parts.Length > 2 ? parts[2] : "0";

                    log($"Windows Update cache cleanup removed {removed} item(s); {skipped} in-use/protected item(s) were left untouched.");
                    sawResult = true;

                    if (parts.Length > 2 && int.TryParse(parts[2], out int skippedCount) && skippedCount > 0)
                        success = false;
                }
                else if (line.Equals("SERVICE_RESTORED|wuauserv", StringComparison.OrdinalIgnoreCase))
                {
                    log("Restored the Windows Update service to its previous running state.");
                }
                else if (line.StartsWith("SERVICE_RESTORE_WARNING|", StringComparison.OrdinalIgnoreCase))
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

            return success && sawResult;
        }

        public bool CleanComponentStore()
        {
            log("Checking Windows servicing state before component-store cleanup...");

            var preflight = PowerShellHelper.Run(@"
$pending =
    (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
    (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')

$servicing = Get-Process -Name TiWorker,TrustedInstaller,MoUsoCoreWorker -ErrorAction SilentlyContinue

if ($pending -or $servicing) {
    'SERVICING_BUSY'
}
else {
    'SERVICING_IDLE'
}
");

            if (ContainsAny(preflight.StdOut, "SERVICING_BUSY"))
            {
                log("Skipped component-store cleanup because Windows servicing is active or a servicing reboot is pending.");
                return false;
            }

            // Deliberately omit /ResetBase. ResetBase prevents uninstalling superseded
            // updates and is too aggressive for a general housekeeping utility.
            var result = PowerShellHelper.Run("Dism.exe /Online /Cleanup-Image /StartComponentCleanup");

            if (ContainsAny(result.StdOut, "0x800f0806", "pending operations") ||
                ContainsAny(result.StdErr, "0x800f0806", "pending operations"))
            {
                log("Skipped component-store cleanup because Windows servicing has pending operations.");
                return false;
            }

            return LogResult(result, "Windows component-store cleanup");
        }

        public bool OptimizeSystemDrive()
        {
            log("Optimizing system drive C: using Windows media-aware defaults...");

            var result = PowerShellHelper.Run(@"
try {
    Optimize-Volume -DriveLetter C -ErrorAction Stop | Out-Null
    'DRIVE_OPTIMIZED'
}
catch {
    throw
}
");

            if (SplitLines(result.StdOut)
                .Any(line => line.Equals("DRIVE_OPTIMIZED", StringComparison.OrdinalIgnoreCase)))
            {
                log("Windows completed the appropriate optimization for drive C: based on its media type.");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(result.StdErr))
                log("WARNING: Drive optimization: " + result.StdErr.Trim());

            return false;
        }

        public bool RemoveBloatApps()
        {
            log("Removing only the confirmed, conservative optional-app allowlist...");
            bool overallSuccess = true;

            string[] apps =
            {
                "Clipchamp.Clipchamp",
                "Microsoft.MicrosoftSolitaireCollection",
                "Microsoft.Getstarted",
                "Microsoft.BingNews",
                "Microsoft.BingWeather",
                "Microsoft.SkypeApp"
            };

            foreach (string app in apps)
            {
                string script = @"
$packageName = '__PACKAGE__'
$pkgs = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue

if (-not $pkgs) {
    'SKIPPED|NO_MATCH|' + $packageName
    return
}

foreach ($pkg in $pkgs) {
    $running = $false

    if (-not [string]::IsNullOrWhiteSpace($pkg.InstallLocation)) {
        foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
            try {
                if ($process.Path -and
                    $process.Path.StartsWith(
                        $pkg.InstallLocation,
                        [System.StringComparison]::OrdinalIgnoreCase)) {
                    $running = $true
                    break
                }
            }
            catch { }
        }
    }

    if ($running) {
        'SKIPPED|RUNNING|' + $pkg.Name
        continue
    }

    try {
        Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction Stop
        'REMOVED|' + $pkg.Name
    }
    catch {
        $message = $_.Exception.Message

        if ($message -match '0x80073CFA' -or
            $message -match 'cannot be uninstalled on a per-user basis' -or
            $message -match 'part of Windows') {
            'SKIPPED|PROTECTED|' + $pkg.Name
        }
        else {
            'WARNING|' + $pkg.Name + '|' + $message
        }
    }
}
".Replace("__PACKAGE__", app.Replace("'", "''"));

                var result = PowerShellHelper.Run(script);
                bool hadResult = false;

                foreach (string line in SplitLines(result.StdOut))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length < 2)
                        continue;

                    if (parts[0].Equals("REMOVED", StringComparison.OrdinalIgnoreCase))
                    {
                        log("Removed optional app: " + parts[1]);
                        hadResult = true;
                    }
                    else if (parts[0].Equals("SKIPPED", StringComparison.OrdinalIgnoreCase))
                    {
                        string reason = parts.Length > 1 ? parts[1] : "UNKNOWN";
                        string name = parts.Length > 2 ? parts[2] : app;

                        log(reason.Equals("RUNNING", StringComparison.OrdinalIgnoreCase)
                            ? $"Skipped running app rather than closing it: {name}"
                            : $"Skipped optional app ({reason}): {name}");

                        hadResult = true;
                    }
                    else if (parts[0].Equals("WARNING", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = parts.Length > 1 ? parts[1] : app;
                        log("WARNING: Optional app removal failed for " + name);
                        hadResult = true;
                        overallSuccess = false;
                    }
                }

                if (!hadResult && !result.Success)
                    overallSuccess = false;
            }

            return overallSuccess;
        }

        private bool LogResult(PowerShellResult result, string operationName)
        {
            if (!string.IsNullOrWhiteSpace(result.StdOut))
                log(result.StdOut.Trim());

            if (!string.IsNullOrWhiteSpace(result.StdErr))
                log("WARNING: " + result.StdErr.Trim());

            if (!result.Success)
                log($"WARNING: {operationName} exited with code {result.ExitCode}.");

            return result.Success;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return needles.Any(needle =>
                value.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        private static string[] SplitLines(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }
    }
}
