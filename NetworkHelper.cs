using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;

namespace WindowsOptimizer
{
    public sealed class NetworkAdapterEntry
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Speed { get; init; } = string.Empty;
        public string DnsSuffix { get; init; } = string.Empty;
        public string IpSummary { get; init; } = string.Empty;

        public override string ToString()
        {
            return $"{Name} | {Type} | {Status} | {Speed}\n{Description}\n{IpSummary}";
        }
    }

    public static class NetworkHelper
    {
        public static List<NetworkAdapterEntry> GetAdapters()
        {
            var result = new List<NetworkAdapterEntry>();

            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                var props = adapter.GetIPProperties();
                var addresses = new List<string>();

                foreach (var address in props.UnicastAddresses)
                {
                    if (address?.Address == null) continue;
                    addresses.Add(address.Address.ToString());
                }

                result.Add(new NetworkAdapterEntry
                {
                    Name = adapter.Name,
                    Description = adapter.Description,
                    Type = adapter.NetworkInterfaceType.ToString(),
                    Status = adapter.OperationalStatus.ToString(),
                    Speed = FormatSpeed(adapter.Speed),
                    DnsSuffix = props.DnsSuffix ?? string.Empty,
                    IpSummary = string.Join(", ", addresses)
                });
            }

            return result;
        }

        public static string BuildNetworkSummary()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Network optimisation guidance");
            builder.AppendLine("- Review adapter speed and status first.");
            builder.AppendLine("- Prefer Ethernet for heavy transfers and backups.");
            builder.AppendLine("- Review Wi-Fi signal and band choice in Windows Settings.");
            builder.AppendLine("- Disconnect unused VPN profiles when diagnosing slow browsing.");
            builder.AppendLine("- Flush DNS after DNS, VPN, hosting, or network changes if name resolution appears stale.");
            return builder.ToString();
        }

        public static PowerShellResult FlushDns() => PowerShellHelper.Run("ipconfig /flushdns");
        public static PowerShellResult ShowIpConfiguration() => PowerShellHelper.Run("ipconfig /all");
        public static PowerShellResult ShowRoutes() => PowerShellHelper.Run("route print");

        private static string FormatSpeed(long bitsPerSecond)
        {
            if (bitsPerSecond <= 0) return "Unknown";
            double mbps = bitsPerSecond / 1_000_000d;
            if (mbps >= 1000) return $"{mbps / 1000:0.##} Gbps";
            return $"{mbps:0.##} Mbps";
        }
    }
}
