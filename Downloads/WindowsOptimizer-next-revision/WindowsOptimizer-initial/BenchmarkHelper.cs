using System;
using System.Diagnostics;

namespace WindowsOptimizer
{
    public sealed class BenchmarkHelper
    {
        public sealed class MetricsSnapshot
        {
            public DateTime CapturedAt { get; init; } = DateTime.Now;
            public double DiskFreeCgb { get; init; }
            public double DiskTotalCgb { get; init; }
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
                DiskFreeCgb = DiskHelper.GetFreeSpaceGB("C"),
                DiskTotalCgb = DiskHelper.GetTotalSpaceGB("C"),
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
                return "Take both BEFORE and AFTER snapshots to compare. Automatic run summaries appear here after a completed run as well.";
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

        public string BuildRunSummary(MetricsSnapshot before, MetricsSnapshot after, int appliedActionCount, int successCount, int warningCount, bool rebootRecommended)
        {
            double freeDeltaGb = Math.Round(after.DiskFreeCgb - before.DiskFreeCgb, 2);
            double ramDeltaMb = Math.Round(after.AvailableMemoryMb - before.AvailableMemoryMb, 0);

            string freeLine = freeDeltaGb switch
            {
                > 0 => $"Disk C free space increased by {freeDeltaGb:N2} GB.",
                < 0 => $"Disk C free space decreased by {Math.Abs(freeDeltaGb):N2} GB.",
                _ => "Disk C free space is unchanged."
            };

            string ramLine = ramDeltaMb switch
            {
                > 0 => $"Available RAM increased by {ramDeltaMb:N0} MB.",
                < 0 => $"Available RAM decreased by {Math.Abs(ramDeltaMb):N0} MB.",
                _ => "Available RAM is unchanged at this measurement point."
            };

            return
                "Automatic post-run summary" + Environment.NewLine +
                "--------------------------" + Environment.NewLine +
                $"Applied actions: {appliedActionCount}{Environment.NewLine}" +
                $"Successful actions: {successCount}{Environment.NewLine}" +
                $"Actions with warnings or partial completion: {warningCount}{Environment.NewLine}" +
                $"Before: {before.CapturedAt:G}{Environment.NewLine}" +
                $"After:  {after.CapturedAt:G}{Environment.NewLine}{Environment.NewLine}" +
                $"Disk before: {before.DiskFreeCgb:N2} GB free of {before.DiskTotalCgb:N2} GB{Environment.NewLine}" +
                $"Disk after:  {after.DiskFreeCgb:N2} GB free of {after.DiskTotalCgb:N2} GB{Environment.NewLine}" +
                freeLine + Environment.NewLine +
                ramLine + Environment.NewLine +
                $"Logical processors: {after.LogicalProcessors}{Environment.NewLine}" +
                $"CPU note: {after.CpuHint}{Environment.NewLine}{Environment.NewLine}" +
                (rebootRecommended
                    ? "Reboot recommended: yes. Some applied changes, such as optional feature removal, pagefile changes, service-state changes, or hibernation changes, may not be fully reflected until after restart."
                    : "Reboot recommended: no immediate restart signal was detected from the selected actions.");
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
