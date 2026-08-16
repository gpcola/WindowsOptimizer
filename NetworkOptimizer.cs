using System;
using System.Collections.Generic;
using System.Linq;

namespace WindowsOptimizer
{
    public sealed class NetworkOptimizer
    {
        private readonly Action<string> log;

        public NetworkOptimizer(Action<string> logger)
        {
            log = logger;
        }

        public string GetStatus()
        {
            var result = PowerShellHelper.Run(@"
$piaPath = Join-Path $env:ProgramFiles 'Private Internet Access\pia-client.exe'
$piaDetected = Test-Path -LiteralPath $piaPath

$profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue)
$physical = @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.Status -ne 'Disabled' })

'PIA|' + $(if ($piaDetected) { 'Detected' } else { 'Not detected' })

foreach ($profile in $profiles) {
    'PROFILE|' + $profile.Name + '|' + $profile.NetworkCategory
}

foreach ($adapter in $physical) {
    'ADAPTER|' + $adapter.Name + '|' + $adapter.Status + '|' + $adapter.LinkSpeed + '|' + $adapter.InterfaceDescription
}
");

            if (!result.Success)
                return "Network status could not be determined.";

            var lines = SplitLines(result.StdOut);
            bool piaDetected = lines.Any(line => line.Equals("PIA|Detected", StringComparison.OrdinalIgnoreCase));

            var profiles = lines
                .Where(line => line.StartsWith("PROFILE|", StringComparison.OrdinalIgnoreCase))
                .Select(line => line.Split(new[] { '|' }, 3, StringSplitOptions.None))
                .Where(parts => parts.Length == 3)
                .Select(parts => $"{parts[1]} ({parts[2]})")
                .ToList();

            var adapters = lines
                .Where(line => line.StartsWith("ADAPTER|", StringComparison.OrdinalIgnoreCase))
                .Select(line => line.Split(new[] { '|' }, 5, StringSplitOptions.None))
                .Where(parts => parts.Length == 5)
                .Select(parts => $"{parts[1]}: {parts[2]}, {parts[3]}")
                .ToList();

            string profileText = profiles.Count > 0
                ? string.Join(", ", profiles)
                : "no active profile detected";

            string adapterText = adapters.Count > 0
                ? string.Join("; ", adapters)
                : "no enabled physical Ethernet/Wi-Fi adapter detected";

            string piaText = piaDetected
                ? "PIA detected. VPN adapter, DNS and MTU settings will be left untouched; enable PIA 'Allow LAN Traffic' for local media devices."
                : "PIA was not detected at its standard install path.";

            return $"Network: {profileText}. Physical adapters: {adapterText}. {piaText}";
        }

        public bool Optimize()
        {
            log("Applying conservative network performance settings to physical Ethernet/Wi-Fi adapters only...");

            var result = PowerShellHelper.Run(@"
$ErrorActionPreference = 'Continue'

# Preserve VPN/tunnel semantics: never touch DNS, MTU, routes, bindings, firewall,
# virtual adapters, LSO/RSC/checksum offloads, or PIA-specific settings.
$vpnPattern = 'Private Internet Access|PIA|Wintun|WireGuard|TAP-Windows|OpenVPN'
$piaPath = Join-Path $env:ProgramFiles 'Private Internet Access\pia-client.exe'

try {
    & netsh int tcp set global autotuninglevel=normal | Out-Null
    if ($LASTEXITCODE -eq 0) {
        'TCP_AUTOTUNING|Normal'
    }
    else {
        'WARNING|TCP autotuning|' + $LASTEXITCODE
    }
}
catch {
    'WARNING|TCP autotuning|' + $_.Exception.Message
}

try {
    & netsh int tcp set global rss=enabled | Out-Null
    if ($LASTEXITCODE -eq 0) {
        'TCP_RSS_GLOBAL|Enabled'
    }
    else {
        'WARNING|Global RSS|' + $LASTEXITCODE
    }
}
catch {
    'WARNING|Global RSS|' + $_.Exception.Message
}

$adapters = @(
    Get-NetAdapter -Physical -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Status -ne 'Disabled' -and
        $_.Name -notmatch $vpnPattern -and
        $_.InterfaceDescription -notmatch $vpnPattern
    })

if ($adapters.Count -eq 0) {
    'WARNING|Adapters|No eligible physical adapters found'
}

foreach ($adapter in $adapters) {
    $name = $adapter.Name
    'ADAPTER_FOUND|' + $name + '|' + $adapter.LinkSpeed

    try {
        $rss = Get-NetAdapterRss -Name $name -ErrorAction Stop
        if ($rss.Enabled) {
            'RSS_ALREADY_ENABLED|' + $name
        }
        else {
            Set-NetAdapterRss -Name $name -Enabled $true -NoRestart -ErrorAction Stop
            'RSS_ENABLED|' + $name
        }
    }
    catch {
        # Wireless adapters and some physical NICs do not expose RSS. This is not an error.
        'RSS_UNSUPPORTED|' + $name
    }

    try {
        $power = Get-NetAdapterPowerManagement -Name $name -ErrorAction Stop
        if ($power.SelectiveSuspend.ToString() -ne 'Unsupported') {
            try {
                Set-NetAdapterPowerManagement -Name $name -SelectiveSuspend Disabled -NoRestart -ErrorAction Stop
                'SELECTIVE_SUSPEND_DISABLED|' + $name
            }
            catch {
                'POWER_SETTING_SKIPPED|' + $name + '|SelectiveSuspend'
            }
        }

        if ($power.DeviceSleepOnDisconnect.ToString() -ne 'Unsupported') {
            try {
                Set-NetAdapterPowerManagement -Name $name -DeviceSleepOnDisconnect Disabled -NoRestart -ErrorAction Stop
                'SLEEP_ON_DISCONNECT_DISABLED|' + $name
            }
            catch {
                'POWER_SETTING_SKIPPED|' + $name + '|DeviceSleepOnDisconnect'
            }
        }
    }
    catch {
        'POWER_MANAGEMENT_UNSUPPORTED|' + $name
    }
}

'PIA|' + $(if (Test-Path -LiteralPath $piaPath) { 'Detected' } else { 'NotDetected' })
");

            bool success = result.Success;
            bool foundAdapter = false;

            foreach (string line in SplitLines(result.StdOut))
            {
                string[] parts = line.Split('|');
                string code = parts.Length > 0 ? parts[0] : string.Empty;
                string name = parts.Length > 1 ? parts[1] : string.Empty;

                switch (code.ToUpperInvariant())
                {
                    case "TCP_AUTOTUNING":
                        log("TCP receive-window auto-tuning set to Normal.");
                        break;
                    case "TCP_RSS_GLOBAL":
                        log("Windows global Receive Side Scaling (RSS) enabled.");
                        break;
                    case "ADAPTER_FOUND":
                        foundAdapter = true;
                        string speed = parts.Length > 2 ? parts[2] : "unknown speed";
                        log($"Eligible physical adapter: {name} ({speed}).");
                        break;
                    case "RSS_ENABLED":
                        log($"Enabled RSS on physical adapter: {name}. Adapter was not restarted.");
                        break;
                    case "RSS_ALREADY_ENABLED":
                        log($"RSS already enabled on physical adapter: {name}.");
                        break;
                    case "RSS_UNSUPPORTED":
                        log($"RSS not exposed by adapter {name}; left unchanged (normal for Wi-Fi and some NICs).");
                        break;
                    case "SELECTIVE_SUSPEND_DISABLED":
                        log($"Disabled selective suspend on physical adapter: {name}. Adapter was not restarted.");
                        break;
                    case "SLEEP_ON_DISCONNECT_DISABLED":
                        log($"Disabled device sleep-on-disconnect on physical adapter: {name}. Adapter was not restarted.");
                        break;
                    case "POWER_SETTING_SKIPPED":
                    case "POWER_MANAGEMENT_UNSUPPORTED":
                        log($"Power-management setting unsupported for {name}; left unchanged.");
                        break;
                    case "PIA":
                        if (name.Equals("Detected", StringComparison.OrdinalIgnoreCase))
                        {
                            log("Private Internet Access detected. PIA virtual adapters, DNS, MTU, routes and tunnel settings were deliberately left untouched.");
                            log("For LAN media streaming while PIA is connected, enable 'Allow LAN Traffic' in PIA Settings > Network.");
                        }
                        break;
                    case "WARNING":
                        log("WARNING: Network optimization: " + string.Join(" | ", parts.Skip(1)));
                        success = false;
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                log("WARNING: Network optimization: " + result.StdErr.Trim());
                success = false;
            }

            return success && foundAdapter;
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
