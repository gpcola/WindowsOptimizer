using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowsOptimizer.Models
{
    public sealed class WslDistroEntry : INotifyPropertyChanged
    {
        private bool isSelectedForMove;
        private long vhdxSizeBytes;
        private long linuxUsedBytes;
        private long cacheEstimateBytes;
        private DateTime? lastSizeRefresh;

        public string Name { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public int Version { get; set; }
        public bool IsDefault { get; set; }
        public string InstallPath { get; set; } = string.Empty;
        public string VhdxPath { get; set; } = string.Empty;
        public string RegistryKey { get; set; } = string.Empty;
        public string DefaultUser { get; set; } = string.Empty;
        public string ConfigSummary { get; set; } = string.Empty;

        public bool IsSelectedForMove
        {
            get => isSelectedForMove;
            set => SetField(ref isSelectedForMove, value);
        }

        public long VhdxSizeBytes
        {
            get => vhdxSizeBytes;
            set
            {
                if (SetField(ref vhdxSizeBytes, value))
                {
                    OnPropertyChanged(nameof(VhdxSizeDisplay));
                }
            }
        }

        public long LinuxUsedBytes
        {
            get => linuxUsedBytes;
            set
            {
                if (SetField(ref linuxUsedBytes, value))
                {
                    OnPropertyChanged(nameof(LinuxUsedDisplay));
                }
            }
        }

        public long CacheEstimateBytes
        {
            get => cacheEstimateBytes;
            set
            {
                if (SetField(ref cacheEstimateBytes, value))
                {
                    OnPropertyChanged(nameof(CacheEstimateDisplay));
                }
            }
        }

        public DateTime? LastSizeRefresh
        {
            get => lastSizeRefresh;
            set
            {
                if (SetField(ref lastSizeRefresh, value))
                {
                    OnPropertyChanged(nameof(LastSizeRefreshDisplay));
                }
            }
        }

        public string DisplayName => IsDefault ? $"{Name} (default)" : Name;
        public string VersionDisplay => Version <= 0 ? "Unknown" : Version.ToString();
        public string VhdxSizeDisplay => FormatBytes(VhdxSizeBytes);
        public string LinuxUsedDisplay => LinuxUsedBytes > 0 ? FormatBytes(LinuxUsedBytes) : "Not scanned";
        public string CacheEstimateDisplay => CacheEstimateBytes > 0 ? FormatBytes(CacheEstimateBytes) : "Not scanned";
        public string LastSizeRefreshDisplay => LastSizeRefresh?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string? propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.##} {units[unit]}";
        }
    }
}
