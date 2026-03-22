using System;

namespace WindowsOptimizer
{
    public sealed class RestoreManager
    {
        private readonly Action<string> log;
        private readonly SnapshotManager snapshotManager;

        public RestoreManager(Action<string> logger, SnapshotManager snapshotMgr)
        {
            log = logger;
            snapshotManager = snapshotMgr;
        }

        public void RestoreServicesAndBackgroundApps()
        {
            log("Restoring services and background apps...");

            string[] services =
            {
                "DiagTrack","dmwappushservice","MapsBroker","WSearch",
                "RetailDemo","RemoteRegistry","Fax","XblAuthManager",
                "XblGameSave","XboxNetApiSvc","WbioSrvc","SharedAccess",
                "PhoneSvc","WalletService","PrintNotify"
            };

            foreach (var s in services)
            {
                PowerShellHelper.Run($"if (Get-Service -Name '{s}' -ErrorAction SilentlyContinue) {{ Set-Service -Name '{s}' -StartupType Manual -ErrorAction SilentlyContinue }}");
            }

            PowerShellHelper.Run(@"Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' -Name GlobalUserDisabled -ErrorAction SilentlyContinue");
            PowerShellHelper.Run(@"Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy' -Name LetAppsRunInBackground -ErrorAction SilentlyContinue");
            log("Services/background apps restored to a more default posture.");
        }

        public void RestorePagefileDefault()
        {
            log("Restoring pagefile to automatic management...");
            PowerShellHelper.Run("Get-CimInstance Win32_PageFileSetting -ErrorAction SilentlyContinue | Remove-CimInstance -ErrorAction SilentlyContinue");
            PowerShellHelper.Run("$cs = Get-CimInstance Win32_ComputerSystem; Set-CimInstance -InputObject $cs -Property @{ AutomaticManagedPagefile = $true } | Out-Null");
            log("Pagefile restored to automatic management. Reboot recommended.");
        }

        public void RestoreIndexing()
        {
            log("Restoring indexing service...");
            PowerShellHelper.Run("Set-Service WSearch -StartupType Automatic -ErrorAction SilentlyContinue");
            PowerShellHelper.Run("Start-Service WSearch -ErrorAction SilentlyContinue");
            log("Indexing service re-enabled.");
        }

        public void RestoreLatestSnapshot()
        {
            snapshotManager.RestoreLatestSnapshot();
        }

        public void CreateSnapshotNow()
        {
            snapshotManager.CreateSnapshot("Manual snapshot");
        }
    }
}
