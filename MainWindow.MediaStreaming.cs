using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WindowsOptimizer
{
    public partial class MainWindow
    {
        private async void EnableMediaStreaming_Click(object sender, RoutedEventArgs e)
        {
            if (isBusy)
            {
                SetMediaStreamingStatus("Another operation is already running.");
                return;
            }

            isBusy = true;
            SetBusyState(true);
            SetQuickMediaStreamingButtonEnabled(false);

            try
            {
                SetMediaStreamingStatus("Checking the active network and Windows media components...");
                logger.Log("Checking prerequisites for LAN media streaming.");

                var networkResult = await Task.Run(GetNetworkProfileStatus);
                var networkLines = SplitMediaOutput(networkResult.StdOut)
                    .Where(line => line.StartsWith("PROFILE|", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!networkResult.Success || networkLines.Count == 0)
                {
                    SetMediaStreamingStatus("No active Windows network profile could be verified. Media streaming was not enabled.");
                    logger.Log("Media streaming skipped because no active network profile could be verified.");
                    return;
                }

                var publicProfiles = networkLines
                    .Select(ParseNetworkProfile)
                    .Where(profile => profile.HasValue &&
                                      profile.Value.Category.Equals("Public", StringComparison.OrdinalIgnoreCase))
                    .Select(profile => profile.Value)
                    .ToList();

                if (publicProfiles.Count > 0)
                {
                    string names = string.Join(
                        Environment.NewLine,
                        publicProfiles.Select(profile => $"• {profile.Name}"));

                    var confirmPrivate = MessageBox.Show(
                        "Windows media streaming should only be enabled on a trusted LAN.\n\n" +
                        "The following active network profile(s) are currently Public:\n" +
                        names +
                        "\n\nChange these profile(s) to Private and continue?",
                        "Trusted LAN required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (confirmPrivate != MessageBoxResult.Yes)
                    {
                        SetMediaStreamingStatus("Media streaming was not enabled because the active LAN was not confirmed as trusted/private.");
                        logger.Log("Media streaming cancelled because the user did not approve changing the active Public network profile.");
                        return;
                    }

                    int[] interfaceIndices = publicProfiles
                        .Select(profile => profile.InterfaceIndex)
                        .Distinct()
                        .ToArray();

                    var privateResult = await Task.Run(() => SetNetworkProfilesPrivate(interfaceIndices));
                    if (!privateResult.Success ||
                        !SplitMediaOutput(privateResult.StdOut)
                            .Any(line => line.Equals("NETWORKS_PRIVATE", StringComparison.OrdinalIgnoreCase)))
                    {
                        SetMediaStreamingStatus("Windows could not change the active network to Private. Media streaming was not enabled.");
                        logger.Log("WARNING: Failed to change one or more active network profiles to Private.");
                        if (!string.IsNullOrWhiteSpace(privateResult.StdErr))
                            logger.Log("WARNING: " + privateResult.StdErr.Trim());
                        return;
                    }

                    logger.Log("Approved active LAN profile(s) changed from Public to Private.");
                }

                string? wmpConfigPath = await Task.Run(FindWmpConfigPath);

                if (string.IsNullOrWhiteSpace(wmpConfigPath))
                {
                    var addMedia = MessageBox.Show(
                        "Windows Media Player Legacy / Windows media components are not currently available.\n\n" +
                        "Windows' built-in DLNA media sharing requires them. Add the required Microsoft media component now?\n\n" +
                        "Windows Optimizer will only ADD the missing feature or capability. It will never remove Windows features.",
                        "Add Windows media component",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (addMedia != MessageBoxResult.Yes)
                    {
                        SetMediaStreamingStatus("Media streaming was not enabled because the required Windows media component is not installed.");
                        logger.Log("Media component installation declined by the user.");
                        return;
                    }

                    SetMediaStreamingStatus("Adding the required Microsoft media component...");
                    var installResult = await Task.Run(InstallWindowsMediaComponents);
                    string[] installLines = SplitMediaOutput(installResult.StdOut);

                    foreach (string line in installLines)
                    {
                        if (line.StartsWith("ADDED|", StringComparison.OrdinalIgnoreCase))
                            logger.Log("Added Windows media component: " + line.Substring("ADDED|".Length));
                    }

                    if (!installResult.Success)
                    {
                        SetMediaStreamingStatus("Windows could not add the required media component. No existing Windows feature was removed or changed.");
                        logger.Log("WARNING: Windows media component installation failed.");
                        if (!string.IsNullOrWhiteSpace(installResult.StdErr))
                            logger.Log("WARNING: " + installResult.StdErr.Trim());
                        return;
                    }

                    bool restartRequired = installLines
                        .Any(line => line.Equals("REBOOT_REQUIRED", StringComparison.OrdinalIgnoreCase));
                    bool componentAdded = installLines
                        .Any(line => line.StartsWith("ADDED|", StringComparison.OrdinalIgnoreCase));

                    wmpConfigPath = await Task.Run(FindWmpConfigPath);

                    if (string.IsNullOrWhiteSpace(wmpConfigPath))
                    {
                        if (restartRequired || componentAdded)
                        {
                            SetMediaStreamingStatus(
                                "The Microsoft media component was added successfully. Restart Windows, then press this button again to finish enabling LAN streaming.");
                            logger.Log("Windows media component added; restart required before media streaming can be enabled.");
                        }
                        else
                        {
                            SetMediaStreamingStatus(
                                "This Windows edition did not expose the Microsoft media component required for built-in DLNA streaming. Nothing was removed or disabled.");
                            logger.Log("WARNING: Compatible Windows media streaming components were not found on this Windows edition.");
                        }
                        return;
                    }
                }

                SetMediaStreamingStatus("Enabling Windows media sharing, browsing and its firewall exception...");
                var enableResult = await Task.Run(() => RunWmpConfig(wmpConfigPath!, "HMEOn"));

                if (!enableResult.Success ||
                    !SplitMediaOutput(enableResult.StdOut)
                        .Any(line => line.Equals("STREAMING_ENABLED", StringComparison.OrdinalIgnoreCase)))
                {
                    SetMediaStreamingStatus("Windows media streaming could not be enabled. No Windows features were removed.");
                    logger.Log("WARNING: Windows media streaming enablement failed.");
                    if (!string.IsNullOrWhiteSpace(enableResult.StdErr))
                        logger.Log("WARNING: " + enableResult.StdErr.Trim());
                    return;
                }

                SetMediaStreamingStatus(
                    "LAN media streaming is enabled for this trusted private network. Windows Media Player libraries can now be shared with compatible devices.");
                logger.Log("LAN media streaming enabled using Windows Media Player network sharing.");
            }
            catch (Exception ex)
            {
                SetMediaStreamingStatus("Media streaming stopped safely after an error. No Windows features were removed.");
                logger.Log("ERR: Media streaming: " + ex.Message);
            }
            finally
            {
                isBusy = false;
                SetBusyState(false);
                SetQuickMediaStreamingButtonEnabled(true);
            }
        }

        private async void DisableMediaStreaming_Click(object sender, RoutedEventArgs e)
        {
            if (isBusy)
            {
                SetMediaStreamingStatus("Another operation is already running.");
                return;
            }

            string? wmpConfigPath = await Task.Run(FindWmpConfigPath);
            if (string.IsNullOrWhiteSpace(wmpConfigPath))
            {
                SetMediaStreamingStatus("Windows Media Player network sharing is not installed, so there is nothing to turn off.");
                return;
            }

            var confirm = MessageBox.Show(
                "Turn off Windows Media Player network sharing on this PC?\n\n" +
                "This disables media sharing and its firewall exception but does NOT uninstall Windows Media Player or remove any Windows feature.",
                "Turn off LAN media streaming",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            isBusy = true;
            SetBusyState(true);
            SetQuickMediaStreamingButtonEnabled(false);

            try
            {
                SetMediaStreamingStatus("Turning off LAN media streaming...");
                var result = await Task.Run(() => RunWmpConfig(wmpConfigPath, "HMEOff"));

                if (result.Success &&
                    SplitMediaOutput(result.StdOut)
                        .Any(line => line.Equals("STREAMING_DISABLED", StringComparison.OrdinalIgnoreCase)))
                {
                    SetMediaStreamingStatus("LAN media streaming is turned off. Installed Windows media features were left in place.");
                    logger.Log("LAN media streaming turned off; media components were preserved.");
                }
                else
                {
                    SetMediaStreamingStatus("Windows could not turn off media streaming cleanly. Review the activity log.");
                    logger.Log("WARNING: Media streaming disablement returned an error.");
                    if (!string.IsNullOrWhiteSpace(result.StdErr))
                        logger.Log("WARNING: " + result.StdErr.Trim());
                }
            }
            finally
            {
                isBusy = false;
                SetBusyState(false);
                SetQuickMediaStreamingButtonEnabled(true);
            }
        }

        private void RefreshMediaStreamingStatus()
        {
            try
            {
                string? path = FindWmpConfigPath();
                var serviceResult = PowerShellHelper.Run(@"
$svc = Get-Service -Name WMPNetworkSvc -ErrorAction SilentlyContinue
if ($null -eq $svc) {
    'SERVICE|Not installed'
}
else {
    'SERVICE|' + $svc.Status
}

$profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue)
if ($profiles.Count -eq 0) {
    'NETWORK|No active profile'
}
else {
    'NETWORK|' + (($profiles | ForEach-Object { $_.Name + ' (' + $_.NetworkCategory + ')' }) -join ', ')
}");

                string service = SplitMediaOutput(serviceResult.StdOut)
                    .FirstOrDefault(line => line.StartsWith("SERVICE|", StringComparison.OrdinalIgnoreCase))
                    ?.Substring("SERVICE|".Length) ?? "Unknown";

                string network = SplitMediaOutput(serviceResult.StdOut)
                    .FirstOrDefault(line => line.StartsWith("NETWORK|", StringComparison.OrdinalIgnoreCase))
                    ?.Substring("NETWORK|".Length) ?? "Unknown";

                string status = string.IsNullOrWhiteSpace(path)
                    ? $"Windows media streaming components are not installed. Network: {network}."
                    : $"Windows media streaming components are available. Sharing service: {service}. Network: {network}.";

                txtMediaStreamingStatus.Text = status;
                SetQuickMediaStreamingStatus(status);
            }
            catch
            {
                txtMediaStreamingStatus.Text = "Media streaming status could not be determined.";
                SetQuickMediaStreamingStatus("Media streaming status could not be determined.");
            }
        }

        private void SetMediaStreamingStatus(string text)
        {
            Dispatcher.Invoke(() =>
            {
                txtMediaStreamingStatus.Text = text;
                SetQuickMediaStreamingStatus(text);
            });
        }

        private static PowerShellResult GetNetworkProfileStatus()
        {
            return PowerShellHelper.Run(@"
$profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue)
foreach ($profile in $profiles) {
    'PROFILE|' + $profile.InterfaceIndex + '|' + $profile.NetworkCategory + '|' + $profile.Name
}");
        }

        private static PowerShellResult SetNetworkProfilesPrivate(IEnumerable<int> interfaceIndices)
        {
            string indices = string.Join(",", interfaceIndices.Distinct());
            if (string.IsNullOrWhiteSpace(indices))
            {
                return new PowerShellResult
                {
                    Success = false,
                    ExitCode = 1,
                    StdErr = "No network interface indices were supplied."
                };
            }

            return PowerShellHelper.Run($@"
$indices = @({indices})
foreach ($index in $indices) {{
    Set-NetConnectionProfile -InterfaceIndex $index -NetworkCategory Private -ErrorAction Stop
}}
'NETWORKS_PRIVATE'
");
        }

        private static (int InterfaceIndex, string Category, string Name)? ParseNetworkProfile(string line)
        {
            string[] parts = line.Split(new[] { '|' }, 4, StringSplitOptions.None);
            if (parts.Length != 4 || !int.TryParse(parts[1], out int interfaceIndex))
                return null;

            return (interfaceIndex, parts[2], parts[3]);
        }

        private static string? FindWmpConfigPath()
        {
            var result = PowerShellHelper.Run(@"
$candidates = @(
    (Join-Path $env:ProgramFiles 'Windows Media Player\wmpconfig.exe'),
    (Join-Path $env:SystemRoot 'System32\wmpconfig.exe')
)

$programFilesX86 = ${env:ProgramFiles(x86)}
if ($programFilesX86) {
    $candidates += (Join-Path $programFilesX86 'Windows Media Player\wmpconfig.exe')
}

$match = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if ($match) {
    'PATH|' + $match
}");

            string? line = SplitMediaOutput(result.StdOut)
                .FirstOrDefault(value => value.StartsWith("PATH|", StringComparison.OrdinalIgnoreCase));

            return line?.Substring("PATH|".Length);
        }

        private static PowerShellResult InstallWindowsMediaComponents()
        {
            return PowerShellHelper.Run(@"
function Find-WmpConfig {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Windows Media Player\wmpconfig.exe'),
        (Join-Path $env:SystemRoot 'System32\wmpconfig.exe')
    )
    $programFilesX86 = ${env:ProgramFiles(x86)}
    if ($programFilesX86) {
        $candidates += (Join-Path $programFilesX86 'Windows Media Player\wmpconfig.exe')
    }
    return ($candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1)
}

if (Find-WmpConfig) {
    'READY'
    return
}

$feature = Get-WindowsOptionalFeature -Online -FeatureName WindowsMediaPlayer -ErrorAction SilentlyContinue
if ($null -ne $feature) {
    if ($feature.State -eq 'EnablePending') {
        'REBOOT_REQUIRED'
        return
    }

    if ($feature.State -ne 'Enabled') {
        $result = Enable-WindowsOptionalFeature -Online -FeatureName WindowsMediaPlayer -All -NoRestart -ErrorAction Stop
        'ADDED|WindowsMediaPlayer'
        if ($result.RestartNeeded) {
            'REBOOT_REQUIRED'
            return
        }
    }
}

if (Find-WmpConfig) {
    'READY'
    return
}

$capabilityName = 'Media.MediaFeaturePack~~~~0.0.1.0'
$capability = Get-WindowsCapability -Online -Name $capabilityName -ErrorAction SilentlyContinue

if ($null -ne $capability -and $capability.State -ne 'Installed') {
    $result = Add-WindowsCapability -Online -Name $capabilityName -ErrorAction Stop
    'ADDED|MediaFeaturePack'
    if ($result.RestartNeeded) {
        'REBOOT_REQUIRED'
        return
    }
}

if (Find-WmpConfig) {
    'READY'
}
else {
    'NOT_READY'
}
");
        }

        private static PowerShellResult RunWmpConfig(string wmpConfigPath, string command)
        {
            string safePath = wmpConfigPath.Replace("'", "''");
            string safeCommand = command.Replace("'", "''");
            string successToken = command.Equals("HMEOff", StringComparison.OrdinalIgnoreCase)
                ? "STREAMING_DISABLED"
                : "STREAMING_ENABLED";

            return PowerShellHelper.Run($@"
$wmpConfig = '{safePath}'
if (-not (Test-Path -LiteralPath $wmpConfig)) {{
    throw 'wmpconfig.exe is not available.'
}}

& $wmpConfig '{safeCommand}'
if ($LASTEXITCODE -ne 0) {{
    throw ('wmpconfig exited with code ' + $LASTEXITCODE)
}}

if ('{safeCommand}' -eq 'HMEOn') {{
    $svc = Get-Service -Name WMPNetworkSvc -ErrorAction SilentlyContinue
    if ($null -ne $svc -and $svc.Status -ne 'Running') {{
        Start-Service -Name WMPNetworkSvc -ErrorAction Stop
    }}
}}

'{successToken}'
");
        }

        private static string[] SplitMediaOutput(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }
    }
}
