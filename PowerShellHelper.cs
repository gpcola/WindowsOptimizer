using System;
using System.Diagnostics;
using System.Text;

namespace WindowsOptimizer
{
    public sealed class PowerShellResult
    {
        public bool Success { get; init; }
        public int ExitCode { get; init; }
        public string StdOut { get; init; } = string.Empty;
        public string StdErr { get; init; } = string.Empty;
    }

    public static class PowerShellHelper
    {
        public static PowerShellResult Run(string command)
        {
            string wrappedCommand =
                "$ProgressPreference='SilentlyContinue'; " +
                "$InformationPreference='SilentlyContinue'; " +
                "$ErrorActionPreference='Continue'; " +
                command;

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" +
                            wrappedCommand.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };

            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    stdOut.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;

                if (LooksLikePowerShellNoise(e.Data))
                    return;

                stdErr.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return new PowerShellResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                StdOut = stdOut.ToString().Trim(),
                StdErr = stdErr.ToString().Trim()
            };
        }

        private static bool LooksLikePowerShellNoise(string value)
        {
            return value.Contains("#< CLIXML", StringComparison.OrdinalIgnoreCase)
                || value.Contains("<Objs Version=", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Preparing modules for first use.", StringComparison.OrdinalIgnoreCase);
        }
    }
}
