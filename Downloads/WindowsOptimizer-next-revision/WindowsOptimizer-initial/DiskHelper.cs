using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WindowsOptimizer
{
    public static class DiskHelper
    {
        public static double GetFreeSpaceGB(string driveLetter)
        {
            var drive = new DriveInfo(NormalizeDriveLetter(driveLetter));
            return Math.Round(drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0, 2);
        }

        public static double GetTotalSpaceGB(string driveLetter)
        {
            var drive = new DriveInfo(NormalizeDriveLetter(driveLetter));
            return Math.Round(drive.TotalSize / 1024.0 / 1024.0 / 1024.0, 2);
        }

        public static IEnumerable<string> GetFixedDriveLetters()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => d.Name.Substring(0, 1).ToUpperInvariant())
                .Distinct()
                .OrderBy(x => x);
        }

        public static bool DriveExists(string driveLetter)
        {
            string normalized = NormalizeDriveLetter(driveLetter);
            return DriveInfo.GetDrives().Any(d =>
                d.IsReady &&
                d.DriveType == DriveType.Fixed &&
                d.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeDriveLetter(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter))
                throw new ArgumentException("Drive letter is required.", nameof(driveLetter));

            driveLetter = driveLetter.Trim().TrimEnd('\\').TrimEnd(':');
            return driveLetter + @":\";
        }
    }
}
