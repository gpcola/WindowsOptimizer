using System;
using System.Diagnostics;

namespace WindowsOptimizer
{
    public sealed class BenchmarkHelper
    {
        public sealed class MetricsSnapshot
        {
            public DateTime CapturedAt { get; init; } = DateTime.Now;
            public long DiskFreeCBytes { get; init; }
            public long DiskTotalCBytes { get; init; }
            public double DiskFreeCgb => DiskFreeCBytes / 1024d / 1024d / 1024d;
            public double DiskTotalCgb => DiskTotalCBytes / 1024d / 1024d / 1024d;
            public double AvailableMemoryMb { get; init; }
            public double TotalMemoryMbEstimate { get; init; }
            public int LogicalProcessors { get; init; }
            public string CpuHint { get; init; } = string.Empty;
        }

        public MetricsSnapshot CaptureMetrics()
        {
            return new MetricsSnapshot
            {
                CapturedAt = DateTime.Now,
                DiskFreeCBytes = DiskHelper.GetFreeSpaceBytes("C"),
                DiskTotalCBytes = DiskHelper.GetTotalSpaceBytes("C"),
                AvailableMemoryMb = GetAvailableMemoryMb(),
                TotalMemoryMbEstimate = GetTotalMemoryMbEstimate(),
                LogicalProcessors = Environment.ProcessorCount,
                CpuHint = GetCpuUsageHint()
            };
        }

        public string FormatSnapshot(MetricsSnapshot snapshot)
        {
            return
                $"Time: {snapshot.CapturedAt}{Environment.NewLine}" +
                $"Disk C: Free {snapshot.DiskFreeCgb:N2} GB / Total {snapshot.DiskTotalCgb:N2} GB{Environment.NewLine}" +
                $"RAM: Available {snapshot.AvailableMemoryMb:N0} MB / Estimated Total {snapshot.TotalMemoryMbEstimate:N0} MB{Environment.NewLine}" +
                $"Logical processors: {snapshot.LogicalProcessors}{Environment.NewLine}" +
                $"CPU note: {snapshot.CpuHint}";
        }

        public string TakeSnapshot() => FormatSnapshot(CaptureMetrics());

        public string Compare(string before, string after)
        {
            if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after))
            {
                return "Take both BEFORE and AFTER snapshots to compare. Automatic run summaries appear here after a completed maintenance run as well.";
            }

            return
                "Snapshot comparison is text-based in this version." + Environment.NewLine + Environment.NewLine +
                "BEFORE" + Environment.NewLine +
                "------" + Environment.NewLine +
                before + Environment.NewLine + Environment.NewLine +
                "AFTER" + Environment.NewLine +
                "-----" + Environment.NewLine +
                after;
        }

        public sealed class RunImpact
        {
            public long DiskFreeDeltaBytes { get; init; }
            public string Headline { get; init; } = string.Empty;
            public string Detail { get; init; } = string.Empty;
        }

        public RunImpact BuildRunImpact(MetricsSnapshot before, MetricsSnapshot after)
        {
            long freeDeltaBytes = after.DiskFreeCBytes - before.DiskFreeCBytes;
            const long measurementFloorBytes = 1024L * 1024L;

            string headline;
            if (freeDeltaBytes >= measurementFloorBytes)
            {
                headline = $"Reclaimed {FormatBytes(freeDeltaBytes)}";
            }
            else if (freeDeltaBytes <= -measurementFloorBytes)
            {
                headline = $"Net free space changed by -{FormatBytes(Math.Abs(freeDeltaBytes))}";
            }
            else
            {
                headline = "No measurable disk-space change";
            }

            string signedDelta = freeDeltaBytes switch
            {
                >= measurementFloorBytes => $"+{FormatBytes(freeDeltaBytes)}",
                <= -measurementFloorBytes => $"-{FormatBytes(Math.Abs(freeDeltaBytes))}",
                _ => "less than 1 MB"
            };

            string detail =
                $"C: free space {FormatBytes(before.DiskFreeCBytes)} → {FormatBytes(after.DiskFreeCBytes)} " +
                $"({signedDelta} net). Measured immediately before and after the run; normal Windows background activity can slightly affect this figure.";

            return new RunImpact
            {
                DiskFreeDeltaBytes = freeDeltaBytes,
                Headline = headline,
                Detail = detail
            };
        }

        public string BuildRunSummary(MetricsSnapshot before, MetricsSnapshot after, int appliedActionCount, bool rebootRecommended)
        {
            RunImpact impact = BuildRunImpact(before, after);
            double ramDeltaMb = Math.Round(after.AvailableMemoryMb - before.AvailableMemoryMb, 0);

            string ramLine = ramDeltaMb switch
            {
                > 0 => $"Available RAM increased by {ramDeltaMb:N0} MB.",
                < 0 => $"Available RAM decreased by {Math.Abs(ramDeltaMb):N0} MB.",
                _ => "Available RAM is unchanged at this measurement point."
            };

            return
                "Automatic post-run summary" + Environment.NewLine +
                "--------------------------" + Environment.NewLine +
                $"{impact.Headline}{Environment.NewLine}" +
                $"{impact.Detail}{Environment.NewLine}{Environment.NewLine}" +
                $"Applied actions: {appliedActionCount}{Environment.NewLine}" +
                $"Before: {before.CapturedAt:G}{Environment.NewLine}" +
                $"After:  {after.CapturedAt:G}{Environment.NewLine}{Environment.NewLine}" +
                ramLine + Environment.NewLine +
                $"Logical processors: {after.LogicalProcessors}{Environment.NewLine}" +
                $"CPU note: {after.CpuHint}{Environment.NewLine}{Environment.NewLine}" +
                (rebootRecommended
                    ? "Reboot recommended: yes. Complete the restart before reassessing performance."
                    : "Reboot recommended: no for the completed housekeeping/performance actions. Media component installation reports any restart requirement separately.");
        }

        private static string FormatBytes(long bytes)
        {
            double value = Math.Abs((double)bytes);

            if (value >= 1024d * 1024d * 1024d)
                return $"{value / 1024d / 1024d / 1024d:N2} GB";

            if (value >= 1024d * 1024d)
                return $"{value / 1024d / 1024d:N0} MB";

            if (value >= 1024d)
                return $"{value / 1024d:N0} KB";

            return $"{value:N0} bytes";
        }

        private string GetCpuUsageHint()
        {
            try
            {
                return "Use Task Manager or Performance Monitor for live CPU verification under workload.";
            }
            catch
            {
                return "CPU info not available.";
            }
        }

        private double GetAvailableMemoryMb()
        {
            try
            {
                using var pc = new PerformanceCounter("Memory", "Available MBytes");
                return Math.Round(pc.NextValue(), 0);
            }
            catch
            {
                return 0;
            }
        }

        private double GetTotalMemoryMbEstimate()
        {
            try
            {
                using var pc = new PerformanceCounter("Memory", "Commit Limit");
                return Math.Round(pc.NextValue() / 1024d / 1024d, 0);
            }
            catch
            {
                return 0;
            }
        }
    }
}
